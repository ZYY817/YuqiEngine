using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services.Latency
{
    internal static class NativeMethods
    {
        [DllImport("ntdll.dll")]
        public static extern int NtQueryTimerResolution(out uint MinRes, out uint MaxRes, out uint CurRes);

        [DllImport("kernel32.dll")]
        public static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

        [DllImport("kernel32.dll")]
        public static extern bool QueryPerformanceFrequency(out long lpFrequency);
    }

    public class LatencyTestService : ILatencyTestService
    {
        private static readonly string HistoryPath = Path.Combine(AppPaths.AppDataRoot, "LatencyHistory");

        // 维度权重 - 调整为更合理的分布
        private static readonly Dictionary<ResponseDimension, double> DimensionWeights = new()
        {
            { ResponseDimension.Scheduling, 0.20 },
            { ResponseDimension.DpcLatency, 0.18 },
            { ResponseDimension.TimerResolution, 0.08 },
            { ResponseDimension.MemoryBandwidth, 0.18 },
            { ResponseDimension.CacheEfficiency, 0.08 },
            { ResponseDimension.ThreadSync, 0.13 },
            { ResponseDimension.RenderReady, 0.15 }
        };

        // 评分阈值表 - 基于真实硬件数据
        // 格式: [电竞级100分, 优秀85分, 良好70分, 一般50分, 较差30分]
        private static readonly Dictionary<ResponseDimension, double[]> ScoreThresholds = new()
        {
            // 调度响应 (ms) - 越小越好
            { ResponseDimension.Scheduling, new[] { 0.05, 0.15, 0.3, 0.6, 1.5 } },
            // DPC延迟 (μs) - 越小越好
            { ResponseDimension.DpcLatency, new[] { 30.0, 80.0, 150.0, 300.0, 800.0 } },
            // 定时器精度 (ms) - 越小越好
            { ResponseDimension.TimerResolution, new[] { 0.5, 1.0, 5.0, 10.0, 15.6 } },
            // 内存带宽 (GB/s) - 越大越好
            { ResponseDimension.MemoryBandwidth, new[] { 35.0, 25.0, 18.0, 12.0, 6.0 } },
            // 缓存效率 (ns) - 越小越好
            { ResponseDimension.CacheEfficiency, new[] { 1.5, 2.5, 4.0, 7.0, 15.0 } },
            // 线程同步 (ms) - 越小越好
            { ResponseDimension.ThreadSync, new[] { 0.05, 0.15, 0.3, 0.6, 1.5 } },
            // 渲染就绪 (ms) - 越小越好
            { ResponseDimension.RenderReady, new[] { 0.3, 0.8, 1.5, 3.0, 8.0 } }
        };

        public async Task<ResponsivenessTestSession> RunResponsivenessTestAsync(
            int sampleCount, IProgress<(int current, string phase)>? progress = null, CancellationToken ct = default)
        {
            var session = new ResponsivenessTestSession { SampleCount = sampleCount };
            var totalPhases = 8; // 7个测试 + 1个分析
            var currentPhase = 0;

            // 预热阶段
            progress?.Report((5, "预热系统..."));
            await WarmupAsync(ct);

            // 1. 调度响应测试
            progress?.Report((++currentPhase * 100 / totalPhases, "测试调度响应..."));
            session.DimensionResults.Add(await MeasureSchedulingAsync(sampleCount, ct));

            // 2. 系统抖动测试 (替代原来的DPC延迟)
            progress?.Report((++currentPhase * 100 / totalPhases, "测试系统抖动..."));
            session.DimensionResults.Add(MeasureSystemJitter(sampleCount));

            // 3. 定时器分辨率
            progress?.Report((++currentPhase * 100 / totalPhases, "测试定时器精度..."));
            session.DimensionResults.Add(MeasureTimerResolution());

            // 4. 内存带宽
            progress?.Report((++currentPhase * 100 / totalPhases, "测试内存带宽..."));
            session.DimensionResults.Add(MeasureMemoryBandwidth(sampleCount));

            // 5. 缓存效率
            progress?.Report((++currentPhase * 100 / totalPhases, "测试缓存效率..."));
            session.DimensionResults.Add(MeasureCacheEfficiency(sampleCount));

            // 6. 线程同步
            progress?.Report((++currentPhase * 100 / totalPhases, "测试线程同步..."));
            session.DimensionResults.Add(await MeasureThreadSyncAsync(sampleCount, ct));

            // 7. 渲染就绪
            progress?.Report((++currentPhase * 100 / totalPhases, "测试渲染就绪..."));
            session.DimensionResults.Add(MeasureRenderReady(sampleCount));

            // 计算综合评分
            progress?.Report((95, "分析结果..."));
            CalculateOverallScore(session);
            DiagnoseBottlenecks(session);

            progress?.Report((100, "测试完成"));
            return session;
        }

        private async Task WarmupAsync(CancellationToken ct)
        {
            // CPU预热
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 200 && !ct.IsCancellationRequested)
            {
                double sum = 0;
                for (int i = 0; i < 10000; i++)
                    sum += Math.Sin(i * 0.001);
            }
            await Task.Delay(50, ct);
        }

        private async Task<DimensionResult> MeasureSchedulingAsync(int sampleCount, CancellationToken ct)
        {
            var samples = new List<double>();
            var actualSamples = Math.Max(sampleCount, 30); // 至少30次采样

            for (int i = 0; i < actualSamples; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                var sw = Stopwatch.StartNew();
                await Task.Yield(); // 让出CPU，测量重新调度时间
                sw.Stop();
                
                samples.Add(sw.Elapsed.TotalMilliseconds);
                
                // 随机间隔，模拟真实场景
                if (i % 5 == 0)
                    await Task.Delay(1, ct);
            }

            // 去除异常值（前5%和后5%）
            var trimmed = TrimOutliers(samples, 0.05);
            var median = GetMedian(trimmed);

            return new DimensionResult
            {
                Dimension = ResponseDimension.Scheduling,
                Name = "调度响应",
                Description = "线程调度延迟",
                RawValue = median,
                Unit = "ms",
                Samples = samples,
                Weight = DimensionWeights[ResponseDimension.Scheduling],
                Score = CalculateScore(ResponseDimension.Scheduling, median)
            };
        }

        private DimensionResult MeasureSystemJitter(int sampleCount)
        {
            var samples = new List<double>();
            var actualSamples = Math.Max(sampleCount * 2, 50);
            
            NativeMethods.QueryPerformanceFrequency(out long freq);
            var ticksPerUs = freq / 1_000_000.0;

            for (int i = 0; i < actualSamples; i++)
            {
                // 测量高精度计时器的抖动
                NativeMethods.QueryPerformanceCounter(out long start);
                
                // 执行一个精确的短延迟
                Thread.SpinWait(100);
                
                NativeMethods.QueryPerformanceCounter(out long end);
                
                var elapsedUs = (end - start) / ticksPerUs;
                
                // 理论上SpinWait(100)应该非常快，任何额外时间都是系统抖动
                // 基准值约为 0.5-2μs，超出部分为抖动
                var jitter = Math.Max(0, elapsedUs - 0.5);
                samples.Add(jitter);
                
                // 间隔采样
                if (i % 10 == 0)
                    Thread.Sleep(1);
            }

            var p95 = GetPercentile(samples, 95);
            
            return new DimensionResult
            {
                Dimension = ResponseDimension.DpcLatency,
                Name = "系统抖动",
                Description = "系统中断/DPC延迟",
                RawValue = p95,
                Unit = "μs",
                Samples = samples,
                Weight = DimensionWeights[ResponseDimension.DpcLatency],
                Score = CalculateScore(ResponseDimension.DpcLatency, p95)
            };
        }

        private DimensionResult MeasureTimerResolution()
        {
            double resolution = 15.625; // Windows默认
            try
            {
                if (NativeMethods.NtQueryTimerResolution(out _, out _, out uint current) == 0)
                    resolution = current / 10000.0;
            }
            catch { }

            return new DimensionResult
            {
                Dimension = ResponseDimension.TimerResolution,
                Name = "定时器精度",
                Description = "系统定时器分辨率",
                RawValue = resolution,
                Unit = "ms",
                Samples = new List<double> { resolution },
                Weight = DimensionWeights[ResponseDimension.TimerResolution],
                Score = CalculateScore(ResponseDimension.TimerResolution, resolution)
            };
        }

        private DimensionResult MeasureMemoryBandwidth(int sampleCount)
        {
            var samples = new List<double>();
            var bufferSize = 128 * 1024 * 1024; // 128MB - 超过L3缓存
            var buffer = new byte[bufferSize];
            var sw = new Stopwatch();
            var runs = Math.Max(3, Math.Min(sampleCount / 3, 8));

            // 预热
            for (int j = 0; j < bufferSize; j += 4096)
                buffer[j] = 1;

            for (int i = 0; i < runs; i++)
            {
                // 顺序写入测试
                sw.Restart();
                for (int j = 0; j < bufferSize; j += 64) // 64字节步进（缓存行大小）
                    buffer[j] = (byte)(j & 0xFF);
                sw.Stop();
                
                var bandwidth = (double)bufferSize / sw.Elapsed.TotalSeconds / (1024 * 1024 * 1024);
                samples.Add(bandwidth);
            }

            var avg = samples.Average();
            
            return new DimensionResult
            {
                Dimension = ResponseDimension.MemoryBandwidth,
                Name = "内存带宽",
                Description = "内存顺序访问速度",
                RawValue = avg,
                Unit = "GB/s",
                Samples = samples,
                Weight = DimensionWeights[ResponseDimension.MemoryBandwidth],
                Score = CalculateScore(ResponseDimension.MemoryBandwidth, avg)
            };
        }

        private DimensionResult MeasureCacheEfficiency(int sampleCount)
        {
            var samples = new List<double>();
            var cacheSize = 32 * 1024; // 32KB - L1缓存大小
            var buffer = new long[cacheSize / sizeof(long)];
            var sw = new Stopwatch();
            var iterations = 5_000_000;
            var runs = Math.Max(3, Math.Min(sampleCount / 3, 6));

            // 初始化
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = i;

            for (int r = 0; r < runs; r++)
            {
                sw.Restart();
                long sum = 0;
                for (int i = 0; i < iterations; i++)
                    sum += buffer[i & (buffer.Length - 1)]; // 位运算取模，更快
                sw.Stop();
                
                // 防止编译器优化掉循环
                if (sum == long.MinValue) Console.Write("");
                
                var nsPerAccess = sw.Elapsed.TotalNanoseconds / iterations;
                samples.Add(nsPerAccess);
            }

            var median = GetMedian(samples);
            
            return new DimensionResult
            {
                Dimension = ResponseDimension.CacheEfficiency,
                Name = "缓存效率",
                Description = "L1缓存访问延迟",
                RawValue = median,
                Unit = "ns",
                Samples = samples,
                Weight = DimensionWeights[ResponseDimension.CacheEfficiency],
                Score = CalculateScore(ResponseDimension.CacheEfficiency, median)
            };
        }

        private async Task<DimensionResult> MeasureThreadSyncAsync(int sampleCount, CancellationToken ct)
        {
            var samples = new List<double>();
            var actualSamples = Math.Max(sampleCount, 25);
            var sw = new Stopwatch();

            for (int i = 0; i < actualSamples; i++)
            {
                ct.ThrowIfCancellationRequested();
                
                using var signal = new ManualResetEventSlim(false);
                var workerTask = Task.Run(() => signal.Wait());

                try
                {
                    await Task.Delay(2, ct); // 确保worker线程已启动

                    sw.Restart();
                    signal.Set();
                    await workerTask.ConfigureAwait(false);
                    sw.Stop();

                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }
                finally
                {
                    // Cancellation can happen during the delay. Always release and join
                    // the worker before disposing its wait handle so no blocked pool task leaks.
                    signal.Set();
                    await workerTask.ConfigureAwait(false);
                }
            }

            var trimmed = TrimOutliers(samples, 0.1);
            var median = GetMedian(trimmed);
            
            return new DimensionResult
            {
                Dimension = ResponseDimension.ThreadSync,
                Name = "线程同步",
                Description = "线程间信号延迟",
                RawValue = median,
                Unit = "ms",
                Samples = samples,
                Weight = DimensionWeights[ResponseDimension.ThreadSync],
                Score = CalculateScore(ResponseDimension.ThreadSync, median)
            };
        }

        private DimensionResult MeasureRenderReady(int sampleCount)
        {
            var samples = new List<double>();
            var sw = new Stopwatch();
            var iterations = 50000; // 模拟一帧的CPU计算量
            var runs = Math.Max(sampleCount, 20);

            for (int r = 0; r < runs; r++)
            {
                sw.Restart();
                double sum = 0;
                for (int i = 0; i < iterations; i++)
                {
                    sum += Math.Sin(i * 0.0001) * Math.Cos(i * 0.0001);
                    sum += Math.Sqrt(i + 1);
                }
                sw.Stop();
                
                if (sum == double.MinValue) Console.Write(""); // 防优化
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            var median = GetMedian(samples);
            
            return new DimensionResult
            {
                Dimension = ResponseDimension.RenderReady,
                Name = "渲染就绪",
                Description = "CPU计算响应",
                RawValue = median,
                Unit = "ms",
                Samples = samples,
                Weight = DimensionWeights[ResponseDimension.RenderReady],
                Score = CalculateScore(ResponseDimension.RenderReady, median)
            };
        }

        // 非线性评分算法 - 基于阈值表插值
        private int CalculateScore(ResponseDimension dim, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            
            var thresholds = ScoreThresholds[dim];
            var scores = new[] { 100, 85, 70, 50, 30 };
            
            // 内存带宽是越大越好，其他都是越小越好
            bool higherIsBetter = dim == ResponseDimension.MemoryBandwidth;
            
            if (higherIsBetter)
            {
                // 越大越好：超过最高阈值得满分
                if (value >= thresholds[0]) return 100;
                if (value <= thresholds[4]) return Math.Max(10, (int)(value / thresholds[4] * 30));
                
                // 在阈值之间插值
                for (int i = 0; i < thresholds.Length - 1; i++)
                {
                    if (value >= thresholds[i + 1] && value < thresholds[i])
                    {
                        var ratio = (value - thresholds[i + 1]) / (thresholds[i] - thresholds[i + 1]);
                        return (int)(scores[i + 1] + ratio * (scores[i] - scores[i + 1]));
                    }
                }
            }
            else
            {
                // 越小越好：低于最低阈值得满分
                if (value <= thresholds[0]) return 100;
                if (value >= thresholds[4]) return Math.Max(10, (int)(thresholds[4] / value * 30));
                
                // 在阈值之间插值
                for (int i = 0; i < thresholds.Length - 1; i++)
                {
                    if (value > thresholds[i] && value <= thresholds[i + 1])
                    {
                        var ratio = (thresholds[i + 1] - value) / (thresholds[i + 1] - thresholds[i]);
                        return (int)(scores[i + 1] + ratio * (scores[i] - scores[i + 1]));
                    }
                }
            }
            
            return 50; // 默认中等分数
        }

        private void CalculateOverallScore(ResponsivenessTestSession session)
        {
            // 加权平均分
            double weightedSum = session.DimensionResults.Sum(d => d.Score * d.Weight);
            if (double.IsNaN(weightedSum)) weightedSum = 50;

            // 稳定性系数：基于各维度的变异系数
            var cvList = session.DimensionResults
                .Where(d => d.Average > 0.001 && d.Samples.Count > 1)
                .Select(d => d.StdDev / d.Average)
                .Where(cv => !double.IsNaN(cv) && cv < 10)
                .ToList();
            
            var avgCv = cvList.Count > 0 ? cvList.Average() : 0;
            session.StabilityCoefficient = Math.Max(0.75, Math.Min(1.0, 1 - avgCv * 0.25));

            // 尾部延迟系数：P99与中位数的比值
            var tailRatios = session.DimensionResults
                .Where(d => d.P50 > 0.001 && d.Samples.Count >= 5)
                .Select(d => d.P99 / d.P50)
                .Where(r => !double.IsNaN(r) && r > 0 && r < 100)
                .ToList();
            
            var avgTailRatio = tailRatios.Count > 0 ? tailRatios.Average() : 1;
            session.TailCoefficient = Math.Max(0.75, Math.Min(1.0, 2 - avgTailRatio));

            // 综合分 = 加权分 × 稳定性 × 尾部系数
            var rawScore = weightedSum * session.StabilityCoefficient * session.TailCoefficient;
            session.OverallScore = Math.Max(0, Math.Min(100, (int)Math.Round(rawScore)));

            // 评级
            session.Grade = session.OverallScore switch
            {
                >= 90 => PerformanceGrade.Elite,
                >= 75 => PerformanceGrade.Excellent,
                >= 60 => PerformanceGrade.Good,
                >= 45 => PerformanceGrade.Average,
                _ => PerformanceGrade.Poor
            };

            // 核心指标
            session.CoreMetrics = new CoreMetrics
            {
                SpeedScore = (int)Math.Round(session.DimensionResults.Average(d => d.Score)),
                StabilityScore = (int)(session.StabilityCoefficient * 100),
                TailScore = (int)(session.TailCoefficient * 100)
            };

            // 与理想值对比
            session.IdealComparison = Math.Max(0, 100 - session.OverallScore);
        }

        private void DiagnoseBottlenecks(ResponsivenessTestSession session)
        {
            foreach (var dim in session.DimensionResults.Where(d => d.Score < 75).OrderBy(d => d.Score))
            {
                var diagnosis = new BottleneckDiagnosis
                {
                    Dimension = dim.Dimension,
                    ImpactScore = (int)((75 - dim.Score) * dim.Weight * 100)
                };

                (diagnosis.Issue, diagnosis.Suggestion, diagnosis.Priority) = dim.Dimension switch
                {
                    ResponseDimension.Scheduling => (
                        $"调度延迟 {dim.RawValue:F2}ms",
                        "关闭后台程序，设置游戏进程高优先级，禁用节能模式",
                        dim.Score < 50 ? "高" : "中"),
                    ResponseDimension.DpcLatency => (
                        $"系统抖动 {dim.RawValue:F0}μs",
                        "更新驱动程序，禁用不必要设备，检查音频/网卡驱动",
                        dim.Score < 50 ? "高" : "中"),
                    ResponseDimension.TimerResolution => (
                        $"定时器精度 {dim.RawValue:F2}ms",
                        "游戏运行时会自动提高精度，或使用Timer Resolution工具",
                        "低"),
                    ResponseDimension.MemoryBandwidth => (
                        $"内存带宽 {dim.RawValue:F1}GB/s",
                        "启用XMP/DOCP，确保双通道，检查内存频率",
                        dim.Score < 50 ? "高" : "中"),
                    ResponseDimension.CacheEfficiency => (
                        $"缓存延迟 {dim.RawValue:F1}ns",
                        "关闭后台CPU密集程序，检查CPU温度",
                        "低"),
                    ResponseDimension.ThreadSync => (
                        $"线程同步 {dim.RawValue:F2}ms",
                        "关闭后台同步软件，设置CPU亲和性",
                        dim.Score < 50 ? "高" : "中"),
                    ResponseDimension.RenderReady => (
                        $"渲染准备 {dim.RawValue:F2}ms",
                        "检查CPU性能模式，关闭后台程序",
                        dim.Score < 50 ? "高" : "中"),
                    _ => ("未知问题", "请联系技术支持", "低")
                };

                session.Bottlenecks.Add(diagnosis);
            }
        }

        // 统计工具方法
        private static double GetMedian(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(x => x).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
        }

        private static double GetPercentile(List<double> values, int percentile)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
        }

        private static List<double> TrimOutliers(List<double> values, double trimRatio)
        {
            if (values.Count < 5) return values;
            var sorted = values.OrderBy(x => x).ToList();
            int trimCount = (int)(sorted.Count * trimRatio);
            return sorted.Skip(trimCount).Take(sorted.Count - 2 * trimCount).ToList();
        }

        // 兼容旧接口
        public async Task<LatencyTestResult> RunSingleTestAsync(CancellationToken ct = default)
        {
            var scheduling = await MeasureSchedulingAsync(5, ct);
            var dpc = MeasureSystemJitter(5);
            var render = MeasureRenderReady(5);
            
            return new LatencyTestResult
            {
                InputLatencyMs = scheduling.RawValue,
                DpcLatencyUs = dpc.RawValue,
                RenderLatencyMs = render.RawValue,
                DisplayLatencyMs = 1.0
            };
        }

        public async Task<LatencyTestSession> RunBatchTestAsync(int sampleCount, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            var session = new LatencyTestSession();
            
            var respProgress = new Progress<(int current, string phase)>(p => progress?.Report(p.current));
            var respSession = await RunResponsivenessTestAsync(sampleCount, respProgress, ct);
            session.ResponsivenessData = respSession;

            var scheduling = respSession.DimensionResults.FirstOrDefault(d => d.Dimension == ResponseDimension.Scheduling);
            var dpc = respSession.DimensionResults.FirstOrDefault(d => d.Dimension == ResponseDimension.DpcLatency);
            var render = respSession.DimensionResults.FirstOrDefault(d => d.Dimension == ResponseDimension.RenderReady);

            session.InputStats = scheduling != null ? LatencyStatistics.Calculate(scheduling.Samples) : null;
            session.DpcStats = dpc != null ? LatencyStatistics.Calculate(dpc.Samples) : null;
            session.RenderStats = render != null ? LatencyStatistics.Calculate(render.Samples) : null;
            session.DisplayStats = new LatencyStatistics { Average = 1.0, Min = 1.0, Max = 1.0, SampleCount = 1 };
            
            var totalSamples = new List<double>();
            if (scheduling != null) totalSamples.AddRange(scheduling.Samples.Select(s => s + 1.0));
            session.TotalStats = LatencyStatistics.Calculate(totalSamples);

            return session;
        }

        public LatencyStatistics CalculateStatistics(IReadOnlyList<LatencyTestResult> results, LatencyTestType type)
        {
            if (results == null || results.Count == 0) return new LatencyStatistics();
            var values = type switch
            {
                LatencyTestType.Input => results.Select(r => r.InputLatencyMs).ToList(),
                LatencyTestType.Render => results.Select(r => r.RenderLatencyMs).ToList(),
                LatencyTestType.Display => results.Select(r => r.DisplayLatencyMs).ToList(),
                _ => results.Select(r => r.TotalLatencyMs).ToList()
            };
            return LatencyStatistics.Calculate(values);
        }

        public IReadOnlyList<LatencyChainStep> GetLatencyChain(LatencyTestResult result)
        {
            var total = result.TotalLatencyMs;
            if (total <= 0) total = 1;
            return new List<LatencyChainStep>
            {
                new() { Name = "调度响应", Description = "线程调度", LatencyMs = result.InputLatencyMs, Percentage = result.InputLatencyMs / total * 100 },
                new() { Name = "系统抖动", Description = "DPC/ISR", LatencyMs = result.DpcLatencyUs / 1000, Percentage = result.DpcLatencyUs / 1000 / total * 100 },
                new() { Name = "渲染准备", Description = "CPU计算", LatencyMs = result.RenderLatencyMs, Percentage = result.RenderLatencyMs / total * 100 },
                new() { Name = "显示输出", Description = "显示器", LatencyMs = result.DisplayLatencyMs, Percentage = result.DisplayLatencyMs / total * 100 }
            };
        }

        public async Task SaveSessionAsync(LatencyTestSession session)
        {
            Directory.CreateDirectory(HistoryPath);
            var filePath = Path.Combine(HistoryPath, $"{session.Id}_{session.StartTime:yyyyMMdd_HHmmss}.json");
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<IReadOnlyList<LatencyTestSession>> LoadHistoryAsync(int maxCount = 10)
        {
            var sessions = new List<LatencyTestSession>();
            var readPath = Directory.Exists(HistoryPath)
                ? HistoryPath
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GameOptimizer", "LatencyHistory");
            if (!Directory.Exists(readPath)) return sessions;

            var files = Directory.GetFiles(readPath, "*.json")
                .OrderByDescending(f => File.GetCreationTime(f))
                .Take(maxCount);
                
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var session = JsonSerializer.Deserialize<LatencyTestSession>(json);
                    if (session != null) sessions.Add(session);
                }
                catch { }
            }
            return sessions;
        }

        public Task ClearHistoryAsync()
        {
            if (Directory.Exists(HistoryPath)) 
                Directory.Delete(HistoryPath, true);
            return Task.CompletedTask;
        }

        public async Task<string> ExportReportAsync(LatencyTestSession session)
        {
            var exportDir = AppPaths.ReportsExportDir;
            Directory.CreateDirectory(exportDir);
            var fileName = $"ResponsivenessReport_{session.StartTime:yyyyMMdd_HHmmss}.txt";
            var filePath = Path.Combine(exportDir, fileName);
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine("         GameOptimizer 系统响应性基准测试报告");
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"测试时间: {session.StartTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"采样次数: {session.ResponsivenessData?.SampleCount ?? 0}");
            sb.AppendLine();

            if (session.ResponsivenessData != null)
            {
                var resp = session.ResponsivenessData;
                sb.AppendLine($"综合评分: {resp.OverallScore}/100 {resp.GradeEmoji} {resp.GradeText}");
                sb.AppendLine();
                sb.AppendLine("───────────────────────────────────────────────────────────");
                sb.AppendLine("                      核心指标");
                sb.AppendLine("───────────────────────────────────────────────────────────");
                sb.AppendLine($"响应速度: {resp.CoreMetrics.SpeedScore}分 ({resp.CoreMetrics.SpeedGrade})");
                sb.AppendLine($"稳定性:   {resp.CoreMetrics.StabilityScore}分 ({resp.CoreMetrics.StabilityGrade})");
                sb.AppendLine($"极端表现: {resp.CoreMetrics.TailScore}分 ({resp.CoreMetrics.TailGrade})");
                sb.AppendLine();
                sb.AppendLine("───────────────────────────────────────────────────────────");
                sb.AppendLine("                      详细维度");
                sb.AppendLine("───────────────────────────────────────────────────────────");
                foreach (var dim in resp.DimensionResults)
                {
                    var bar = new string('█', dim.Score / 5) + new string('░', 20 - dim.Score / 5);
                    sb.AppendLine($"{dim.Name,-8} {bar} {dim.Score,3}分 ({dim.FormattedValue})");
                }

                if (resp.Bottlenecks.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("───────────────────────────────────────────────────────────");
                    sb.AppendLine("                      瓶颈诊断");
                    sb.AppendLine("───────────────────────────────────────────────────────────");
                    foreach (var b in resp.Bottlenecks)
                    {
                        sb.AppendLine($"[{b.Priority}] {b.Issue}");
                        sb.AppendLine($"    {b.Suggestion}");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine("              报告由 GameOptimizer 生成");
            sb.AppendLine("═══════════════════════════════════════════════════════════");

            await File.WriteAllTextAsync(filePath, sb.ToString());
            return filePath;
        }
    }
}
