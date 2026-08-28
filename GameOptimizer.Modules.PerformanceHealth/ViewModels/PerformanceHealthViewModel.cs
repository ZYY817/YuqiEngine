using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameOptimizer.Core.Events;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Modules.PerformanceHealth.ViewModels;

public partial class PerformanceHealthViewModel : ObservableObject, IDisposable
{
    private readonly ISystemInsightService _insightService;
    private readonly IEventBus? _eventBus;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    [ObservableProperty]
    private DetectedGame? activeGame;

    [ObservableProperty]
    private SystemResourceSnapshot snapshot = new(
        CpuUsage: 0,
        GpuUsage: 0,
        MemoryUsageGb: 0,
        MemoryTotalGb: 0,
        NetworkLatencyMs: 0,
        TemperatureGpu: 0,
        TemperatureCpu: 0,
        DiskUsedGb: 0,
        DiskTotalGb: 0,
        DpcLatencyUs: 0);

    [ObservableProperty]
    private int healthScore = 95;

    public ObservableCollection<OptimizationRecommendation> Recommendations { get; } = new();
    
    public string GameDisplayName => ActiveGame?.Name ?? LocalizationService.Instance.Get("PerfHealth.NoGame");
    public string GameStatusText => ActiveGame?.OptimizationStatus ?? LocalizationService.Instance.Get("PerfHealth.WaitingCheck");
    public string LblMemory => LocalizationService.Instance.Get("PerfHealth.Memory");
    public string LblLatency => LocalizationService.Instance.Get("PerfHealth.Latency");
    public string LblRecommendations => LocalizationService.Instance.Get("PerfHealth.Recommendations");
    public string BtnRefresh => LocalizationService.Instance.Get("PerfHealth.Refresh");

    public PerformanceHealthViewModel(ISystemInsightService insightService, IEventBus? eventBus = null)
    {
        _insightService = insightService;
        _eventBus = eventBus;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        RefreshState();
        
        // MC-008: 订阅优化完成事件，自动刷新健康评分
        if (_eventBus != null)
        {
            _ = SubscribeToOptimizationEventsAsync(_cts.Token);
        }
    }
    
    // MC-008: 订阅优化完成事件
    private async Task SubscribeToOptimizationEventsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _eventBus!.SubscribeAsync<OptimizationCompletedEvent>(ct))
            {
                Debug.WriteLine($"[PerformanceHealthVM] Received OptimizationCompletedEvent: {evt.ChangedCount} changes");
                RefreshState();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PerformanceHealthVM] Event subscription error: {ex.Message}");
        }
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        OnPropertyChanged(nameof(GameDisplayName));
        OnPropertyChanged(nameof(GameStatusText));
        OnPropertyChanged(nameof(LblMemory));
        OnPropertyChanged(nameof(LblLatency));
        OnPropertyChanged(nameof(LblRecommendations));
        OnPropertyChanged(nameof(BtnRefresh));
    }

    [RelayCommand]
    private void RefreshState()
    {
        ActiveGame = _insightService.GetActiveGame();
        Snapshot = _insightService.GetCurrentSnapshot();
        
        // MOD-009: 基于实际数据计算健康分数，而非随机数
        HealthScore = CalculateHealthScore();
        
        OnPropertyChanged(nameof(GameDisplayName));
        OnPropertyChanged(nameof(GameStatusText));

        Recommendations.Clear();
        foreach (var recommendation in _insightService.GetRecommendations())
        {
            Recommendations.Add(recommendation);
        }
    }
    
    private int CalculateHealthScore()
    {
        var score = 100;
        
        // CPU使用率过高扣分
        if (Snapshot.CpuUsage > 90) score -= 20;
        else if (Snapshot.CpuUsage > 70) score -= 10;
        
        // 内存使用率过高扣分
        var memoryUsagePercent = Snapshot.MemoryTotalGb > 0 
            ? Snapshot.MemoryUsageGb / Snapshot.MemoryTotalGb * 100 
            : 0;
        if (memoryUsagePercent > 90) score -= 15;
        else if (memoryUsagePercent > 75) score -= 8;
        
        // 温度过高扣分
        if (Snapshot.TemperatureCpu > 85) score -= 15;
        else if (Snapshot.TemperatureCpu > 70) score -= 5;
        
        // 网络延迟过高扣分
        if (Snapshot.NetworkLatencyMs > 100) score -= 10;
        else if (Snapshot.NetworkLatencyMs > 50) score -= 5;
        
        return Math.Max(0, Math.Min(100, score));
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        // MC-008: 取消事件订阅
        _cts.Cancel();
        _cts.Dispose();
        
        // MOD-010: 取消订阅事件，防止内存泄漏
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        
        GC.SuppressFinalize(this);
    }
}
