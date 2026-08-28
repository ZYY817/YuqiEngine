// File: GameOptimizer.Core/Services/OptimizationGuardService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 守护服务事件参数
/// </summary>
public class GuardServiceEventArgs : EventArgs
{
    public List<string> RevertedOperations { get; set; } = new();
    public List<string> ReappliedOperations { get; set; } = new();
    public List<string> FailedOperations { get; set; } = new();
}

/// <summary>
/// 优化守护服务 - 防止系统自动恢复已应用的优化（增强版）
/// </summary>
public sealed class OptimizationGuardService : IDisposable
{
    private readonly IOptimizationEngine _engine;
    private readonly ILogger<OptimizationGuardService> _logger;
    private readonly System.Timers.Timer _monitorTimer;
    private readonly HashSet<string> _protectedOperations;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isEnabled;
    private bool _isMonitoring;
    private bool _isPaused; // 暂停标志
    private DateTime _lastCheckTime;
    private int _monitorIntervalSeconds = 30;
    
    // 事件
    public event EventHandler<GuardServiceEventArgs>? OperationsReverted;
#pragma warning disable CS0067 // Retained as a source-compatible no-op event for existing UI subscribers.
    public event EventHandler<GuardServiceEventArgs>? OperationsReapplied;
#pragma warning restore CS0067
    public event EventHandler<string>? StatusChanged;

    public OptimizationGuardService(
        IOptimizationEngine engine,
        IOperationRestoreStore restoreStore,
        ILogger<OptimizationGuardService> logger)
    {
        _engine = engine;
        ArgumentNullException.ThrowIfNull(restoreStore);
        _logger = logger;
        _protectedOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _lastCheckTime = DateTime.Now;
        
        _monitorTimer = new System.Timers.Timer(_monitorIntervalSeconds * 1000);
        _monitorTimer.Elapsed += OnMonitorTick;
        _monitorTimer.AutoReset = true;
    }
    
    /// <summary>
    /// 获取守护状态
    /// </summary>
    public bool IsEnabled => _isEnabled;
    
    /// <summary>
    /// 获取暂停状态
    /// </summary>
    public bool IsPaused => _isPaused;
    
    /// <summary>
    /// 获取最后检查时间
    /// </summary>
    public DateTime LastCheckTime => _lastCheckTime;
    
    /// <summary>
    /// 获取监控间隔（秒）
    /// </summary>
    public int MonitorIntervalSeconds => _monitorIntervalSeconds;
    
    /// <summary>
    /// 设置监控间隔
    /// </summary>
    public void SetMonitorInterval(int seconds)
    {
        if (seconds < 10 || seconds > 600) 
            throw new ArgumentOutOfRangeException(nameof(seconds), "间隔必须在10-600秒之间");
        
        _monitorIntervalSeconds = seconds;
        _monitorTimer.Interval = seconds * 1000;
        _logger.LogInformation($"[守护] 监控间隔已更新: {seconds}秒");
    }

    /// <summary>
    /// 启用守护功能
    /// </summary>
    public void Enable()
    {
        if (_isEnabled) return;
        
        _isEnabled = true;
        _isPaused = false;
        _monitorTimer.Start();
        _logger.LogInformation($"[守护] 优化守护已启用，监控间隔: {_monitorIntervalSeconds}秒");
        StatusChanged?.Invoke(this, "已启用");
    }

    /// <summary>
    /// 禁用守护功能
    /// </summary>
    public void Disable()
    {
        if (!_isEnabled) return;
        
        _isEnabled = false;
        _isPaused = false;
        _monitorTimer.Stop();
        _logger.LogInformation("[守护] 优化守护已禁用");
        StatusChanged?.Invoke(this, "已禁用");
    }
    
    /// <summary>
    /// 暂停守护（临时）
    /// </summary>
    public void Pause()
    {
        _isPaused = true;
        _logger.LogInformation("[守护] 守护服务已暂停");
        StatusChanged?.Invoke(this, "已暂停");
    }
    
    /// <summary>
    /// 恢复守护
    /// </summary>
    public void Resume()
    {
        _isPaused = false;
        _logger.LogInformation("[守护] 守护服务已恢复");
        StatusChanged?.Invoke(this, "已恢复");
    }

    /// <summary>
    /// 添加需要保护的优化项（只添加成功应用的项）
    /// </summary>
    public async Task AddProtectedOperationsAsync(IEnumerable<string> operationIds)
    {
        await _lock.WaitAsync();
        try
        {
            var addedCount = 0;
            foreach (var opId in operationIds)
            {
                if (string.IsNullOrEmpty(opId)) continue;
                
                if (_protectedOperations.Add(opId))
                {
                    addedCount++;
                    _logger.LogInformation($"[守护] 添加保护: {opId}");
                }
            }
            
            if (addedCount > 0)
            {
                _logger.LogInformation($"[守护] 已添加 {addedCount} 项到保护列表，当前总数: {_protectedOperations.Count}");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 移除保护的优化项
    /// </summary>
    public async Task RemoveProtectedOperationsAsync(IEnumerable<string> operationIds)
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var opId in operationIds)
            {
                if (_protectedOperations.Remove(opId))
                {
                    _logger.LogInformation($"[守护] 移除保护: {opId}");
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 清空所有保护项
    /// </summary>
    public async Task ClearProtectedOperationsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var count = _protectedOperations.Count;
            _protectedOperations.Clear();
            _logger.LogInformation($"[守护] 已清空所有保护项（共{count}项）");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 获取当前保护的优化项列表
    /// </summary>
    public async Task<List<string>> GetProtectedOperationsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return _protectedOperations.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 监控定时器回调
    /// </summary>
    private async void OnMonitorTick(object? sender, ElapsedEventArgs e)
    {
        if (!_isEnabled || _isMonitoring || _isPaused) return;

        _isMonitoring = true;
        try
        {
            _lastCheckTime = DateTime.Now;
            await CheckNowAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[守护] 监控检查异常");
        }
        finally
        {
            _isMonitoring = false;
        }
    }

    /// <summary>
    /// 检查受保护项是否被外部修改。
    /// 守护服务只负责检测和通知；任何系统修改必须由用户发起并经过统一执行链。
    /// </summary>
    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync();
        try
        {
            if (_protectedOperations.Count == 0) return;

            _logger.LogDebug($"[守护] 开始检查 {_protectedOperations.Count} 项保护优化");

            var revertedItems = new List<string>();
            var checkTasks = new List<Task<(string opId, bool isReverted)>>();

            // 并发检查所有保护项。守护服务不执行重新应用，也不重试写入。
            foreach (var opId in _protectedOperations.ToList())
            {
                checkTasks.Add(CheckOperationStatusAsync(opId, cancellationToken));
            }

            // 等待所有检查完成（最多5秒）
            var checkResults = await Task.WhenAll(checkTasks);
            revertedItems = checkResults.Where(r => r.isReverted).Select(r => r.opId).ToList();

            if (revertedItems.Count > 0)
            {
                _logger.LogWarning("[守护] 检测到 {Count} 项保护设置已变化，等待用户确认后再处理", revertedItems.Count);
                
                // 仅通知 UI。不得在后台直接调用 IOptimizationOperation.ApplyAsync，
                // 因为这会绕过确认、权限、备份、回读和回执门禁。
                OperationsReverted?.Invoke(this, new GuardServiceEventArgs { RevertedOperations = revertedItems });
                StatusChanged?.Invoke(this, "检测到设置变化，等待用户确认");
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// 检查单个操作状态（带超时）
    /// </summary>
    private async Task<(string opId, bool isReverted)> CheckOperationStatusAsync(
        string opId,
        CancellationToken cancellationToken)
    {
        try
        {
            var operation = FindOperation(opId);
            if (operation == null) return (opId, false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var status = await operation.GetStatusAsync(cts.Token);
            
            if (!status.IsCompliant)
            {
                _logger.LogWarning($"[守护] 检测到被恢复: {opId}");
                return (opId, true);
            }
            
            return (opId, false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"[守护] 检查超时: {opId}");
            return (opId, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[守护] 检查状态失败: {opId}");
            return (opId, false);
        }
    }
    
    /// <summary>
    /// 查找优化操作
    /// </summary>
    private IOptimizationOperation? FindOperation(string operationId)
    {
        foreach (var module in _engine.Modules)
        {
            var operation = module.Operations.FirstOrDefault(op => 
                op.Id.Equals(operationId, StringComparison.OrdinalIgnoreCase));
            if (operation != null) return operation;
        }
        return null;
    }

    public void Dispose()
    {
        _monitorTimer?.Dispose();
        _lock?.Dispose();
    }
}
