using System;
using System.Collections.Generic;
using System.Linq;

namespace GameOptimizer.Core.Services.Latency
{
    // 测试维度枚举
    public enum ResponseDimension
    {
        Scheduling,      // 调度响应
        DpcLatency,      // DPC/ISR延迟
        TimerResolution, // 定时器分辨率
        MemoryBandwidth, // 内存带宽
        CacheEfficiency, // 缓存效率
        ThreadSync,      // 线程同步
        RenderReady      // 渲染就绪
    }

    // 评级等级
    public enum PerformanceGrade
    {
        Elite,      // 电竞级 95+
        Excellent,  // 优秀 85-94
        Good,       // 良好 70-84
        Average,    // 一般 50-69
        Poor        // 较差 <50
    }

    // 单个维度的测量结果
    public class DimensionResult
    {
        public ResponseDimension Dimension { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double RawValue { get; set; }
        public string Unit { get; set; } = string.Empty;
        public int Score { get; set; }
        public double Weight { get; set; }
        public List<double> Samples { get; set; } = new();
        
        // 统计数据
        public double Average => Samples.Count > 0 ? Samples.Average() : RawValue;
        public double Min => Samples.Count > 0 ? Samples.Min() : RawValue;
        public double Max => Samples.Count > 0 ? Samples.Max() : RawValue;
        public double P50 => GetPercentile(50);
        public double P99 => GetPercentile(99);
        public double StdDev => CalculateStdDev();
        
        public string FormattedValue => FormatValue();
        public string ScoreText => $"{Score}分";
        public double WeightedScore
        {
            get
            {
                var result = Score * Weight;
                return double.IsNaN(result) || double.IsInfinity(result) ? 0 : result;
            }
        }

        private double GetPercentile(int percentile)
        {
            if (Samples.Count == 0) return RawValue;
            var sorted = Samples.OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
        }

        private double CalculateStdDev()
        {
            if (Samples.Count < 2) return 0;
            var avg = Average;
            if (double.IsNaN(avg) || double.IsInfinity(avg)) return 0;
            var variance = Samples.Sum(x => (x - avg) * (x - avg)) / Samples.Count;
            var result = Math.Sqrt(variance);
            return double.IsNaN(result) || double.IsInfinity(result) ? 0 : result;
        }

        private string FormatValue()
        {
            return Dimension switch
            {
                ResponseDimension.Scheduling => $"{RawValue:F2}ms",
                ResponseDimension.DpcLatency => $"{RawValue:F0}μs",
                ResponseDimension.TimerResolution => $"{RawValue:F2}ms",
                ResponseDimension.MemoryBandwidth => $"{RawValue:F1}GB/s",
                ResponseDimension.CacheEfficiency => $"{RawValue:F1}ns",
                ResponseDimension.ThreadSync => $"{RawValue:F2}ms",
                ResponseDimension.RenderReady => $"{RawValue:F2}ms",
                _ => $"{RawValue:F2}"
            };
        }
    }

    // 核心指标（三大维度）
    public class CoreMetrics
    {
        public int SpeedScore { get; set; }        // 响应速度
        public int StabilityScore { get; set; }    // 稳定性
        public int TailScore { get; set; }         // 极端表现(P99)
        
        public string SpeedGrade => GetGradeText(SpeedScore);
        public string StabilityGrade => GetGradeText(StabilityScore);
        public string TailGrade => GetGradeText(TailScore);

        private static string GetGradeText(int score) => score switch
        {
            >= 90 => "极快",
            >= 75 => "快",
            >= 60 => "正常",
            >= 40 => "偏慢",
            _ => "慢"
        };
    }

    // 瓶颈诊断
    public class BottleneckDiagnosis
    {
        public ResponseDimension Dimension { get; set; }
        public string Issue { get; set; } = string.Empty;
        public string Suggestion { get; set; } = string.Empty;
        public string Priority { get; set; } = "中";
        public int ImpactScore { get; set; }
    }

    // 完整的测试会话结果
    public class ResponsivenessTestSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime TestTime { get; set; } = DateTime.Now;
        public int SampleCount { get; set; }
        
        // 综合评分
        public int OverallScore { get; set; }
        public PerformanceGrade Grade { get; set; }
        public string GradeText => Grade switch
        {
            PerformanceGrade.Elite => "电竞级",
            PerformanceGrade.Excellent => "优秀",
            PerformanceGrade.Good => "良好",
            PerformanceGrade.Average => "一般",
            _ => "较差"
        };
        public string GradeEmoji => Grade switch
        {
            PerformanceGrade.Elite => "🏆",
            PerformanceGrade.Excellent => "⭐",
            PerformanceGrade.Good => "✅",
            PerformanceGrade.Average => "⚠️",
            _ => "❌"
        };

        // 核心指标
        public CoreMetrics CoreMetrics { get; set; } = new();
        
        // 各维度详细结果
        public List<DimensionResult> DimensionResults { get; set; } = new();
        
        // 瓶颈诊断
        public List<BottleneckDiagnosis> Bottlenecks { get; set; } = new();
        
        // 系数
        public double StabilityCoefficient { get; set; } = 1.0;
        public double TailCoefficient { get; set; } = 1.0;

        // 与理想值对比
        public int IdealComparison { get; set; } // 与理想电竞PC的差距百分比
    }

    // 保留旧接口兼容性
    public enum LatencyTestType { Input, Render, Display, System }
    
    public class LatencyTestResult
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public double InputLatencyMs { get; set; }
        public double RenderLatencyMs { get; set; }
        public double DisplayLatencyMs { get; set; }
        public double DpcLatencyUs { get; set; }
        public double TotalLatencyMs => InputLatencyMs + RenderLatencyMs + DisplayLatencyMs + (DpcLatencyUs / 1000);
        public string? Notes { get; set; }
    }

    public class LatencyStatistics
    {
        public double Average { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double StdDev { get; set; }
        public int SampleCount { get; set; }

        public static LatencyStatistics Calculate(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0)
                return new LatencyStatistics();

            double sum = 0, min = double.MaxValue, max = double.MinValue;
            foreach (var v in values)
            {
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            double avg = sum / values.Count;
            double variance = 0;
            foreach (var v in values)
                variance += (v - avg) * (v - avg);
            variance /= values.Count;

            return new LatencyStatistics
            {
                Average = Math.Round(avg, 2),
                Min = Math.Round(min, 2),
                Max = Math.Round(max, 2),
                StdDev = Math.Round(Math.Sqrt(variance), 2),
                SampleCount = values.Count
            };
        }
    }

    public class LatencyTestSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime StartTime { get; set; } = DateTime.Now;
        public List<LatencyTestResult> Results { get; set; } = new();
        public LatencyStatistics? InputStats { get; set; }
        public LatencyStatistics? RenderStats { get; set; }
        public LatencyStatistics? DisplayStats { get; set; }
        public LatencyStatistics? DpcStats { get; set; }
        public LatencyStatistics? TotalStats { get; set; }
        
        // 新版数据
        public ResponsivenessTestSession? ResponsivenessData { get; set; }
    }

    public class LatencyChainStep
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double LatencyMs { get; set; }
        public double Percentage { get; set; }
    }
}
