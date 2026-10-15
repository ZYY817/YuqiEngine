using System.Security.Principal;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Core.Optimization.Operations;

public abstract class OrchestratedChangeOperation : IOptimizationOperation
{
    private readonly IChangeOrchestrator _orchestrator;
    private readonly IChangeConfirmationAuthority? _confirmationAuthority;
    private readonly TargetScopeKind _scopeKind;
    private readonly Func<string> _targetPayloadFactory;

    protected OrchestratedChangeOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        OperationType operationType,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        IChangeConfirmationAuthority? confirmationAuthority = null,
        bool requiresExplicitConfirmation = false,
        TargetScopeKind scopeKind = TargetScopeKind.CurrentUser,
        bool requiresAdministrator = false,
        Func<string>? targetPayloadFactory = null)
    {
        _orchestrator = orchestrator;
        _confirmationAuthority = confirmationAuthority;
        _scopeKind = scopeKind;
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        OperationType = operationType;
        RiskLevel = riskLevel;
        Benefit = benefit;
        Drawback = drawback;
        RequiresExplicitConfirmation = requiresExplicitConfirmation;
        RequiresAdmin = requiresAdministrator;
        _targetPayloadFactory = targetPayloadFactory ?? (() => "apply");
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; }
    public string? Benefit { get; }
    public string? Drawback { get; }
    public OperationType OperationType { get; }
    public virtual string? RegistryHiveText => null;
    public virtual string? RegistryPath => null;
    public virtual string? RegistryValueName => null;
    public virtual string? OptimizedValue => null;
    public virtual string? ValueType => null;
    public bool RequiresAdmin { get; }
    public bool RequiresExplicitConfirmation { get; }
    public virtual bool RequiresSnapshotForRestore => true;

    public virtual async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var result = await _orchestrator.PrepareAsync(
            new ChangeRequest(Id, CreateScope(), CreateTargetPayload()),
            cancellationToken,
            registerPlan: false).ConfigureAwait(false);
        return result.Status switch
        {
            ChangePreparationStatus.NoChange => new OperationStatus(true, result.Message),
            ChangePreparationStatus.Ready => new OperationStatus(false, "当前状态尚未应用该设置。"),
            _ => new OperationStatus(
                false,
                result.Message,
                IsKnown: true,
                SupportState: MapSupportState(result.ReasonCode),
                SupportReasonCode: result.ReasonCode,
                Alternative: IsAlternative(result.ReasonCode) ? result.Message : null)
        };
    }

    private static OperationSupportState MapSupportState(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode)) return OperationSupportState.Unknown;
        if (IsAlternative(reasonCode)) return OperationSupportState.AlternativeAvailable;
        if (reasonCode.StartsWith("permission.", StringComparison.OrdinalIgnoreCase)) return OperationSupportState.PermissionRequired;
        if (reasonCode.Contains("policy", StringComparison.OrdinalIgnoreCase)) return OperationSupportState.ManagedByPolicy;
        if (reasonCode.Contains("conflict", StringComparison.OrdinalIgnoreCase)
            || reasonCode.Contains("drift", StringComparison.OrdinalIgnoreCase)
            || reasonCode.Contains("busy", StringComparison.OrdinalIgnoreCase)) return OperationSupportState.Conflict;
        if (reasonCode.Contains("not-applicable", StringComparison.OrdinalIgnoreCase)
            || reasonCode.Contains("not-found", StringComparison.OrdinalIgnoreCase)
            || reasonCode.StartsWith("input.", StringComparison.OrdinalIgnoreCase)
            || reasonCode.StartsWith("device.", StringComparison.OrdinalIgnoreCase)) return OperationSupportState.NotApplicable;
        if (reasonCode.StartsWith("compatibility.", StringComparison.OrdinalIgnoreCase)
            || reasonCode.StartsWith("os.", StringComparison.OrdinalIgnoreCase)
            || reasonCode.StartsWith("scope.", StringComparison.OrdinalIgnoreCase)) return OperationSupportState.Unsupported;
        return OperationSupportState.Unknown;
    }

    private static bool IsAlternative(string reasonCode) =>
        reasonCode is "compatibility.os.feature-retired"
            or "compatibility.hardware.wddm-unknown"
            or "compatibility.hardware.hags-unknown"
            or "compatibility.method.manual-only";

    public virtual async Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        var preparation = await _orchestrator.PrepareAsync(
            new ChangeRequest(Id, CreateScope(), CreateTargetPayload()),
            cancellationToken).ConfigureAwait(false);
        if (preparation.Status == ChangePreparationStatus.NoChange)
        {
            return new OperationInvokeResult(true, false, preparation.Message);
        }

        if (preparation.Status != ChangePreparationStatus.Ready || preparation.Plan is null)
        {
            return new OperationInvokeResult(false, false, preparation.Message);
        }

        ConfirmationToken confirmation;
        if (preparation.Plan.RequiresExplicitConfirmation)
        {
            if (_confirmationAuthority is null)
            {
                return new OperationInvokeResult(false, false, "显式确认授权服务未接入，操作已安全阻断。");
            }

            var issued = await _confirmationAuthority.RequestAsync(
                preparation.Plan,
                Title,
                $"{Description}\n\n影响：{Drawback ?? "请确认变更影响。"}",
                cancellationToken).ConfigureAwait(false);
            if (issued is null)
            {
                return new OperationInvokeResult(false, false, "用户未确认或确认计划已过期，未执行任何写入。");
            }

            confirmation = issued;
        }
        else
        {
            confirmation = ConfirmationToken.ForPreparedPlan(preparation.Plan);
        }

        var receipt = await _orchestrator.ExecuteAsync(
            preparation.Plan,
            confirmation,
            cancellationToken).ConfigureAwait(false);
        if (receipt.Outcome == ChangeOutcome.SucceededVerified)
        {
            await SavePointerAsync(context, preparation.Plan, receipt.Operation, cancellationToken).ConfigureAwait(false);
            return new OperationInvokeResult(true, true, receipt.Operation.Message);
        }

        if (receipt.Outcome == ChangeOutcome.NoChange)
        {
            return new OperationInvokeResult(true, false, receipt.Operation.Message);
        }

        if (receipt.Operation.MutationOccurred && receipt.Operation.RecoveryId is not null)
        {
            await SavePointerAsync(context, preparation.Plan, receipt.Operation, CancellationToken.None).ConfigureAwait(false);
        }

        return new OperationInvokeResult(false, receipt.Operation.MutationOccurred, receipt.Operation.Message);
    }

    public virtual async Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        var pointer = await context.LoadSnapshotAsync<OptimizationRecoveryPointer>(Id, cancellationToken).ConfigureAwait(false);
        if (pointer is null)
        {
            // Why: 指针在变更完成后才落盘，崩溃窗口内会丢失；
            // 但底层恢复材料已由协调器在写入前持久化，按能力标识反查兜底。
            var fallbackReceipt = await _orchestrator
                .RestoreLatestByCapabilityAsync(Id, cancellationToken)
                .ConfigureAwait(false);
            if (fallbackReceipt is null)
            {
                return new OperationInvokeResult(false, false, "缺少统一安全协调器的恢复回执。");
            }

            return fallbackReceipt.Outcome switch
            {
                ChangeOutcome.RestoredVerified => new OperationInvokeResult(true, true, fallbackReceipt.Operation.Message),
                ChangeOutcome.NoChange => new OperationInvokeResult(true, false, fallbackReceipt.Operation.Message),
                _ => new OperationInvokeResult(false, fallbackReceipt.Operation.MutationOccurred, fallbackReceipt.Operation.Message)
            };
        }

        var receipt = await _orchestrator.RestoreAsync(
            new RestoreRequest(pointer.ReceiptId, pointer.RecoveryId),
            cancellationToken).ConfigureAwait(false);
        return receipt.Outcome switch
        {
            ChangeOutcome.RestoredVerified => new OperationInvokeResult(true, true, receipt.Operation.Message),
            ChangeOutcome.NoChange => new OperationInvokeResult(true, false, receipt.Operation.Message),
            _ => new OperationInvokeResult(false, receipt.Operation.MutationOccurred, receipt.Operation.Message)
        };
    }

    private Task SavePointerAsync(
        OperationExecutionContext context,
        PreparedChangePlan plan,
        OperationReceipt receipt,
        CancellationToken cancellationToken) =>
        context.SaveSnapshotAsync(
            Id,
            new OptimizationRecoveryPointer
            {
                SchemaVersion = 2,
                OperationId = Id,
                ReceiptId = receipt.ReceiptId,
                RecoveryId = receipt.RecoveryId,
                CapabilityId = plan.Change.CapabilityId,
                DefinitionVersion = plan.Change.DefinitionVersion,
                ResourceKey = plan.Change.ResourceKey,
                Scope = plan.Change.Scope,
                BeforeFingerprint = plan.Change.BeforeFingerprint,
                TargetFingerprint = plan.Change.TargetFingerprint
            },
            cancellationToken);

    private TargetScope CreateScope()
    {
        if (_scopeKind == TargetScopeKind.Machine)
        {
            return TargetScope.Machine(Environment.MachineName);
        }

        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
        {
            throw new InvalidOperationException("无法确定当前 Windows 用户 SID，已阻断当前用户注册表操作。");
        }

        return TargetScope.CurrentUser(sid);
    }

    private string CreateTargetPayload()
    {
        var payload = _targetPayloadFactory();
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("目标设置载荷为空，已阻断执行。");
        }

        return payload;
    }

}

public abstract class OrchestratedRegistryValueOperation : OrchestratedChangeOperation
{
    protected OrchestratedRegistryValueOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        string registryPath,
        string registryValueName,
        string optimizedValue,
        string valueType,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        IChangeConfirmationAuthority? confirmationAuthority = null,
        bool requiresExplicitConfirmation = false,
        TargetScopeKind scopeKind = TargetScopeKind.CurrentUser,
        bool requiresAdministrator = false,
        Func<string>? targetPayloadFactory = null)
        : base(
            orchestrator,
            id,
            title,
            description,
            category,
            OperationType.Registry,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation,
            scopeKind,
            requiresAdministrator,
            targetPayloadFactory)
    {
        RegistryPath = registryPath;
        RegistryValueName = registryValueName;
        OptimizedValue = optimizedValue;
        ValueType = valueType;
        RegistryHiveText = scopeKind == TargetScopeKind.Machine ? "HKLM" : "HKCU";
    }

    public override string RegistryHiveText { get; }
    public override string RegistryPath { get; }
    public override string RegistryValueName { get; }
    public override string OptimizedValue { get; }
    public override string ValueType { get; }
}

public class OrchestratedRegistryDwordOperation : OrchestratedRegistryValueOperation
{
    public OrchestratedRegistryDwordOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        string registryPath,
        string registryValueName,
        int optimizedValue,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        IChangeConfirmationAuthority? confirmationAuthority = null,
        bool requiresExplicitConfirmation = false,
        TargetScopeKind scopeKind = TargetScopeKind.CurrentUser,
        bool requiresAdministrator = false)
        : base(
            orchestrator,
            id,
            title,
            description,
            category,
            registryPath,
            registryValueName,
            optimizedValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "DWORD",
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation,
            scopeKind,
            requiresAdministrator)
    {
    }
}

public sealed class OrchestratedRegistryStringOperation : OrchestratedRegistryValueOperation
{
    public OrchestratedRegistryStringOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        string registryPath,
        string registryValueName,
        string optimizedValue,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        IChangeConfirmationAuthority? confirmationAuthority = null,
        bool requiresExplicitConfirmation = false,
        TargetScopeKind scopeKind = TargetScopeKind.CurrentUser,
        bool requiresAdministrator = false)
        : base(
            orchestrator,
            id,
            title,
            description,
            category,
            registryPath,
            registryValueName,
            optimizedValue,
            "REG_SZ",
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation,
            scopeKind,
            requiresAdministrator)
    {
    }
}

public sealed class OrchestratedRegistryValueSetOperation : OrchestratedRegistryValueOperation
{
    public OrchestratedRegistryValueSetOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        string registryPath,
        string registryValueNames,
        string optimizedValues,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        Func<string> targetPayloadFactory,
        IChangeConfirmationAuthority? confirmationAuthority = null,
        bool requiresExplicitConfirmation = true,
        TargetScopeKind scopeKind = TargetScopeKind.CurrentUser,
        bool requiresAdministrator = false)
        : base(
            orchestrator,
            id,
            title,
            description,
            category,
            registryPath,
            registryValueNames,
            optimizedValues,
            "REGISTRY VALUE SET",
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation,
            scopeKind,
            requiresAdministrator,
            targetPayloadFactory: targetPayloadFactory)
    {
    }

    public static string CreateKeyboardResponseTargetPayload()
    {
        var settings = PeripheralSettingsProvider.GetSettings();
        return SerializeStringTargets(
            ("KeyboardDelay", settings?.KeyboardDelay ?? "0"),
            ("KeyboardSpeed", settings?.KeyboardSpeed ?? "31"));
    }

    public static string CreateMousePrecisionTargetPayload()
    {
        var settings = PeripheralSettingsProvider.GetSettings();
        return SerializeStringTargets(
            ("MouseSpeed", (settings?.MouseSpeed ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("MouseThreshold1", (settings?.MouseThreshold1 ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("MouseThreshold2", (settings?.MouseThreshold2 ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static string CreateTouchFeedbackTargetPayload() =>
        SerializeDwordTargets(
            ("ContactVisualization", 0),
            ("GestureVisualization", 0));

    public static string CreateHandwritingCollectionTargetPayload() =>
        SerializeDwordTargets(
            ("RestrictImplicitInkCollection", 1),
            ("RestrictImplicitTextCollection", 1));

    public static string CreateAdvertisingIdTargetPayload() =>
        JsonSerializer.Serialize(new ValueSetTargetRequest(
        [
            new TypedTargetValue("Enabled", 1, null, 0),
            new TypedTargetValue("Id", 0, "00000000-0000-0000-0000-000000000000", null)
        ]));

    public static string CreateWindowsTipsTargetPayload() =>
        SerializeDwordTargets(
            ("SoftLandingEnabled", 0),
            ("SubscribedContent-338389Enabled", 0));

    public static string CreateSuggestedAppsTargetPayload() =>
        SerializeDwordTargets(
            ("OemPreInstalledAppsEnabled", 0),
            ("PreInstalledAppsEnabled", 0),
            ("PreInstalledAppsEverEnabled", 0),
            ("SilentInstalledAppsEnabled", 0));

    public static string CreateFullscreenOptimizationsTargetPayload() =>
        SerializeDwordTargets(
            ("GameDVR_FSEBehavior", 2),
            ("GameDVR_FSEBehaviorMode", 2));

    public static string CreateOptimizeDnsTargetPayload() =>
        SerializeDwordTargets(
            ("MaxCacheTtl", 900),
            ("MaxNegativeCacheTtl", 30),
            ("NegativeSOACacheTime", 0),
            ("NetFailureCacheTime", 0),
            ("ServerPriorityTimeLimit", 0));

    public static string CreateDisableCloudContentTargetPayload() =>
        SerializeDwordTargets(
            ("DisableWindowsConsumerFeatures", 1),
            ("DisableWindowsSpotlightFeatures", 1),
            ("DisableSoftLanding", 1));

    public static string CreateDisableAppSyncTargetPayload() =>
        SerializeDwordTargets(
            ("DisableSettingSync", 1),
            ("DisableSettingSyncUserOverride", 1));

    public static string CreateDisableTimelineTargetPayload() =>
        SerializeDwordTargets(
            ("EnableActivityFeed", 0),
            ("PublishUserActivities", 0),
            ("UploadUserActivities", 0));

    public static string CreateOptimizeAfdParametersTargetPayload() =>
        SerializeDwordTargets(
            ("DefaultReceiveWindow", 65535),
            ("DefaultSendWindow", 65535));

    public static string CreateDisableNetworkThrottlingTargetPayload() =>
        SerializeDwordTargets(
            ("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF)),
            ("SystemResponsiveness", 0));

    public static string CreateOptimizeSampleRateTargetPayload() =>
        JsonSerializer.Serialize(new ValueSetTargetRequest(
        [
            new TypedTargetValue("Priority", 1, null, 6),
            new TypedTargetValue("Affinity", 1, null, 0),
            new TypedTargetValue("Scheduling Category", 0, "High", null),
            new TypedTargetValue("SFIO Priority", 0, "High", null)
        ]));

    public static string CreateDisableDwmAnimationsTargetPayload() =>
        SerializeDwordTargets(
            ("EnableAeroPeek", 0),
            ("AlwaysHibernateThumbnails", 0));

    public static string CreateDisableSpectreMeltdownTargetPayload() =>
        SerializeDwordTargets(
            ("FeatureSettingsOverride", 3),
            ("FeatureSettingsOverrideMask", 3));

    private static string SerializeStringTargets(params (string Name, string Value)[] values) =>
        JsonSerializer.Serialize(new ValueSetTargetRequest(
            values.Select(static value => new TypedTargetValue(value.Name, 0, value.Value, null)).ToArray()));

    private static string SerializeDwordTargets(params (string Name, int Value)[] values) =>
        JsonSerializer.Serialize(new ValueSetTargetRequest(
            values.Select(static value => new TypedTargetValue(value.Name, 1, null, value.Value)).ToArray()));

    // Wire adapter for the capability-specific JSON target contract. The capability validates names,
    // kinds, cardinality and numeric ranges before it signs a plan.
    private sealed record ValueSetTargetRequest(IReadOnlyList<TypedTargetValue> Values);
    private sealed record TypedTargetValue(string ValueName, int Kind, string? StringValue, int? DwordValue);
}
