using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Services.Latency;

namespace GameOptimizer.Shell.ViewModels
{
    public partial class LatencyTestViewModel : ObservableObject
    {
        private readonly ILatencyTestService _latencyService;
        private CancellationTokenSource? _cts;

        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private int _progress;
        [ObservableProperty] private int _sampleCount = 20;
        [ObservableProperty] private string _statusText = "准备就绪";
        [ObservableProperty] private string _currentPhase = "";

        // 新版评分系统
        [ObservableProperty] private int _overallScore;
        [ObservableProperty] private string _gradeText = "";
        [ObservableProperty] private string _gradeEmoji = "";
        [ObservableProperty] private int _speedScore;
        [ObservableProperty] private int _stabilityScore;
        [ObservableProperty] private int _tailScore;
        [ObservableProperty] private string _speedGrade = "";
        [ObservableProperty] private string _stabilityGrade = "";
        [ObservableProperty] private string _tailGrade = "";
        [ObservableProperty] private int _idealComparison;

        // 旧版兼容
        [ObservableProperty] private double _lastInputLatency;
        [ObservableProperty] private double _lastRenderLatency;
        [ObservableProperty] private double _lastDisplayLatency;
        [ObservableProperty] private double _lastTotalLatency;
        [ObservableProperty] private double _lastDpcLatency;
        [ObservableProperty] private LatencyStatistics? _inputStats;
        [ObservableProperty] private LatencyStatistics? _renderStats;
        [ObservableProperty] private LatencyStatistics? _displayStats;
        [ObservableProperty] private LatencyStatistics? _totalStats;
        [ObservableProperty] private LatencyStatistics? _dpcStats;
        [ObservableProperty] private string _latencyRating = "";
        [ObservableProperty] private string _optimizationTip = "";
        [ObservableProperty] private bool _hasOptimizationTip;
        [ObservableProperty] private bool _showDetailedTips;
        [ObservableProperty] private string _showTipsButtonText = "展开详情";
        [ObservableProperty] private bool _showClearHistoryConfirm;
        [ObservableProperty] private bool _hasHistory;
        [ObservableProperty] private LatencyTestSession? _selectedHistoryItem;
        [ObservableProperty] private bool _showHistoryDetail;
        [ObservableProperty] private bool _canExport;
        [ObservableProperty] private bool _hasTestResult;

        public ObservableCollection<DimensionResult> DimensionResults { get; } = new();
        public ObservableCollection<BottleneckDiagnosis> Bottlenecks { get; } = new();
        public ObservableCollection<LatencyChainStep> LatencyChain { get; } = new();
        public ObservableCollection<LatencyTestSession> History { get; } = new();
        public ObservableCollection<LatencyOptimizationTip> OptimizationTips { get; } = new();

        public LatencyTestViewModel() : this(new LatencyTestService()) { }

        public LatencyTestViewModel(ILatencyTestService latencyService)
        {
            _latencyService = latencyService;
        }

        [RelayCommand(CanExecute = nameof(CanStartTest))]
        private async Task StartTestAsync()
        {
            IsRunning = true;
            Progress = 0;
            StatusText = "正在初始化测试...";
            _cts = new CancellationTokenSource();

            try
            {
                var progressReporter = new Progress<(int current, string phase)>(p =>
                {
                    Progress = p.current;
                    CurrentPhase = p.phase;
                    StatusText = p.phase;
                });

                var session = await _latencyService.RunBatchTestAsync(SampleCount, 
                    new Progress<int>(p => Progress = p), _cts.Token);

                if (session.ResponsivenessData != null)
                {
                    UpdateFromResponsivenessData(session.ResponsivenessData);
                }

                // 更新旧版数据
                InputStats = session.InputStats;
                RenderStats = session.RenderStats;
                DisplayStats = session.DisplayStats;
                DpcStats = session.DpcStats;
                TotalStats = session.TotalStats;

                if (session.InputStats != null)
                {
                    LastInputLatency = session.InputStats.Average;
                    LastTotalLatency = session.TotalStats?.Average ?? 0;
                }

                CanExport = true;
                HasTestResult = true;

                await _latencyService.SaveSessionAsync(session);
                await LoadHistoryAsync();

                StatusText = $"测试完成 - 综合评分: {OverallScore}/100 {GradeEmoji}";
            }
            catch (OperationCanceledException)
            {
                StatusText = "测试已取消";
            }
            catch (Exception ex)
            {
                StatusText = $"测试失败: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void UpdateFromResponsivenessData(ResponsivenessTestSession resp)
        {
            OverallScore = resp.OverallScore;
            GradeText = resp.GradeText;
            GradeEmoji = resp.GradeEmoji;
            SpeedScore = resp.CoreMetrics.SpeedScore;
            StabilityScore = resp.CoreMetrics.StabilityScore;
            TailScore = resp.CoreMetrics.TailScore;
            SpeedGrade = resp.CoreMetrics.SpeedGrade;
            StabilityGrade = resp.CoreMetrics.StabilityGrade;
            TailGrade = resp.CoreMetrics.TailGrade;
            IdealComparison = resp.IdealComparison;

            DimensionResults.Clear();
            foreach (var dim in resp.DimensionResults)
                DimensionResults.Add(dim);

            Bottlenecks.Clear();
            foreach (var b in resp.Bottlenecks)
                Bottlenecks.Add(b);

            // 更新优化建议
            OptimizationTips.Clear();
            foreach (var b in resp.Bottlenecks)
            {
                OptimizationTips.Add(new LatencyOptimizationTip
                {
                    Category = GetDimensionName(b.Dimension),
                    Issue = b.Issue,
                    Suggestion = b.Suggestion,
                    Priority = b.Priority
                });
            }
            HasOptimizationTip = OptimizationTips.Count > 0;
            OptimizationTip = HasOptimizationTip
                ? $"发现 {OptimizationTips.Count} 个可优化项"
                : "系统响应性良好，无需优化";

            // 更新评级
            LatencyRating = $"{resp.GradeEmoji} {resp.GradeText}";
        }

        private static string GetDimensionName(ResponseDimension dim) => dim switch
        {
            ResponseDimension.Scheduling => "调度响应",
            ResponseDimension.DpcLatency => "DPC延迟",
            ResponseDimension.TimerResolution => "定时器精度",
            ResponseDimension.MemoryBandwidth => "内存带宽",
            ResponseDimension.CacheEfficiency => "缓存效率",
            ResponseDimension.ThreadSync => "线程同步",
            ResponseDimension.RenderReady => "渲染就绪",
            _ => "未知"
        };

        private bool CanStartTest() => !IsRunning;

        [RelayCommand(CanExecute = nameof(CanStopTest))]
        private void StopTest()
        {
            _cts?.Cancel();
            StatusText = "正在取消...";
        }

        private bool CanStopTest() => IsRunning;

        [RelayCommand]
        private async Task LoadHistoryAsync()
        {
            History.Clear();
            var sessions = await _latencyService.LoadHistoryAsync();
            foreach (var s in sessions)
                History.Add(s);
            HasHistory = History.Count > 0;
        }

        [RelayCommand]
        private void RequestClearHistory()
        {
            if (!HasHistory) return;
            ShowClearHistoryConfirm = true;
            StatusText = $"⚠️ 确认清空 {History.Count} 条历史记录？";
        }

        [RelayCommand]
        private async Task ConfirmClearHistoryAsync()
        {
            ShowClearHistoryConfirm = false;
            await _latencyService.ClearHistoryAsync();
            History.Clear();
            HasHistory = false;
            StatusText = "✅ 历史记录已清除";
        }

        [RelayCommand]
        private void CancelClearHistory()
        {
            ShowClearHistoryConfirm = false;
            StatusText = "已取消清空操作";
        }

        [RelayCommand]
        private void ToggleTips()
        {
            ShowDetailedTips = !ShowDetailedTips;
            ShowTipsButtonText = ShowDetailedTips ? "收起详情" : "展开详情";
        }

        [RelayCommand]
        private async Task ExportReportAsync()
        {
            if (!HasTestResult)
            {
                StatusText = "⚠️ 请先完成一次测试";
                return;
            }

            try
            {
                StatusText = "正在导出报告...";
                var session = new LatencyTestSession
                {
                    InputStats = InputStats,
                    RenderStats = RenderStats,
                    DisplayStats = DisplayStats,
                    DpcStats = DpcStats,
                    TotalStats = TotalStats,
                    ResponsivenessData = new ResponsivenessTestSession
                    {
                        OverallScore = OverallScore,
                        Grade = OverallScore >= 95 ? PerformanceGrade.Elite :
                                OverallScore >= 85 ? PerformanceGrade.Excellent :
                                OverallScore >= 70 ? PerformanceGrade.Good :
                                OverallScore >= 50 ? PerformanceGrade.Average : PerformanceGrade.Poor,
                        CoreMetrics = new CoreMetrics
                        {
                            SpeedScore = SpeedScore,
                            StabilityScore = StabilityScore,
                            TailScore = TailScore
                        },
                        DimensionResults = DimensionResults.ToList(),
                        Bottlenecks = Bottlenecks.ToList(),
                        SampleCount = SampleCount
                    }
                };
                var path = await _latencyService.ExportReportAsync(session);
                StatusText = "✅ 报告已导出";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText = $"导出失败: {ex.Message}";
            }
        }

        [RelayCommand]
        private void SelectHistoryItem(LatencyTestSession? session)
        {
            if (session == null) return;

            if (SelectedHistoryItem == session && ShowHistoryDetail)
            {
                ShowHistoryDetail = false;
                SelectedHistoryItem = null;
                return;
            }

            SelectedHistoryItem = session;
            ShowHistoryDetail = true;

            if (session.ResponsivenessData != null)
            {
                UpdateFromResponsivenessData(session.ResponsivenessData);
            }

            InputStats = session.InputStats;
            RenderStats = session.RenderStats;
            DisplayStats = session.DisplayStats;
            DpcStats = session.DpcStats;
            TotalStats = session.TotalStats;
            HasTestResult = true;
            CanExport = true;

            StatusText = $"查看历史记录: {session.StartTime:MM-dd HH:mm}";
        }

        public void Initialize()
        {
            _ = LoadHistoryAsync();
        }
    }

    public class LatencyOptimizationTip
    {
        public string Category { get; set; } = string.Empty;
        public string Issue { get; set; } = string.Empty;
        public string Suggestion { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
    }
}
