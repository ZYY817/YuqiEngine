// File: GameOptimizer.Shell/Models/OptimizationState.cs
using System;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 优化项状态枚举 - 明确定义所有可能的状态
/// </summary>
public enum OptimizationState
{
    /// <summary>未应用（系统默认状态）</summary>
    NotApplied,
    
    /// <summary>待应用（用户已勾选，但未执行）</summary>
    PendingApply,
    
    /// <summary>应用中（正在执行优化操作）</summary>
    Applying,
    
    /// <summary>已应用（优化已生效）</summary>
    Applied,
    
    /// <summary>待恢复（用户已取消勾选，但未执行）</summary>
    PendingRestore,
    
    /// <summary>恢复中（正在执行恢复操作）</summary>
    Restoring,
    
    /// <summary>状态未知（检测失败或系统异常）</summary>
    Unknown
}

/// <summary>
/// 状态转换事件
/// </summary>
public enum StateTransitionEvent
{
    /// <summary>用户勾选</summary>
    UserEnabled,
    
    /// <summary>用户取消勾选</summary>
    UserDisabled,
    
    /// <summary>开始应用操作</summary>
    StartApply,
    
    /// <summary>应用成功</summary>
    ApplySucceeded,
    
    /// <summary>应用失败</summary>
    ApplyFailed,
    
    /// <summary>开始恢复操作</summary>
    StartRestore,
    
    /// <summary>恢复成功</summary>
    RestoreSucceeded,
    
    /// <summary>恢复失败</summary>
    RestoreFailed,
    
    /// <summary>系统状态同步</summary>
    SystemStateDetected
}

/// <summary>
/// 状态机 - 管理优化项的状态转换
/// </summary>
public class OptimizationStateMachine
{
    private OptimizationState _currentState;
    private readonly object _stateLock = new();
    
    public OptimizationState CurrentState
    {
        get { lock (_stateLock) return _currentState; }
        private set { lock (_stateLock) _currentState = value; }
    }
    
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    
    public OptimizationStateMachine(OptimizationState initialState = OptimizationState.NotApplied)
    {
        _currentState = initialState;
    }
    
    /// <summary>
    /// 触发状态转换
    /// </summary>
    public bool TryTransition(StateTransitionEvent transitionEvent, out string? errorMessage)
    {
        lock (_stateLock)
        {
            var oldState = _currentState;
            var newState = GetNextState(_currentState, transitionEvent);
            
            if (newState == _currentState)
            {
                errorMessage = $"Invalid transition: {transitionEvent} from {_currentState}";
                return false;
            }
            
            _currentState = newState;
            errorMessage = null;
            
            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, newState, transitionEvent));
            return true;
        }
    }
    
    /// <summary>
    /// 强制设置状态（仅用于系统状态同步）
    /// </summary>
    public void ForceSetState(OptimizationState newState, string reason)
    {
        lock (_stateLock)
        {
            var oldState = _currentState;
            if (oldState == newState) return;
            
            _currentState = newState;
            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, newState, StateTransitionEvent.SystemStateDetected, reason));
        }
    }
    
    /// <summary>
    /// 状态转换规则
    /// </summary>
    private static OptimizationState GetNextState(OptimizationState current, StateTransitionEvent transitionEvent)
    {
        return (current, transitionEvent) switch
        {
            // 用户勾选
            (OptimizationState.NotApplied, StateTransitionEvent.UserEnabled) => OptimizationState.PendingApply,
            (OptimizationState.Applied, StateTransitionEvent.UserEnabled) => OptimizationState.Applied, // 已应用，保持不变
            
            // 用户取消勾选
            (OptimizationState.PendingApply, StateTransitionEvent.UserDisabled) => OptimizationState.NotApplied,
            (OptimizationState.Applied, StateTransitionEvent.UserDisabled) => OptimizationState.PendingRestore,
            
            // 应用流程
            (OptimizationState.PendingApply, StateTransitionEvent.StartApply) => OptimizationState.Applying,
            // Why: ApplyByRiskLevel 等批量操作可能跳过 UserEnabled 步骤，直接从 NotApplied 发起 Apply
            (OptimizationState.NotApplied, StateTransitionEvent.StartApply) => OptimizationState.Applying,
            (OptimizationState.Applying, StateTransitionEvent.ApplySucceeded) => OptimizationState.Applied,
            (OptimizationState.Applying, StateTransitionEvent.ApplyFailed) => OptimizationState.PendingApply,
            
            // 恢复流程
            (OptimizationState.PendingRestore, StateTransitionEvent.StartRestore) => OptimizationState.Restoring,
            // Why: Restore_WindowsDefault 等批量恢复可能跳过 UserDisabled 步骤，直接从 Applied 发起 Restore
            (OptimizationState.Applied, StateTransitionEvent.StartRestore) => OptimizationState.Restoring,
            (OptimizationState.Restoring, StateTransitionEvent.RestoreSucceeded) => OptimizationState.NotApplied,
            (OptimizationState.Restoring, StateTransitionEvent.RestoreFailed) => OptimizationState.Applied, // 恢复失败，保持已应用状态
            
            // 系统状态同步（特殊处理）
            (_, StateTransitionEvent.SystemStateDetected) => current, // 由 ForceSetState 处理
            
            // 其他情况保持不变
            _ => current
        };
    }
    
    /// <summary>
    /// 判断是否有待处理的操作
    /// </summary>
    public bool HasPendingOperation => CurrentState is OptimizationState.PendingApply or OptimizationState.PendingRestore;
    
    /// <summary>
    /// 判断是否正在执行操作
    /// </summary>
    public bool IsOperating => CurrentState is OptimizationState.Applying or OptimizationState.Restoring;
}

/// <summary>
/// 状态变更事件参数
/// </summary>
public class StateChangedEventArgs : EventArgs
{
    public OptimizationState OldState { get; }
    public OptimizationState NewState { get; }
    public StateTransitionEvent TransitionEvent { get; }
    public string? Reason { get; }
    
    public StateChangedEventArgs(OptimizationState oldState, OptimizationState newState, StateTransitionEvent transitionEvent, string? reason = null)
    {
        OldState = oldState;
        NewState = newState;
        TransitionEvent = transitionEvent;
        Reason = reason;
    }
}
