using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class DisableWindowsTipsOperation : IOptimizationOperation
{
    private const string TipsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private static readonly MultiRegistryValueOperation DelegateOperation = new(
        id: "windows.disable_tips",
        title: "禁用 Windows 提示",
        description: "禁用 Windows 系统提示和推荐内容",
        category: "Windows 优化",
        definitions: new[]
        {
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.CurrentUser, TipsPath, "SoftLandingEnabled", 0, RegistryValueKind.DWord),
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.CurrentUser, TipsPath, "SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord),
        });

    public string Id => "windows.disable_tips";
    public string Title => "禁用 Windows 提示";
    public string Description => "禁用 Windows 系统提示和推荐内容";
    public string Category => "Windows 优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "减少系统干扰";
    public string? Drawback => "错过 Windows 使用技巧";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKCU";
    public string RegistryPath => TipsPath;
    public string RegistryValueName => "SoftLandingEnabled;SubscribedContent-338389Enabled";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.CurrentUser.OpenSubKey(TipsPath);
        var softLanding = key?.GetValue("SoftLandingEnabled") as int? ?? 1;
        var subscribed = key?.GetValue("SubscribedContent-338389Enabled") as int? ?? 1;
        var disabled = softLanding == 0 && subscribed == 0;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "Windows 提示已禁用" : "Windows 提示已启用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用 Windows 提示", "Windows 提示已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复 Windows 提示", "Windows 提示已恢复");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableSuggestedAppsOperation : IOptimizationOperation
{
    private const string CdmPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private static readonly MultiRegistryValueOperation DelegateOperation = new(
        id: "windows.disable_suggested_apps",
        title: "禁用建议应用",
        description: "禁用开始菜单和设置中的建议应用",
        category: "Windows 优化",
        definitions: new[]
        {
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.CurrentUser, CdmPath, "OemPreInstalledAppsEnabled", 0, RegistryValueKind.DWord),
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.CurrentUser, CdmPath, "PreInstalledAppsEnabled", 0, RegistryValueKind.DWord),
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.CurrentUser, CdmPath, "PreInstalledAppsEverEnabled", 0, RegistryValueKind.DWord),
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.CurrentUser, CdmPath, "SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord),
        });

    public string Id => "windows.disable_suggested_apps";
    public string Title => "禁用建议应用";
    public string Description => "禁用开始菜单和设置中的建议应用";
    public string Category => "Windows 优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "减少开始菜单干扰";
    public string? Drawback => "无法接收应用推荐";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKCU";
    public string RegistryPath => CdmPath;
    public string RegistryValueName => "OemPreInstalledAppsEnabled;PreInstalledAppsEnabled;PreInstalledAppsEverEnabled;SilentInstalledAppsEnabled";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.CurrentUser.OpenSubKey(CdmPath);
        var disabled = (key?.GetValue("OemPreInstalledAppsEnabled") as int? ?? 1) == 0
            && (key?.GetValue("PreInstalledAppsEnabled") as int? ?? 1) == 0
            && (key?.GetValue("PreInstalledAppsEverEnabled") as int? ?? 1) == 0
            && (key?.GetValue("SilentInstalledAppsEnabled") as int? ?? 1) == 0;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "建议应用已禁用" : "建议应用已启用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用建议应用", "建议应用已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复建议应用", "建议应用已恢复");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableClipboardHistoryOperation : IOptimizationOperation
{
    private const string ClipPath = @"SOFTWARE\Microsoft\Clipboard";

    private static readonly RegistryToggleOperation DelegateOperation = new(
        id: "windows.disable_clipboard_history",
        title: "禁用剪贴板历史",
        description: "禁用 Windows 剪贴板历史记录功能",
        category: "Windows 优化",
        hive: RegistryHive.CurrentUser,
        subKeyPath: ClipPath,
        valueName: "EnableClipboardHistory",
        desiredValue: 0,
        valueKind: RegistryValueKind.DWord);

    public string Id => "windows.disable_clipboard_history";
    public string Title => "禁用剪贴板历史";
    public string Description => "禁用 Windows 剪贴板历史记录功能";
    public string Category => "Windows 优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "保护剪贴板隐私";
    public string? Drawback => "无法使用 Win+V 历史记录";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKCU";
    public string RegistryPath => ClipPath;
    public string RegistryValueName => "EnableClipboardHistory";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.CurrentUser.OpenSubKey(ClipPath);
        var enabled = key?.GetValue("EnableClipboardHistory") as int? ?? 1;
        var disabled = enabled == 0;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "剪贴板历史已禁用" : "剪贴板历史已启用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用剪贴板历史", "剪贴板历史已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复剪贴板历史", "剪贴板历史已恢复");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableFocusAssistOperation : IOptimizationOperation
{
    private const string NotificationSettingsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    private static readonly RegistryToggleOperation DelegateOperation = new(
        id: "windows.disable_focus_assist",
        title: "禁用专注助手",
        description: "禁用 Windows 专注助手，确保游戏通知不被拦截",
        category: "Windows 优化",
        hive: RegistryHive.CurrentUser,
        subKeyPath: NotificationSettingsPath,
        valueName: "NOC_GLOBAL_SETTING_TOASTS_ENABLED",
        desiredValue: 1,
        valueKind: RegistryValueKind.DWord);

    public string Id => "windows.disable_focus_assist";
    public string Title => "禁用专注助手";
    public string Description => "禁用 Windows 专注助手，确保游戏通知不被拦截";
    public string Category => "Windows 优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "游戏通知不被拦截";
    public string? Drawback => "可能收到更多通知";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKCU";
    public string RegistryPath => NotificationSettingsPath;
    public string RegistryValueName => "NOC_GLOBAL_SETTING_TOASTS_ENABLED";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.CurrentUser.OpenSubKey(NotificationSettingsPath);
        var enabled = key?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") as int? ?? 1;
        var compliant = enabled == 1;
        return Task.FromResult(new OperationStatus(compliant, compliant ? "通知已启用" : "通知已禁用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用专注助手", "专注助手已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复专注助手", "专注助手已恢复");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableTimelineOperation : IOptimizationOperation
{
    private const string TimelinePath = @"SOFTWARE\Policies\Microsoft\Windows\System";

    private static readonly MultiRegistryValueOperation DelegateOperation = new(
        id: "windows.disable_timeline",
        title: "禁用时间线",
        description: "禁用 Windows 时间线/活动历史记录功能",
        category: "Windows 优化",
        definitions: new[]
        {
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.LocalMachine, TimelinePath, "EnableActivityFeed", 0, RegistryValueKind.DWord),
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.LocalMachine, TimelinePath, "PublishUserActivities", 0, RegistryValueKind.DWord),
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.LocalMachine, TimelinePath, "UploadUserActivities", 0, RegistryValueKind.DWord),
        });

    public string Id => "windows.disable_timeline";
    public string Title => "禁用时间线";
    public string Description => "禁用 Windows 时间线/活动历史记录功能";
    public string Category => "Windows 优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "保护活动历史隐私";
    public string? Drawback => "无法跨设备同步活动";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKLM";
    public string RegistryPath => TimelinePath;
    public string RegistryValueName => "EnableActivityFeed;PublishUserActivities;UploadUserActivities";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.OpenSubKey(TimelinePath);
        var disabled = (key?.GetValue("EnableActivityFeed") as int? ?? 1) == 0
            && (key?.GetValue("PublishUserActivities") as int? ?? 1) == 0
            && (key?.GetValue("UploadUserActivities") as int? ?? 1) == 0;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "时间线已禁用" : "时间线已启用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用时间线", "时间线已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复时间线", "时间线已恢复");
    }
}
