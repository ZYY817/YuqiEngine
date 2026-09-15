using CommunityToolkit.Mvvm.ComponentModel;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Shell.Services;
using System;
using EngineOperationStatus = GameOptimizer.Core.Optimization.OperationStatus;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// UI-only presentation of the result returned by the existing typed capability/Engine probe.
/// This enum never grants execution rights; <see cref="CanApply"/> and <see cref="CanRestore"/>
/// remain the single execution gate.
/// </summary>
public enum ModuleCapabilityState
{
    Ready,
    Executing,
    AdminRequired,
    Unsupported,
    AlternativeAvailable,
    PolicyManaged,
    Conflict,
    ReadbackUnavailable,
    NotApplicable,
    ReadOnly,
    RecoveryUnavailable,
    Unknown
}

public partial class OptimizationToggleModel : ObservableObject
{
    private readonly string _titleFallback;
    private readonly string? _descriptionFallback;
    private readonly OptimizationStateMachine _stateMachine;
    private readonly System.Windows.Threading.Dispatcher? _uiDispatcher;
    
    // Why: powercfg 等系统命令执行后，注册表值可能延迟生效。
    // 操作完成后 3 秒内拒绝 SyncStatus 覆盖，防止状态回弹。
    private static readonly TimeSpan SyncCooldown = TimeSpan.FromSeconds(8);
    private DateTime _lastOperationCompletedAt = DateTime.MinValue;
    
    public OptimizationToggleModel(
        string title,
        string? description = null,
        bool isEnabled = false,
        bool isApplied = false,
        string? operationId = null,
        OperationRiskLevel riskLevel = OperationRiskLevel.Balanced,
        string? benefit = null,
        string? drawback = null,
        string? category = null,
        bool requiresReboot = false,
        string? valuePreview = null,
        string? registryPath = null,
        string? registryHiveText = null,
        string? impactWarning = null,
        string? registryValueName = null,
        string? optimizedValue = null,
        string? defaultValue = null,
        string? valueType = null,
        OperationType operationType = OperationType.Other,
        string? manualPath = null,
        string? manualSteps = null,
        string? targetName = null,
        string? targetValue = null,
        string? hardwareIncompatibleHint = null,
        string? runtimeStatusDetails = null,
        bool requiresAdmin = true,
        bool requiresExplicitConfirmation = false,
        bool requiresRestorePoint = false,
        bool affectsUserFeeling = false,
        OptimizationOperationRoute operationRoute = OptimizationOperationRoute.Unknown,
        bool requiresSnapshotForRestore = false,
        bool isAdministrator = true)
    {
        _titleFallback = title;
        _descriptionFallback = description;
        var applicationDispatcher = System.Windows.Application.Current?.Dispatcher;
        _uiDispatcher = applicationDispatcher?.CheckAccess() == true
            ? applicationDispatcher
            : null;
        
        // 初始化状态机
        var initialState = isApplied ? OptimizationState.Applied : OptimizationState.NotApplied;
        _stateMachine = new OptimizationStateMachine(initialState);
        _stateMachine.StateChanged += OnStateMachineStateChanged;
        
        this.isEnabled = isEnabled;
        this.isApplied = isApplied;
        OperationId = operationId;
        RiskLevel = riskLevel;
        Benefit = benefit;
        Drawback = drawback;
        Category = category;
        RequiresReboot = requiresReboot;
        ValuePreview = valuePreview;
        RegistryPath = registryPath;
        RegistryHiveText = registryHiveText;
        ImpactWarning = impactWarning;
        RegistryValueName = registryValueName;
        OptimizedValue = optimizedValue;
        DefaultValue = defaultValue;
        ValueType = valueType;
        OperationType = operationType;
        ManualPath = manualPath;
        ManualSteps = manualSteps;
        TargetName = targetName;
        TargetValue = targetValue;
        HardwareIncompatibleHint = hardwareIncompatibleHint;
        RuntimeStatusDetails = runtimeStatusDetails;
        RequiresAdmin = requiresAdmin;
        RequiresExplicitConfirmation = requiresExplicitConfirmation;
        RequiresRestorePoint = requiresRestorePoint;
        AffectsUserFeeling = affectsUserFeeling;
        OperationRoute = operationRoute;
        RequiresSnapshotForRestore = requiresSnapshotForRestore;
        _isAdministrator = isAdministrator;
        EvaluateCapabilities();
    }
    
    private void OnStateMachineStateChanged(object? sender, StateChangedEventArgs e)
    {
        // Why: 状态机事件可能在任意线程触发，必须确保UI属性更新在UI线程
        var dispatcher = _uiDispatcher;
        if (dispatcher == null)
        {
            // Why: 测试环境或应用程序未初始化时，直接同步更新属性
            System.Diagnostics.Debug.WriteLine($"[StateMachine] Dispatcher is null, updating properties synchronously for {OperationId ?? Title}");
            UpdatePropertiesFromState(e.NewState, e);
            return;
        }
        
        // PERF-P3: InvokeAsync替代Invoke，防止后台线程死等UI线程
        // Why: UI线程调用走CheckAccess同步分支不受影响；
        //      后台线程用InvokeAsync+Send优先级，仍然高优先执行但不阻塞调用方
        try
        {
            if (dispatcher.CheckAccess())
            {
                UpdatePropertiesFromState(e.NewState, e);
            }
            else
            {
                dispatcher.InvokeAsync(() => UpdatePropertiesFromState(e.NewState, e),
                    System.Windows.Threading.DispatcherPriority.Send);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StateMachine] Failed to update properties: {ex.Message}");
        }
    }
    
    private void UpdatePropertiesFromState(OptimizationState newState, StateChangedEventArgs e)
    {
        // 更新IsApplied
        var shouldBeApplied = newState is OptimizationState.Applied or OptimizationState.PendingRestore;
        if (IsApplied != shouldBeApplied)
        {
            IsApplied = shouldBeApplied;
        }
        
        // 更新IsEnabled
        // Why: 系统状态同步时，如果用户当前有待处理意图（PendingApply/PendingRestore），
        //      不应覆盖用户勾选状态，否则会出现“自动勾回/自动取消”的体验问题。
        var keepUserSelection =
            e.TransitionEvent == StateTransitionEvent.SystemStateDetected &&
            (e.OldState is OptimizationState.PendingApply or OptimizationState.PendingRestore);
        var shouldBeEnabled = keepUserSelection
            ? IsEnabled
            : (e.TransitionEvent == StateTransitionEvent.SystemStateDetected
                ? shouldBeApplied
                : newState is OptimizationState.PendingApply or OptimizationState.Applying or OptimizationState.Applied);
        if (IsEnabled != shouldBeEnabled)
        {
            IsEnabled = shouldBeEnabled;
        }
        
        System.Diagnostics.Debug.WriteLine($"[StateMachine] {OperationId ?? Title}: {e.OldState} -> {e.NewState} (Event: {e.TransitionEvent})");
    }

    public string Title => !string.IsNullOrEmpty(OperationId) 
        ? OptimizationTranslations.Instance.GetTitle(OperationId, _titleFallback, LocalizationService.Instance.CurrentLanguage != "zh-CN")
        : _titleFallback;
        
    public string? Description => !string.IsNullOrEmpty(OperationId) 
        ? OptimizationTranslations.Instance.GetDescription(OperationId, _descriptionFallback ?? "", LocalizationService.Instance.CurrentLanguage != "zh-CN")
        : _descriptionFallback;
    
    public string? WhyExplanation => !string.IsNullOrEmpty(OperationId) 
        ? OptimizationTranslations.Instance.GetWhy(OperationId, LocalizationService.Instance.CurrentLanguage != "zh-CN")
        : null;
    public string? OperationId { get; init; }
    public OperationRiskLevel RiskLevel { get; init; }
    public string? Category { get; init; }
    public bool RequiresReboot { get; init; }
    public string? ValuePreview { get; init; }
    public string? RegistryPath { get; init; }
    public string? RegistryHiveText { get; init; }
    public string? ImpactWarning { get; init; }
    public string? RegistryValueName { get; init; }
    public string? OptimizedValue { get; init; }
    public string? DefaultValue { get; init; }
    public string? ValueType { get; init; }
    public OperationType OperationType { get; init; }
    public string? ManualPath { get; init; }
    public string? ManualSteps { get; init; }
    public string? TargetName { get; init; }
    public string? TargetValue { get; init; }
    public bool RequiresAdmin { get; init; } = true;
    public bool RequiresExplicitConfirmation { get; init; }
    public bool RequiresRestorePoint { get; init; }
    public bool AffectsUserFeeling { get; init; }
    public OptimizationOperationRoute OperationRoute { get; init; }
    public bool RequiresSnapshotForRestore { get; init; }
    
    // Why: 硬件不兼容时显示提示文字，null 表示兼容
    public string? HardwareIncompatibleHint { get; init; }
    public bool IsHardwareIncompatible => !string.IsNullOrEmpty(HardwareIncompatibleHint);
    
    public bool HasRegistryInfo => !string.IsNullOrEmpty(RegistryPath);
    public bool HasManualInfo => !string.IsNullOrEmpty(ManualPath) || !string.IsNullOrEmpty(ManualSteps);
    public bool HasDetailInfo => HasRegistryInfo || HasManualInfo;
    public bool HasImpactWarning => !string.IsNullOrEmpty(ImpactWarning);
    
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool HasEffectInfo => !string.IsNullOrEmpty(Benefit) || !string.IsNullOrEmpty(Drawback);

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private bool isApplied;
    
    [ObservableProperty]
    private bool isUserApplied;  // 用户是否手动应用过此优化

    [ObservableProperty]
    private string? runtimeStatusDetails;

    [ObservableProperty]
    private bool canApply;

    [ObservableProperty]
    private bool canRestore;

    [ObservableProperty]
    private bool isReadbackKnown;

    [ObservableProperty]
    private string? blockedReason;

    [ObservableProperty]
    private OperationSupportState supportState = OperationSupportState.Supported;

    [ObservableProperty]
    private string? supportReasonCode;

    [ObservableProperty]
    private string? compatibilityAlternative;

    [ObservableProperty]
    private string? compatibilityVariantId;

    [ObservableProperty]
    private string? compatibilityVariantLabel;

    [ObservableProperty]
    private string? compatibilityMethod;

    [ObservableProperty]
    private string? compatibilityInformation;

    [ObservableProperty]
    private string? lastOperationReceipt;

    [ObservableProperty]
    private bool lastOperationSucceeded;

    private bool _hasKnownRestoreReceipt;
    private bool _isAdministrator;
    
    /// <summary>
    /// 获取当前状态机状态
    /// </summary>
    public OptimizationState CurrentState => _stateMachine.CurrentState;

    public bool IsPending => IsEnabled != IsApplied;

    public bool CanToggle => !_stateMachine.IsOperating && (CanApply || CanRestore);

    /// <summary>
    /// A localized, user-facing projection of the existing Engine/capability probe.
    /// It is deliberately derived from the execution gate, so an unknown probe can never look ready.
    /// </summary>
    public ModuleCapabilityState CapabilityState => ResolveCapabilityState();

    /// <summary>
    /// 已提权却仍收到 PermissionRequired：这是探测/文案误归因，不是「缺管理员」。
    /// </summary>
    private bool IsStalePermissionRequired =>
        SupportState == OperationSupportState.PermissionRequired && _isAdministrator;

    public string CapabilityStateLabel =>
        LocalizationService.Instance.Get($"Module.Capability.{CapabilityState}");

    public string CapabilityStatusReason
    {
        get
        {
            var loc = LocalizationService.Instance;
            var guidance = loc.Get($"Module.Capability.{CapabilityState}.Reason");
            return GetPlainCapabilityReason(BlockedReason, guidance);
        }
    }

    /// <summary>
    /// Retrying only repeats the existing read-only status probe. It never retries a system write.
    /// </summary>
    public bool CanRetryCapabilityProbe => CapabilityState is ModuleCapabilityState.ReadbackUnavailable
        or ModuleCapabilityState.Unknown
        or ModuleCapabilityState.Conflict;

    public bool HasCapabilityConstraint => CapabilityState != ModuleCapabilityState.Ready;

    // 行内只显示用户能采取行动的执行结果。能力探测的原始诊断保留在详情页，
    // 避免把同一段技术原因重复显示两次。
    public string? OperationFeedback => LastOperationReceipt;

    public bool HasOperationFeedback => !string.IsNullOrWhiteSpace(OperationFeedback);
    
    /// <summary>
    /// 是否显示"已应用"标签（只有用户手动应用过才显示）
    /// </summary>
    public bool ShowAppliedTag => IsApplied && IsUserApplied && IsEnabled;
    
    /// <summary>
    /// 用户勾选/取消勾选时调用
    /// </summary>
    public bool OnUserToggled(bool enabled)
    {
        if (_stateMachine.IsOperating)
        {
            return false;
        }

        if ((enabled && !CanApply) || (!enabled && !CanRestore))
        {
            RollbackIntentToObservedState("当前动作没有通过 Engine 能力门禁。");
            return false;
        }

        var transitionEvent = enabled ? StateTransitionEvent.UserEnabled : StateTransitionEvent.UserDisabled;
        if (!_stateMachine.TryTransition(transitionEvent, out var error))
        {
            System.Diagnostics.Debug.WriteLine($"[Toggle] 状态转换失败: {error}");
            RollbackIntentToObservedState(error);
            return false;
        }

        return true;
    }
    
    /// <summary>
    /// 开始应用操作
    /// </summary>
    public void OnStartApply()
    {
        _stateMachine.TryTransition(StateTransitionEvent.StartApply, out _);
        EvaluateCapabilities();
    }
    
    /// <summary>
    /// 应用成功
    /// </summary>
    public void OnApplySucceeded()
    {
        _stateMachine.TryTransition(StateTransitionEvent.ApplySucceeded, out _);
        _lastOperationCompletedAt = DateTime.UtcNow;
        IsUserApplied = true;
        OnPropertyChanged(nameof(ShowAppliedTag));
        EvaluateCapabilities();
    }
    
    /// <summary>
    /// 应用失败
    /// </summary>
    public void OnApplyFailed()
    {
        _stateMachine.TryTransition(StateTransitionEvent.ApplyFailed, out _);
        EvaluateCapabilities();
    }
    
    /// <summary>
    /// 开始恢复操作
    /// </summary>
    public void OnStartRestore()
    {
        _stateMachine.TryTransition(StateTransitionEvent.StartRestore, out _);
        EvaluateCapabilities();
    }
    
    /// <summary>
    /// 恢复成功
    /// </summary>
    public void OnRestoreSucceeded()
    {
        _stateMachine.TryTransition(StateTransitionEvent.RestoreSucceeded, out _);
        _lastOperationCompletedAt = DateTime.UtcNow;
        IsUserApplied = false;
        OnPropertyChanged(nameof(ShowAppliedTag));
        _hasKnownRestoreReceipt = false;
        EvaluateCapabilities();
    }
    
    /// <summary>
    /// 恢复失败
    /// </summary>
    public void OnRestoreFailed()
    {
        _stateMachine.TryTransition(StateTransitionEvent.RestoreFailed, out _);
        EvaluateCapabilities();
    }
    
    /// <summary>
    /// 同步系统状态（带保护机制）
    /// </summary>
    public void SyncStatus(bool systemIsApplied)
    {
        if (_stateMachine.IsOperating)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncStatus] 跳过同步 {OperationId ?? Title}：正在执行操作");
            return;
        }
        
        if (_stateMachine.HasPendingOperation)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncStatus] 跳过同步 {OperationId ?? Title}：有待处理操作");
            return;
        }
        
        // Why: powercfg 等命令执行后注册表值可能延迟生效，
        // 冷却期内 SyncStatus 读到的仍是旧值，会把状态错误地弹回去
        if (DateTime.UtcNow - _lastOperationCompletedAt < SyncCooldown)
        {
            System.Diagnostics.Debug.WriteLine($"[SyncStatus] 跳过同步 {OperationId ?? Title}：操作刚完成，冷却期内");
            return;
        }
        
        var targetState = systemIsApplied ? OptimizationState.Applied : OptimizationState.NotApplied;
        _stateMachine.ForceSetState(targetState, "系统状态同步");
    }

    public bool HasPending => IsPending;

    public string StateLabel => IsApplied ? LocalizationService.Instance.Get("Memory.Active") : LocalizationService.Instance.Get("Memory.Inactive");
    public string DisplayStatus => IsApplied ? LocalizationService.Instance.Get("Memory.Active") : (IsEnabled ? LocalizationService.Instance.Get("Memory.Pending") : LocalizationService.Instance.Get("Memory.Inactive"));
    
    public string RiskLevelText => RiskLevel switch
    {
        OperationRiskLevel.Extreme => LocalizationService.Instance.Get("Filter.HighRisk"),
        OperationRiskLevel.Balanced => LocalizationService.Instance.Get("Filter.Medium"),
        _ => LocalizationService.Instance.Get("Filter.Safe")
    };
    
    public string AppliedText => "✓ " + LocalizationService.Instance.Get("Memory.Active");
    
    public string RegInfoTooltip => LocalizationService.Instance.Get("Memory.ViewRegistryDetails");

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(DisplayStatus));
        OnPropertyChanged(nameof(ShowAppliedTag));
    }

    partial void OnIsAppliedChanged(bool value)
    {
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(DisplayStatus));
        OnPropertyChanged(nameof(AppliedText));
        OnPropertyChanged(nameof(ShowAppliedTag));
        EvaluateCapabilities();
    }
    
    partial void OnIsUserAppliedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAppliedTag));
    }

    public void CompleteApplyFromSummary(OperationExecutionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        UpdateRuntimeStatusDetails(OptimizationExecutionStateEvaluator.GetBestRuntimeDetails(summary));

        if (OptimizationExecutionStateEvaluator.DidApplyReachTarget(summary))
        {
            _hasKnownRestoreReceipt = summary.Changed;
            OnApplySucceeded();
            SetOperationReceipt(
                summary.Changed ? "应用成功，逐项回读已达到目标。" : "当前状态已符合目标，未产生新的恢复回执。",
                succeeded: true);
            return;
        }

        OnApplyFailed();
        RollbackIntentToSummaryState(summary.After.IsCompliant);
        var prefix = summary.Success ? "应用回执与目标回读不一致" : "应用失败";
        SetOperationReceipt($"{prefix}：{GetSummaryDetails(summary)}", succeeded: false);
    }

    public void CompleteRestoreFromSummary(OperationExecutionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        UpdateRuntimeStatusDetails(OptimizationExecutionStateEvaluator.GetBestRuntimeDetails(summary));

        if (OptimizationExecutionStateEvaluator.DidRestoreReachTarget(summary))
        {
            OnRestoreSucceeded();
            SetOperationReceipt("恢复成功，逐项回读已达到原状态。", succeeded: true);
            return;
        }

        OnRestoreFailed();
        RollbackIntentToSummaryState(summary.After.IsCompliant);
        var prefix = summary.Success ? "恢复回执与目标回读不一致" : "恢复失败";
        SetOperationReceipt($"{prefix}：{GetSummaryDetails(summary)}", succeeded: false);
    }

    public void CompleteApplyFailure(string reason)
    {
        OnApplyFailed();
        RollbackIntentToObservedState(reason);
        SetOperationReceipt($"应用失败：{NormalizeReason(reason)}", succeeded: false);
    }

    public void CompleteRestoreFailure(string reason)
    {
        OnRestoreFailed();
        RollbackIntentToObservedState(reason);
        SetOperationReceipt($"恢复失败：{NormalizeReason(reason)}", succeeded: false);
    }

    public void SyncOperationContract(EngineOperationStatus status, bool isAdministrator)
    {
        ArgumentNullException.ThrowIfNull(status);
        _isAdministrator = isAdministrator;
        SupportState = status.SupportState;
        SupportReasonCode = status.SupportReasonCode;
        CompatibilityAlternative = status.Alternative;
        CompatibilityVariantId = status.CompatibilityVariantId;
        CompatibilityVariantLabel = status.CompatibilityVariantLabel;
        CompatibilityMethod = status.CompatibilityMethod;
        CompatibilityInformation = status.CompatibilityInformation;
        UpdateRuntimeStatusDetails(status.Details);

        if (IsSyntheticEngineReadbackFailure(status))
        {
            MarkReadbackUnavailable(status.Details);
            return;
        }

        IsReadbackKnown = true;
        SyncStatus(status.IsCompliant);
        EvaluateCapabilities();
    }

    public void MarkReadbackUnavailable(string? reason)
    {
        IsReadbackKnown = false;
        RuntimeStatusDetails = TextDisplayNormalizer.NormalizeNullable(reason);
        RollbackIntentToObservedState(reason);
        EvaluateCapabilities();
    }

    public void RefreshPrivilege(bool isAdministrator)
    {
        _isAdministrator = isAdministrator;
        EvaluateCapabilities();
    }

    public void UpdateRuntimeStatusDetails(string? details)
    {
        RuntimeStatusDetails = TextDisplayNormalizer.NormalizeNullable(details);
    }

    private void EvaluateCapabilities()
    {
        try
        {
            CanApply = false;
            CanRestore = false;

            if (_stateMachine.IsOperating)
            {
                BlockedReason = "operation 正在执行，等待逐项回执与回读验证。";
                return;
            }

            // Why: 已提权进程上仍出现 PermissionRequired 属于误归因（过期探测/错误文案），
            // 不得把「需要管理员」当成真实阻断原因，否则用户已以管理员运行却仍见开关置灰。
            if (SupportState != OperationSupportState.Supported && !IsStalePermissionRequired)
            {
                BlockedReason = string.IsNullOrWhiteSpace(RuntimeStatusDetails)
                    ? LocalizationService.Instance.Get("Compatibility.UnknownBlock")
                    : RuntimeStatusDetails;
                return;
            }

            if (IsHardwareIncompatible)
            {
                BlockedReason = HardwareIncompatibleHint;
                return;
            }

            if (OperationRoute == OptimizationOperationRoute.ManagedFailClosed)
            {
                BlockedReason = string.IsNullOrWhiteSpace(RuntimeStatusDetails)
                    ? "该 operation 由 Provider 标记为 fail-closed，当前不允许执行。"
                    : RuntimeStatusDetails;
                return;
            }

            if (OperationRoute == OptimizationOperationRoute.Unknown)
            {
                BlockedReason = "Provider 未提供可识别的 typed Owner，operation 已安全阻断。";
                return;
            }

            if (!IsReadbackKnown)
            {
                BlockedReason = string.IsNullOrWhiteSpace(RuntimeStatusDetails)
                    ? "正在等待 Engine 返回可验证的系统状态。"
                    : $"系统状态回读不可用：{RuntimeStatusDetails}";
                return;
            }

            if (RequiresAdmin && !_isAdministrator)
            {
                BlockedReason = GameOptimizer.Core.Services.PermissionChecker.GetAdminRequiredMessage(
                    OperationId ?? Title);
                return;
            }

            if (!IsApplied)
            {
                CanApply = true;
                BlockedReason = null;
                return;
            }

            if (RequiresSnapshotForRestore && !_hasKnownRestoreReceipt)
            {
                BlockedReason = IsUserApplied
                    ? LocalizationService.Instance.Get("Module.Capability.RecoveryUnavailable.SessionReceipt")
                    : LocalizationService.Instance.Get("Module.Capability.RecoveryUnavailable.ExternalState");
                return;
            }

            CanRestore = true;
            BlockedReason = null;
        }
        finally
        {
            NotifyCapabilityPresentationChanged();
        }
    }

    private ModuleCapabilityState ResolveCapabilityState()
    {
        if (_stateMachine.IsOperating) return ModuleCapabilityState.Executing;
        if (SupportState == OperationSupportState.AlternativeAvailable) return ModuleCapabilityState.AlternativeAvailable;
        // Why: 已提权时 PermissionRequired 属误归因，不得再显示「需要管理员」；按已受支持继续评估。
        if (SupportState == OperationSupportState.PermissionRequired && !_isAdministrator)
        {
            return ModuleCapabilityState.AdminRequired;
        }
        if (SupportState == OperationSupportState.ManagedByPolicy) return ModuleCapabilityState.PolicyManaged;
        if (SupportState == OperationSupportState.Conflict) return ModuleCapabilityState.Conflict;
        if (SupportState == OperationSupportState.NotApplicable) return ModuleCapabilityState.NotApplicable;
        if (SupportState == OperationSupportState.Unsupported) return ModuleCapabilityState.Unsupported;
        if (SupportState == OperationSupportState.Unknown) return ModuleCapabilityState.Unknown;
        if (IsHardwareIncompatible) return ModuleCapabilityState.NotApplicable;

        if (OperationRoute == OptimizationOperationRoute.ManagedFailClosed)
        {
            if (ContainsAny(RuntimeStatusDetails, "policy", "策略")) return ModuleCapabilityState.PolicyManaged;
            if (ContainsAny(RuntimeStatusDetails, "unsupported", "not supported", "不支持")) return ModuleCapabilityState.Unsupported;
            if (ContainsAny(RuntimeStatusDetails, "conflict", "冲突")) return ModuleCapabilityState.Conflict;
            return ModuleCapabilityState.ReadOnly;
        }

        if (OperationRoute == OptimizationOperationRoute.Unknown) return ModuleCapabilityState.Unknown;
        if (!IsReadbackKnown) return ModuleCapabilityState.ReadbackUnavailable;
        if (RequiresAdmin && !_isAdministrator) return ModuleCapabilityState.AdminRequired;
        if (IsApplied && RequiresSnapshotForRestore && !_hasKnownRestoreReceipt) return ModuleCapabilityState.RecoveryUnavailable;
        return ModuleCapabilityState.Ready;
    }

    private static bool ContainsAny(string? value, params string[] tokens) =>
        !string.IsNullOrWhiteSpace(value)
        && tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private void NotifyCapabilityPresentationChanged()
    {
        OnPropertyChanged(nameof(CapabilityState));
        OnPropertyChanged(nameof(CapabilityStateLabel));
        OnPropertyChanged(nameof(CapabilityStatusReason));
        OnPropertyChanged(nameof(CanRetryCapabilityProbe));
        OnPropertyChanged(nameof(HasCapabilityConstraint));
    }

    private void RollbackIntentToObservedState(string? reason)
    {
        var observedIsApplied = IsApplied;
        _stateMachine.ForceSetState(
            observedIsApplied ? OptimizationState.Applied : OptimizationState.NotApplied,
            NormalizeReason(reason));
        IsApplied = observedIsApplied;
        IsEnabled = observedIsApplied;
        OnPropertyChanged(nameof(CanToggle));
    }

    private void RollbackIntentToSummaryState(bool isApplied)
    {
        _stateMachine.ForceSetState(
            isApplied ? OptimizationState.Applied : OptimizationState.NotApplied,
            "逐项回执失败后回滚 UI 意图到 After 回读状态。");
        IsApplied = isApplied;
        IsEnabled = isApplied;
        OnPropertyChanged(nameof(CanToggle));
    }

    private void SetOperationReceipt(string message, bool succeeded)
    {
        LastOperationSucceeded = succeeded;
        LastOperationReceipt = NormalizeReason(message);
        OnPropertyChanged(nameof(OperationFeedback));
        OnPropertyChanged(nameof(HasOperationFeedback));
    }

    private static string GetSummaryDetails(OperationExecutionSummary summary)
    {
        var details = OptimizationExecutionStateEvaluator.GetBestRuntimeDetails(summary);
        return NormalizeReason(string.IsNullOrWhiteSpace(details) ? summary.Message : details);
    }

    private static string NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Engine 未返回可验证原因。";
        }

        var normalized = reason.Trim();
        var diagnosticTail = normalized[(normalized.LastIndexOf('：') + 1)..].Trim();
        if (LocalizationService.Instance.CurrentLanguage == "zh-CN"
            && !ContainsCjk(diagnosticTail)
            && ContainsLatinLetter(diagnosticTail))
        {
            // Keep raw provider/exception details in diagnostics. The Chinese UI only receives
            // a localized outcome, never internal capability IDs or English timeout text.
            return CommandResultPresentationMapper
                .FromException(new InvalidOperationException(diagnosticTail))
                .DisplayText;
        }

        return normalized;
    }

    private static string GetPlainCapabilityReason(string? blockedReason, string fallback)
    {
        if (string.IsNullOrWhiteSpace(blockedReason))
        {
            return fallback;
        }

        // 运行时诊断会包含能力 ID、OEM 名称或内部命令。列表只告诉用户“能否修改”和“下一步”，
        // 技术原文仍可在详情页查看，便于排查而不增加日常阅读负担。
        if (ContainsAny(blockedReason, "当前电源计划未公开", "power plan does not expose"))
        {
            return LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "This power plan does not offer this option, so it cannot be changed safely."
                : "当前电源计划没有提供这一项，软件不会强行修改。";
        }

        if (ContainsAny(blockedReason, "OEM", "组织策略", "policy"))
        {
            return LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "This option is managed by your computer maker or a system policy, so it is left unchanged."
                : "这项设置由电脑厂商或系统策略管理，软件不会覆盖它。";
        }

        if (ContainsAny(blockedReason, "Modern Standby", "供电", "电源语义"))
        {
            return LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "This computer uses a restricted power mode. The app cannot change this option safely."
                : "这台电脑使用受限电源模式，软件无法安全修改这一项。";
        }

        if (ContainsAny(blockedReason, "当前 CPU", "当前处理器", "CPU 架构", "Intel", "AMD", "processor"))
        {
            return LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "This option does not apply to the current processor."
                : "这项功能不适用于当前处理器。";
        }

        if (ContainsAny(blockedReason, "没有可验证的执行与恢复链", "not execute"))
        {
            return LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "This option is not available until a safe change-and-restore process is ready."
                : "此功能尚未完成安全的修改与恢复验证，暂不开放编辑。";
        }

        return fallback;
    }

    private static bool ContainsCjk(string value) =>
        value.Any(character => character is >= '\u4E00' and <= '\u9FFF');

    private static bool ContainsLatinLetter(string value) =>
        value.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

    private static bool IsSyntheticEngineReadbackFailure(EngineOperationStatus status) =>
        !status.IsKnown
        || status.Details.StartsWith("Status check failed:", StringComparison.OrdinalIgnoreCase)
        || status.Details.StartsWith("Status check timed out", StringComparison.OrdinalIgnoreCase);

    partial void OnCanApplyChanged(bool value) => OnPropertyChanged(nameof(CanToggle));
    partial void OnCanRestoreChanged(bool value) => OnPropertyChanged(nameof(CanToggle));
    partial void OnIsReadbackKnownChanged(bool value) => OnPropertyChanged(nameof(CanToggle));
    partial void OnSupportStateChanged(OperationSupportState value)
    {
        EvaluateCapabilities();
    }

    partial void OnSupportReasonCodeChanged(string? value) => NotifyCapabilityPresentationChanged();
    partial void OnCompatibilityAlternativeChanged(string? value) => NotifyCapabilityPresentationChanged();
    partial void OnCompatibilityVariantIdChanged(string? value) => NotifyCapabilityPresentationChanged();
    partial void OnCompatibilityVariantLabelChanged(string? value) => NotifyCapabilityPresentationChanged();
    partial void OnCompatibilityMethodChanged(string? value) => NotifyCapabilityPresentationChanged();
    partial void OnCompatibilityInformationChanged(string? value) => NotifyCapabilityPresentationChanged();

    partial void OnBlockedReasonChanged(string? value)
    {
        OnPropertyChanged(nameof(OperationFeedback));
        OnPropertyChanged(nameof(HasOperationFeedback));
        OnPropertyChanged(nameof(CapabilityStatusReason));
    }

    partial void OnLastOperationReceiptChanged(string? value)
    {
        OnPropertyChanged(nameof(OperationFeedback));
        OnPropertyChanged(nameof(HasOperationFeedback));
    }
}
