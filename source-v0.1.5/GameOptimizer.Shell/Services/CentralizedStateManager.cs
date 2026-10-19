// File: GameOptimizer.Shell/Services/CentralizedStateManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 优化项状态数据结构
/// </summary>
public sealed class OptimizationItemState
{
    public bool IsSelected { get; init; }
    public bool IsApplied { get; init; }
    public DateTime LastModified { get; init; }
    
    public static OptimizationItemState Default => new()
    {
        IsSelected = false,
        IsApplied = false,
        LastModified = DateTime.UtcNow
    };
}

/// <summary>
/// 状态变更事件参数
/// </summary>
public sealed class StateChangedEventArgs : EventArgs
{
    public string ModuleKey { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public OptimizationItemState? OldState { get; init; }
    public OptimizationItemState NewState { get; init; } = OptimizationItemState.Default;
}

/// <summary>
/// 状态验证结果
/// Requirements: 20.2, 20.3
/// </summary>
public sealed class StateVerificationResult
{
    public bool IsConsistent { get; init; }
    public string? DiscrepancyDescription { get; init; }
    public object? ActualSystemValue { get; init; }
    public object? StoredStateValue { get; init; }
    
    public static StateVerificationResult Consistent() => new()
    {
        IsConsistent = true,
        DiscrepancyDescription = null
    };
    
    public static StateVerificationResult Inconsistent(
        string description, 
        object? actualValue, 
        object? storedValue) => new()
    {
        IsConsistent = false,
        DiscrepancyDescription = description,
        ActualSystemValue = actualValue,
        StoredStateValue = storedValue
    };
}

/// <summary>
/// 集中式状态管理器 - 单一数据源，线程安全
/// Requirements: 20.1, 20.2, 20.5, 20.6
/// </summary>
public sealed class CentralizedStateManager
{
    private readonly ConcurrentDictionary<string, OptimizationItemState> _states = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly IOptimizationOperationProvider? _operationProvider;
    
    /// <summary>
    /// 构造函数（支持依赖注入）
    /// </summary>
    public CentralizedStateManager(IOptimizationOperationProvider? operationProvider = null)
    {
        _operationProvider = operationProvider;
    }
    
    /// <summary>
    /// 状态变更事件（在UI线程触发）
    /// </summary>
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    
    /// <summary>
    /// 查询当前状态
    /// </summary>
    public OptimizationItemState GetState(string moduleKey, string operationId)
    {
        if (string.IsNullOrEmpty(moduleKey))
            throw new ArgumentException("Module key cannot be null or empty", nameof(moduleKey));
        if (string.IsNullOrEmpty(operationId))
            throw new ArgumentException("Operation ID cannot be null or empty", nameof(operationId));
        
        var key = BuildKey(moduleKey, operationId);
        return _states.GetValueOrDefault(key, OptimizationItemState.Default);
    }
    
    /// <summary>
    /// 更新单个状态（线程安全）
    /// </summary>
    public void UpdateState(string moduleKey, string operationId, OptimizationItemState newState)
    {
        if (string.IsNullOrEmpty(moduleKey))
            throw new ArgumentException("Module key cannot be null or empty", nameof(moduleKey));
        if (string.IsNullOrEmpty(operationId))
            throw new ArgumentException("Operation ID cannot be null or empty", nameof(operationId));
        if (newState == null)
            throw new ArgumentNullException(nameof(newState));
        
        var key = BuildKey(moduleKey, operationId);
        
        // PERF-P2: 加超时防止UI线程死等后台锁
        if (!_updateLock.Wait(TimeSpan.FromSeconds(3)))
        {
            System.Diagnostics.Debug.WriteLine("[CentralizedStateManager][UpdateState] 获取锁超时，跳过本次更新");
            return;
        }
        try
        {
            var oldState = _states.GetValueOrDefault(key);
            _states[key] = newState;
            
            // Why: 事件必须在UI线程触发，避免跨线程异常
            RaiseStateChangedOnUIThread(moduleKey, operationId, oldState, newState);
        }
        finally
        {
            _updateLock.Release();
        }
    }
    
    /// <summary>
    /// 批量更新状态（原子性）
    /// </summary>
    public void UpdateStates(string moduleKey, IReadOnlyDictionary<string, OptimizationItemState> updates)
    {
        if (string.IsNullOrEmpty(moduleKey))
            throw new ArgumentException("Module key cannot be null or empty", nameof(moduleKey));
        if (updates == null)
            throw new ArgumentNullException(nameof(updates));
        if (updates.Count == 0)
            return;
        
        // PERF-P2: 加超时防止UI线程死等后台锁
        if (!_updateLock.Wait(TimeSpan.FromSeconds(3)))
        {
            System.Diagnostics.Debug.WriteLine("[CentralizedStateManager][UpdateStates] 获取锁超时，跳过本次批量更新");
            return;
        }
        try
        {
            // Why: 原子性保证 - 第一阶段：验证所有输入，任何异常都不会导致部分更新
            foreach (var (operationId, newState) in updates)
            {
                if (string.IsNullOrEmpty(operationId))
                    throw new ArgumentException("Operation ID cannot be null or empty");
                if (newState == null)
                    throw new ArgumentNullException(nameof(newState), $"New state for {operationId} cannot be null");
            }
            
            // Why: 第二阶段：所有验证通过后，批量更新状态
            var changes = new List<(string operationId, OptimizationItemState? oldState, OptimizationItemState newState)>();
            
            foreach (var (operationId, newState) in updates)
            {
                var key = BuildKey(moduleKey, operationId);
                var oldState = _states.GetValueOrDefault(key);
                _states[key] = newState;
                
                changes.Add((operationId, oldState, newState));
            }
            
            // Why: 批量触发事件，减少UI线程调度次数
            foreach (var (operationId, oldState, newState) in changes)
            {
                RaiseStateChangedOnUIThread(moduleKey, operationId, oldState, newState);
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }
    
    /// <summary>
    /// 在UI线程触发StateChanged事件
    /// </summary>
    private void RaiseStateChangedOnUIThread(
        string moduleKey,
        string operationId,
        OptimizationItemState? oldState,
        OptimizationItemState newState)
    {
        if (StateChanged == null)
            return;
        
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            System.Diagnostics.Debug.WriteLine($"[StateManager] Dispatcher is null, skipping event for {moduleKey}:{operationId}");
            return;
        }
        
        // Why: 使用InvokeAsync确保事件在UI线程触发，避免跨线程异常
        _ = dispatcher.InvokeAsync(() =>
        {
            try
            {
                StateChanged?.Invoke(this, new StateChangedEventArgs
                {
                    ModuleKey = moduleKey,
                    OperationId = operationId,
                    OldState = oldState,
                    NewState = newState
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StateManager] Failed to raise StateChanged event: {ex.Message}");
            }
        }, System.Windows.Threading.DispatcherPriority.Normal);
    }
    
    /// <summary>
    /// 构建存储key
    /// </summary>
    private static string BuildKey(string moduleKey, string operationId)
    {
        return $"{moduleKey}:{operationId}";
    }
    
    /// <summary>
    /// 验证存储状态与实际系统状态的一致性
    /// Requirements: 20.2, 20.3
    /// </summary>
    /// <param name="moduleKey">模块标识</param>
    /// <param name="operationId">操作ID（对应IOptimizationOperation.Id）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>验证结果，包含一致性状态和差异描述</returns>
    public async Task<StateVerificationResult> VerifyStateAsync(
        string moduleKey, 
        string operationId, 
        CancellationToken cancellationToken = default)
    {
        // Guard: 输入验证
        if (string.IsNullOrEmpty(moduleKey))
            throw new ArgumentException("Module key cannot be null or empty", nameof(moduleKey));
        if (string.IsNullOrEmpty(operationId))
            throw new ArgumentException("Operation ID cannot be null or empty", nameof(operationId));
        
        // Guard: 检查是否注入了 OperationProvider
        if (_operationProvider == null)
        {
            return StateVerificationResult.Inconsistent(
                "无法验证状态：未注入 IOptimizationOperationProvider",
                actualValue: null,
                storedValue: null);
        }
        
        // 1. 获取存储状态
        var storedState = GetState(moduleKey, operationId);
        
        // 2. 查找对应的 Operation
        IOptimizationOperation? operation = null;
        try
        {
            var modules = _operationProvider.GetModules();
            foreach (var module in modules)
            {
                operation = module.Operations.FirstOrDefault(op => op.Id == operationId);
                if (operation != null)
                    break;
            }
            
            if (operation == null)
            {
                return StateVerificationResult.Inconsistent(
                    $"操作未找到: {operationId}",
                    actualValue: null,
                    storedValue: storedState.IsApplied);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StateManager] Failed to find operation {operationId}: {ex.Message}");
            return StateVerificationResult.Inconsistent(
                $"查找操作失败: {ex.Message}",
                actualValue: null,
                storedValue: storedState.IsApplied);
        }
        
        // 3. 查询实际系统状态
        OperationStatus actualStatus;
        try
        {
            actualStatus = await operation.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // 传播取消
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StateManager] Failed to get status for {operationId}: {ex.Message}");
            return StateVerificationResult.Inconsistent(
                $"无法查询系统状态: {ex.Message}",
                actualValue: null,
                storedValue: storedState.IsApplied);
        }
        
        // 4. 状态对比
        // IsCompliant=true 表示优化已生效，对应 IsApplied=true
        bool isConsistent = actualStatus.IsCompliant == storedState.IsApplied;
        
        if (isConsistent)
        {
            return StateVerificationResult.Consistent();
        }
        else
        {
            var description = $"状态不一致: 存储状态={storedState.IsApplied}, 实际状态={actualStatus.IsCompliant} ({actualStatus.Details})";
            return StateVerificationResult.Inconsistent(
                description,
                actualValue: actualStatus.IsCompliant,
                storedValue: storedState.IsApplied);
        }
    }
}
