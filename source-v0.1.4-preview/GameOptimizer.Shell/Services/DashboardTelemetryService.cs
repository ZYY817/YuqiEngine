// File: GameOptimizer.Shell/Services/DashboardTelemetryService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services;

public interface IDashboardTelemetryService : IDisposable
{
    SystemResourceSnapshot CurrentSnapshot { get; }
    IReadOnlyList<double> CpuTrendHistory { get; }
    IReadOnlyList<double> GpuTrendHistory { get; }
    string CpuTrendPath { get; }
    string GpuTrendPath { get; }
    
    event Action<SystemResourceSnapshot>? SnapshotUpdated;
    event Action? TrendUpdated;
    
    void Start(int intervalMs = 1000);
    void Stop();
}

public sealed class DashboardTelemetryService : IDashboardTelemetryService
{
    private readonly ISystemInsightService _insightService;
    private readonly Queue<double> _cpuTrendHistory = new();
    private readonly Queue<double> _gpuTrendHistory = new();
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;
    private bool _disposed;
    
    private const int TrendSampleCapacity = 48;
    private const double TrendCanvasWidth = 400d;
    private const double TrendCanvasHeight = 140d;

    public SystemResourceSnapshot CurrentSnapshot { get; private set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public IReadOnlyList<double> CpuTrendHistory => _cpuTrendHistory.ToArray();
    public IReadOnlyList<double> GpuTrendHistory => _gpuTrendHistory.ToArray();
    public string CpuTrendPath { get; private set; } = string.Empty;
    public string GpuTrendPath { get; private set; } = string.Empty;

    public event Action<SystemResourceSnapshot>? SnapshotUpdated;
    public event Action? TrendUpdated;

    public DashboardTelemetryService(ISystemInsightService insightService)
    {
        _insightService = insightService;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Start(int intervalMs = 1000)
    {
        if (_timer != null) return;
        
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        
        // 立即采集一次
        _ = CollectSnapshotAsync();
    }

    public void Stop()
    {
        if (_timer == null) return;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        await CollectSnapshotAsync();
    }

    private async Task CollectSnapshotAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => _insightService.GetCurrentSnapshot());
            CurrentSnapshot = snapshot;
            
            UpdateTrendHistory(snapshot.CpuUsage, _cpuTrendHistory);
            UpdateTrendHistory(snapshot.GpuUsage, _gpuTrendHistory);
            
            CpuTrendPath = BuildTrendPath(_cpuTrendHistory);
            GpuTrendPath = BuildTrendPath(_gpuTrendHistory);
            
            SnapshotUpdated?.Invoke(snapshot);
            TrendUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DashboardTelemetry] CollectSnapshot failed: {ex.Message}");
        }
    }

    private static void UpdateTrendHistory(double value, Queue<double> history)
    {
        history.Enqueue(value);
        while (history.Count > TrendSampleCapacity)
            history.Dequeue();
    }

    private static string BuildTrendPath(Queue<double> history)
    {
        if (history.Count < 2) return string.Empty;
        
        var points = history.ToArray();
        var sb = new StringBuilder();
        var stepX = TrendCanvasWidth / (TrendSampleCapacity - 1);
        
        for (var i = 0; i < points.Length; i++)
        {
            var x = i * stepX;
            var y = TrendCanvasHeight - (points[i] / 100.0 * TrendCanvasHeight);
            sb.Append(i == 0 ? $"M {x:F1},{y:F1}" : $" L {x:F1},{y:F1}");
        }
        
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
