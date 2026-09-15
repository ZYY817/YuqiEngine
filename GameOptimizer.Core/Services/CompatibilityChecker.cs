// File: GameOptimizer.Core/Services/CompatibilityChecker.cs
using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Optimization;

namespace GameOptimizer.Core.Services;

public enum CompatibilityLevel
{
    FullyCompatible,
    PartiallyCompatible,
    AlternativeAvailable,
    NotCompatible,
    NotApplicable,
    RequiresAdmin,
    RequiresReboot
}

public enum CompatibilityStrategy
{
    Supported,
    Unsupported,
    Alternative
}

public enum WindowsSystemComponent
{
    BcdEdit,
    PowerApi,
    PowerShell,
    PnpUtil,
    ScheduledTasks,
    MicrosoftEdge,
    MicrosoftEdge137,
    XboxGameBar
}

public sealed record WindowsCompatibilityEnvironment(
    bool IsWindows,
    int MajorVersion,
    int BuildNumber,
    string Edition,
    bool IsServer,
    bool IsAdministrator,
    CpuVendor CpuVendor,
    bool HasHybridProcessor,
    bool? HasAmdGpu,
    Version? WddmVersion,
    bool? HardwareAcceleratedGpuSchedulingSupported,
    IReadOnlySet<WindowsSystemComponent> Components);

public interface IWindowsCompatibilityEnvironmentProbe
{
    Task<WindowsCompatibilityEnvironment> CaptureAsync(CancellationToken cancellationToken);
}

public sealed record WindowsCompatibilityRule(
    int MinimumBuild = (int)WindowsBuild.Win10_2004,
    int? MaximumBuild = null,
    bool WindowsClientOnly = true,
    CpuVendor? RequiredCpuVendor = null,
    bool RequiresHybridProcessor = false,
    bool RequiresAmdGpu = false,
    Version? MinimumWddmVersion = null,
    bool RequiresHardwareAcceleratedGpuScheduling = false,
    IReadOnlySet<WindowsSystemComponent>? RequiredComponents = null,
    string? AlternativeResourceKey = null);

public record CompatibilityCheckResult
{
    public string OperationId { get; init; } = string.Empty;
    public CompatibilityLevel Level { get; init; }
    public CompatibilityStrategy Strategy { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int? MinimumBuild { get; init; }
    public int? MaximumBuild { get; init; }
    public bool RequiresAdmin { get; init; }
    public bool RequiresReboot { get; init; }
    public string? Alternative { get; init; }
    public string[] Warnings { get; init; } = [];

    public bool CanExecute => Strategy == CompatibilityStrategy.Supported
        && Level is CompatibilityLevel.FullyCompatible or CompatibilityLevel.PartiallyCompatible or CompatibilityLevel.RequiresReboot;
}

/// <summary>
/// Single source of truth for catalog-wide Windows/build/component/hardware/permission policy.
/// Capability-specific probes still own volatile resource facts such as whether one service,
/// task, adapter, device, registry value, or power setting exists on the current machine.
/// </summary>
public sealed class CompatibilityChecker :
    IChangeCompatibilityPolicy,
    IOptimizationCompatibilityPolicy,
    IOptimizationCompatibilityMetadataProvider
{
    private readonly IWindowsCompatibilityEnvironmentProbe _environmentProbe;

    public CompatibilityChecker()
        : this(new RuntimeWindowsCompatibilityEnvironmentProbe())
    {
    }

    public CompatibilityChecker(IWindowsCompatibilityEnvironmentProbe environmentProbe)
    {
        _environmentProbe = environmentProbe ?? throw new ArgumentNullException(nameof(environmentProbe));
    }

    // Kept for source compatibility with the previous helper API.
    public CompatibilityChecker(bool isAdmin)
        : this(new RuntimeWindowsCompatibilityEnvironmentProbe(isAdmin))
    {
    }

    public CompatibilityCheckResult Check(string operationId)
    {
        Guard.NotNullOrWhiteSpace(operationId, nameof(operationId));
        return CheckAsync(operationId, requiresAdmin: false, requiresReboot: false, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public async Task<CapabilityAssessment?> ProbeAsync(
        CapabilityDescriptor descriptor,
        TargetScope scope,
        ChangeIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var result = await CheckAsync(
            descriptor.CapabilityId,
            descriptor.RequiresAdministrator,
            descriptor.RequiresRestart,
            cancellationToken).ConfigureAwait(false);

        if (result.CanExecute)
        {
            return null;
        }

        var applicability = result.Level == CompatibilityLevel.NotApplicable
            ? CapabilityApplicability.NotApplicable
            : CapabilityApplicability.Unsupported;
        var hasPermission = result.Level != CompatibilityLevel.RequiresAdmin;
        var details = string.IsNullOrWhiteSpace(result.Alternative)
            ? result.Message
            : $"{result.Message} {LocalizationService.Instance.Get("Compatibility.AlternativePrefix", result.Alternative)}";
        return new CapabilityAssessment(applicability, hasPermission, result.ReasonCode, details);
    }

    public async Task<OperationStatus?> ProbeAsync(
        string moduleKey,
        IOptimizationOperation operation,
        CancellationToken cancellationToken)
    {
        Guard.NotNullOrWhiteSpace(moduleKey, nameof(moduleKey));
        ArgumentNullException.ThrowIfNull(operation);
        var result = await CheckAsync(
            operation.Id,
            operation.RequiresAdmin,
            requiresReboot: false,
            cancellationToken).ConfigureAwait(false);
        if (result.CanExecute) return null;

        var metadata = await DescribeAsync(moduleKey, operation, cancellationToken).ConfigureAwait(false);

        var details = string.IsNullOrWhiteSpace(result.Alternative)
            ? result.Message
            : $"{result.Message} {LocalizationService.Instance.Get("Compatibility.AlternativePrefix", result.Alternative)}";
        return new OperationStatus(
            false,
            details,
            IsKnown: true,
            SupportState: result.Strategy == CompatibilityStrategy.Alternative
                ? OperationSupportState.AlternativeAvailable
                : result.Level switch
                {
                    CompatibilityLevel.RequiresAdmin => OperationSupportState.PermissionRequired,
                    CompatibilityLevel.NotApplicable => OperationSupportState.NotApplicable,
                    _ => OperationSupportState.Unsupported
                },
            SupportReasonCode: result.ReasonCode,
            Alternative: result.Alternative,
            CompatibilityVariantId: metadata.VariantId,
            CompatibilityVariantLabel: metadata.VariantLabel,
            CompatibilityMethod: metadata.Method,
            CompatibilityInformation: metadata.Information);
    }

    public async Task<OperationCompatibilityMetadata> DescribeAsync(
        string moduleKey,
        IOptimizationOperation operation,
        CancellationToken cancellationToken)
    {
        Guard.NotNullOrWhiteSpace(moduleKey, nameof(moduleKey));
        ArgumentNullException.ThrowIfNull(operation);
        var environment = await _environmentProbe.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var variant = WindowsOperationVariantCatalog.Resolve(operation, environment);
        var loc = LocalizationService.Instance;
        return new OperationCompatibilityMetadata(
            variant.Id,
            loc.Get(variant.LabelResourceKey, environment.BuildNumber),
            loc.Get(variant.MethodResourceKey),
            AppendRegistryTargetInformation(
                loc.Get(variant.InformationResourceKey, environment.BuildNumber),
                WindowsRegistryTargetCatalog.Resolve(operation, environment.BuildNumber),
                loc));
    }

    private static string AppendRegistryTargetInformation(
        string information,
        WindowsRegistryTarget? target,
        LocalizationService loc)
    {
        if (target is null)
        {
            return information;
        }

        var evidence = target.IsMicrosoftDocumented
            ? loc.Get("Compatibility.RegistryTargetOfficial", target.EvidenceUrl)
            : loc.Get("Compatibility.RegistryTargetOwnerDefined");
        var value = FormatRegistryValueNames(target.ValueName);
        var result = $"{information} {loc.Get("Compatibility.RegistryTarget", target.DisplayPath + value, evidence)}";
        if (!string.IsNullOrWhiteSpace(target.OfficialPolicyPath))
        {
            var policyValue = FormatRegistryValueNames(target.OfficialPolicyValueName);
            var policyEvidence = string.IsNullOrWhiteSpace(target.OfficialPolicyEvidenceUrl)
                ? loc.Get("Compatibility.RegistryTargetOwnerDefined")
                : loc.Get("Compatibility.RegistryTargetOfficial", target.OfficialPolicyEvidenceUrl);
            result += $" {loc.Get("Compatibility.RegistryTargetAlternative", target.OfficialPolicyPath + policyValue, policyEvidence)}";
        }

        return result;
    }

    private static string FormatRegistryValueNames(string? valueNames)
    {
        if (string.IsNullOrWhiteSpace(valueNames))
        {
            return string.Empty;
        }

        return string.Join(", ", valueNames
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(valueName => $"\\{valueName}"));
    }

    public async Task<CompatibilityCheckResult> CheckAsync(
        string operationId,
        bool requiresAdmin,
        bool requiresReboot,
        CancellationToken cancellationToken)
    {
        Guard.NotNullOrWhiteSpace(operationId, nameof(operationId));
        var environment = await _environmentProbe.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var rule = WindowsCompatibilityRuleCatalog.Resolve(operationId, environment.BuildNumber);
        var loc = LocalizationService.Instance;

        if (!environment.IsWindows || environment.MajorVersion != 10)
        {
            return Blocked(operationId, CompatibilityLevel.NotCompatible, "compatibility.os.windows-required",
                loc.Get("Compatibility.WindowsRequired"), rule, requiresAdmin, requiresReboot);
        }

        if (rule.WindowsClientOnly && environment.IsServer)
        {
            return Blocked(operationId, CompatibilityLevel.NotCompatible, "compatibility.os.server-unsupported",
                loc.Get("Compatibility.ServerUnsupported", environment.Edition), rule, requiresAdmin, requiresReboot);
        }

        if (environment.BuildNumber < rule.MinimumBuild)
        {
            return Blocked(operationId, CompatibilityLevel.NotCompatible, "compatibility.os.build-too-old",
                loc.Get("Compatibility.BuildTooOld", environment.BuildNumber, rule.MinimumBuild), rule, requiresAdmin, requiresReboot);
        }

        if (rule.MaximumBuild is int maximumBuild && environment.BuildNumber > maximumBuild)
        {
            return Alternative(operationId, "compatibility.os.feature-retired",
                loc.Get("Compatibility.FeatureRetired", maximumBuild, environment.BuildNumber), rule, requiresAdmin, requiresReboot);
        }

        if (operationId == "windows.disable_focus_assist")
        {
            var isWindows11 = environment.BuildNumber >= (int)WindowsBuild.Win11_21H2;
            var messageKey = isWindows11
                ? "Compatibility.FocusAssist.Windows11Manual"
                : "Compatibility.FocusAssist.Windows10Manual";
            return Alternative(operationId, "compatibility.method.manual-only",
                loc.Get(messageKey), rule, requiresAdmin, requiresReboot);
        }

        var missingComponent = rule.RequiredComponents?
            .Where(component => !environment.Components.Contains(component))
            .Select(component => (WindowsSystemComponent?)component)
            .FirstOrDefault();
        if (missingComponent is not null)
        {
            var componentName = loc.Get($"Compatibility.Component.{missingComponent}");
            return Blocked(operationId, CompatibilityLevel.NotCompatible, "compatibility.component.missing",
                loc.Get("Compatibility.ComponentMissing", componentName), rule, requiresAdmin, requiresReboot);
        }

        if (rule.RequiredCpuVendor is CpuVendor requiredVendor && environment.CpuVendor != requiredVendor)
        {
            return Blocked(operationId, CompatibilityLevel.NotApplicable, "compatibility.hardware.cpu-vendor",
                loc.Get("Compatibility.CpuVendorMismatch", requiredVendor, environment.CpuVendor), rule, requiresAdmin, requiresReboot);
        }

        if (rule.RequiresHybridProcessor && !environment.HasHybridProcessor)
        {
            return Blocked(operationId, CompatibilityLevel.NotApplicable, "compatibility.hardware.hybrid-required",
                loc.Get("Compatibility.HybridCpuRequired"), rule, requiresAdmin, requiresReboot);
        }

        if (rule.RequiresAmdGpu && environment.HasAmdGpu is null)
        {
            return Blocked(operationId, CompatibilityLevel.NotCompatible, "compatibility.hardware.gpu-unknown",
                loc.Get("Compatibility.GpuUnknown"), rule, requiresAdmin, requiresReboot);
        }

        if (rule.RequiresAmdGpu && environment.HasAmdGpu == false)
        {
            return Blocked(operationId, CompatibilityLevel.NotApplicable, "compatibility.hardware.amd-gpu-required",
                loc.Get("Compatibility.AmdGpuRequired"), rule, requiresAdmin, requiresReboot);
        }

        if (rule.MinimumWddmVersion is Version minimumWddm)
        {
            if (environment.WddmVersion is null)
            {
                return Alternative(operationId, "compatibility.hardware.wddm-unknown",
                    loc.Get("Compatibility.WddmUnknown", minimumWddm), rule, requiresAdmin, requiresReboot);
            }

            if (environment.WddmVersion < minimumWddm)
            {
                return Blocked(operationId, CompatibilityLevel.NotApplicable, "compatibility.hardware.wddm-too-old",
                    loc.Get("Compatibility.WddmTooOld", environment.WddmVersion, minimumWddm), rule, requiresAdmin, requiresReboot);
            }
        }

        if (rule.RequiresHardwareAcceleratedGpuScheduling)
        {
            if (environment.HardwareAcceleratedGpuSchedulingSupported is null)
            {
                return Alternative(operationId, "compatibility.hardware.hags-unknown",
                    loc.Get("Compatibility.HagsUnknown"), rule, requiresAdmin, requiresReboot);
            }

            if (environment.HardwareAcceleratedGpuSchedulingSupported == false)
            {
                return Blocked(operationId, CompatibilityLevel.NotApplicable, "compatibility.hardware.hags-unsupported",
                    loc.Get("Compatibility.HagsUnsupported"), rule, requiresAdmin, requiresReboot);
            }
        }

        if (requiresAdmin && !environment.IsAdministrator)
        {
            return Blocked(operationId, CompatibilityLevel.RequiresAdmin, "permission.administrator-required",
                loc.Get("Compatibility.AdminRequired"), rule, requiresAdmin, requiresReboot);
        }

        var warnings = requiresReboot ? new[] { loc.Get("Compatibility.RestartRequired") } : [];
        return new CompatibilityCheckResult
        {
            OperationId = operationId,
            Level = requiresReboot ? CompatibilityLevel.RequiresReboot : CompatibilityLevel.FullyCompatible,
            Strategy = CompatibilityStrategy.Supported,
            ReasonCode = "compatibility.supported",
            Message = loc.Get("Compatibility.Supported", environment.BuildNumber),
            MinimumBuild = rule.MinimumBuild,
            MaximumBuild = rule.MaximumBuild,
            RequiresAdmin = requiresAdmin,
            RequiresReboot = requiresReboot,
            Warnings = warnings
        };
    }

    private static CompatibilityCheckResult Blocked(
        string operationId,
        CompatibilityLevel level,
        string reasonCode,
        string message,
        WindowsCompatibilityRule rule,
        bool requiresAdmin,
        bool requiresReboot) => new()
    {
        OperationId = operationId,
        Level = level,
        Strategy = CompatibilityStrategy.Unsupported,
        ReasonCode = reasonCode,
        Message = message,
        MinimumBuild = rule.MinimumBuild,
        MaximumBuild = rule.MaximumBuild,
        RequiresAdmin = requiresAdmin,
        RequiresReboot = requiresReboot
    };

    private static CompatibilityCheckResult Alternative(
        string operationId,
        string reasonCode,
        string message,
        WindowsCompatibilityRule rule,
        bool requiresAdmin,
        bool requiresReboot) => new()
    {
        OperationId = operationId,
        Level = CompatibilityLevel.AlternativeAvailable,
        Strategy = CompatibilityStrategy.Alternative,
        ReasonCode = reasonCode,
        Message = message,
        MinimumBuild = rule.MinimumBuild,
        MaximumBuild = rule.MaximumBuild,
        RequiresAdmin = requiresAdmin,
        RequiresReboot = requiresReboot,
        Alternative = string.IsNullOrWhiteSpace(rule.AlternativeResourceKey)
            ? null
            : LocalizationService.Instance.Get(rule.AlternativeResourceKey)
    };
}

public static class WindowsCompatibilityRuleCatalog
{
    private static readonly IReadOnlySet<WindowsSystemComponent> BcdComponents =
        new HashSet<WindowsSystemComponent> { WindowsSystemComponent.BcdEdit, WindowsSystemComponent.PowerShell };
    private static readonly IReadOnlySet<WindowsSystemComponent> PowerComponents =
        new HashSet<WindowsSystemComponent> { WindowsSystemComponent.PowerApi };
    private static readonly IReadOnlySet<WindowsSystemComponent> TaskComponents =
        new HashSet<WindowsSystemComponent> { WindowsSystemComponent.ScheduledTasks };
    private static readonly IReadOnlySet<WindowsSystemComponent> DeviceComponents =
        new HashSet<WindowsSystemComponent> { WindowsSystemComponent.PnpUtil };
    private static readonly IReadOnlySet<WindowsSystemComponent> MmAgentComponents =
        new HashSet<WindowsSystemComponent> { WindowsSystemComponent.PowerShell };
    private static readonly IReadOnlySet<WindowsSystemComponent> EdgeGameAssistComponents =
        new HashSet<WindowsSystemComponent> { WindowsSystemComponent.MicrosoftEdge137, WindowsSystemComponent.XboxGameBar };
    private static readonly IReadOnlySet<WindowsSystemComponent> EdgeComponents =
        new HashSet<WindowsSystemComponent> { WindowsSystemComponent.MicrosoftEdge };

    public static WindowsCompatibilityRule Resolve(string operationId) =>
        Resolve(operationId, (int)WindowsBuild.Win10_2004);

    public static WindowsCompatibilityRule Resolve(string operationId, int buildNumber)
    {
        Guard.NotNullOrWhiteSpace(operationId, nameof(operationId));
        return operationId switch
        {
            "gpu.enable_hags" => new(
                MinimumBuild: (int)WindowsBuild.Win10_2004,
                MinimumWddmVersion: new Version(2, 7),
                RequiresHardwareAcceleratedGpuScheduling: true,
                AlternativeResourceKey: buildNumber >= (int)WindowsBuild.Win11_21H2
                    ? "Compatibility.Alternative.GraphicsSettings.Windows11"
                    : "Compatibility.Alternative.GraphicsSettings.Windows10"),
            "explorer.end_task" => new(MinimumBuild: (int)WindowsBuild.Win11_23H2),
            "edge.disable_game_assist" or "edge.disable_game_assist_policy" => new(
                MinimumBuild: (int)WindowsBuild.Win11_22H2,
                RequiredComponents: EdgeGameAssistComponents,
                AlternativeResourceKey: "Compatibility.Alternative.DisableGameAssistManually"),
            "basic.disable_edge_startup" => new(RequiredComponents: EdgeComponents),
            "cpu.enable_tsx" or "intel.disable_cstates" => new(RequiredCpuVendor: CpuVendor.Intel),
            "amd.radeon_anti_lag" => new(RequiresAmdGpu: true),
            "power.class1_efficiency_threshold" or "power.class1_perf_level" => new(
                MinimumBuild: (int)WindowsBuild.Win11_21H2,
                RequiresHybridProcessor: true,
                RequiredComponents: PowerComponents),
            "power.hetero_thread_scheduling" => new(
                RequiresHybridProcessor: true,
                RequiredComponents: PowerComponents),
            "privacy.disable_cortana" => new(
                MaximumBuild: (int)WindowsBuild.Win11_22H2,
                AlternativeResourceKey: "Compatibility.Alternative.WindowsSearchPrivacy"),
            "windows.disable_focus_assist" => new(
                AlternativeResourceKey: buildNumber >= (int)WindowsBuild.Win11_21H2
                    ? "Compatibility.Alternative.DoNotDisturbSettings"
                    : "Compatibility.Alternative.FocusAssistSettings"),
            var id when id.StartsWith("cpu.", StringComparison.OrdinalIgnoreCase)
                && (id.Contains("tick", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("hpet", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("tsc", StringComparison.OrdinalIgnoreCase)) => new(RequiredComponents: BcdComponents),
            var id when id.StartsWith("power.", StringComparison.OrdinalIgnoreCase)
                || id.StartsWith("peripheral.usb.", StringComparison.OrdinalIgnoreCase)
                || id is "cpu.disable_core_parking" or "cpu.disable_boost_mode" or "cpu.disable_pstates" or "cpu.processor_boost_mode" =>
                new(RequiredComponents: PowerComponents),
            var id when id.StartsWith("tasks.", StringComparison.OrdinalIgnoreCase) => new(RequiredComponents: TaskComponents),
            var id when id.StartsWith("device.", StringComparison.OrdinalIgnoreCase) => new(RequiredComponents: DeviceComponents),
            "memory.disable_compression" => new(RequiredComponents: MmAgentComponents),
            _ => new()
        };
    }
}

public sealed record WindowsOperationVariant(
    string Id,
    string LabelResourceKey,
    string MethodResourceKey,
    string InformationResourceKey);

public static class WindowsOperationVariantCatalog
{
    public static WindowsOperationVariant Resolve(
        IOptimizationOperation operation,
        WindowsCompatibilityEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(environment);

        var isWindows11 = environment.BuildNumber >= (int)WindowsBuild.Win11_21H2;
        var generation = ResolveGeneration(environment.BuildNumber);
        var defaultMethod = ResolveMethod(operation);
        var sharedInformation = environment.BuildNumber >= (int)WindowsBuild.Win11_26H1
            ? "Compatibility.Info.Shared.Windows11_26H1"
            : isWindows11
                ? "Compatibility.Info.Shared.Windows11"
                : "Compatibility.Info.Shared.Windows10";

        return operation.Id switch
        {
            "gpu.enable_hags" => Variant(
                $"{generation}.hags.wddm27",
                isWindows11 ? "Compatibility.Variant.Windows11" : "Compatibility.Variant.Windows10",
                "Compatibility.Method.RegistryTyped",
                isWindows11 ? "Compatibility.Info.Hags.Windows11" : "Compatibility.Info.Hags.Windows10"),
            "privacy.disable_cortana" when environment.BuildNumber > (int)WindowsBuild.Win11_22H2 => Variant(
                "win11.cortana-retired",
                "Compatibility.Variant.Windows11RetiredFeature",
                "Compatibility.Method.ManualAlternative",
                "Compatibility.Info.Cortana.Retired"),
            "privacy.disable_cortana" => Variant(
                $"{generation}.cortana-policy",
                isWindows11 ? "Compatibility.Variant.Windows11" : "Compatibility.Variant.Windows10",
                "Compatibility.Method.RegistryPolicyTyped",
                "Compatibility.Info.Cortana.Policy"),
            "windows.disable_focus_assist" => Variant(
                isWindows11 ? "win11.do-not-disturb-manual" : "win10.focus-assist-manual",
                isWindows11 ? "Compatibility.Variant.Windows11DoNotDisturb" : "Compatibility.Variant.Windows10FocusAssist",
                "Compatibility.Method.ManualAlternative",
                isWindows11 ? "Compatibility.Info.FocusAssist.Windows11" : "Compatibility.Info.FocusAssist.Windows10"),
            "explorer.end_task" when environment.BuildNumber >= (int)WindowsBuild.Win11_25H2 => Variant(
                "win11.25h2.end-task-advanced-settings",
                "Compatibility.Variant.Windows11_25H2Plus",
                "Compatibility.Method.RegistryTyped",
                "Compatibility.Info.EndTask.Windows11_25H2"),
            "explorer.end_task" => Variant(
                "win11.23h2.end-task-developer-settings",
                "Compatibility.Variant.Windows11_23H2_24H2",
                "Compatibility.Method.RegistryTyped",
                "Compatibility.Info.EndTask.Windows11_23H2"),
            "edge.disable_game_assist" or "edge.disable_game_assist_policy" => Variant(
                "win11.edge137.game-assist",
                "Compatibility.Variant.Windows11EdgeGameAssist",
                "Compatibility.Method.RegistryPolicyTyped",
                "Compatibility.Info.EdgeGameAssist"),
            "power.class1_efficiency_threshold" or "power.class1_perf_level" or "power.hetero_thread_scheduling" => Variant(
                "win11.hybrid-power-class",
                "Compatibility.Variant.Windows11HybridPower",
                "Compatibility.Method.PowerApiTyped",
                "Compatibility.Info.HybridPower"),
            var id when id.StartsWith("cpu.", StringComparison.OrdinalIgnoreCase)
                && (id.Contains("tick", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("hpet", StringComparison.OrdinalIgnoreCase)
                    || id.Contains("tsc", StringComparison.OrdinalIgnoreCase)) => Variant(
                $"{generation}.bcd-typed",
                isWindows11 ? "Compatibility.Variant.Windows11" : "Compatibility.Variant.Windows10",
                "Compatibility.Method.BcdTyped",
                "Compatibility.Info.BcdShared"),
            _ => Variant(
                $"{generation}.shared-{operation.OperationType.ToString().ToLowerInvariant()}",
                isWindows11 ? "Compatibility.Variant.Windows11Shared" : "Compatibility.Variant.Windows10Shared",
                defaultMethod,
                sharedInformation)
        };
    }

    private static WindowsOperationVariant Variant(
        string id,
        string label,
        string method,
        string information) => new(id, label, method, information);

    private static string ResolveMethod(IOptimizationOperation operation) => operation.OperationType switch
    {
        OperationType.Registry => "Compatibility.Method.RegistryTyped",
        OperationType.Service => "Compatibility.Method.ServiceTyped",
        OperationType.ScheduledTask => "Compatibility.Method.ScheduledTaskTyped",
        OperationType.PowerPlan => "Compatibility.Method.PowerApiTyped",
        OperationType.Command => "Compatibility.Method.CommandTyped",
        OperationType.SystemApi => "Compatibility.Method.SystemApiTyped",
        _ => "Compatibility.Method.OwnerTyped"
    };

    private static string ResolveGeneration(int buildNumber) => buildNumber switch
    {
        >= (int)WindowsBuild.Win11_26H1 => "win11.26h1",
        >= (int)WindowsBuild.Win11_25H2 => "win11.25h2",
        >= (int)WindowsBuild.Win11_24H2 => "win11.24h2",
        >= (int)WindowsBuild.Win11_23H2 => "win11.23h2",
        >= (int)WindowsBuild.Win11_22H2 => "win11.22h2",
        >= (int)WindowsBuild.Win11_21H2 => "win11.21h2",
        >= (int)WindowsBuild.Win10_22H2 => "win10.22h2",
        >= (int)WindowsBuild.Win10_21H2 => "win10.21h2",
        >= (int)WindowsBuild.Win10_21H1 => "win10.21h1",
        >= (int)WindowsBuild.Win10_20H2 => "win10.20h2",
        _ => "win10.2004"
    };
}

internal sealed class RuntimeWindowsCompatibilityEnvironmentProbe : IWindowsCompatibilityEnvironmentProbe
{
    private readonly bool? _administratorOverride;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WindowsCompatibilityEnvironment? _cached;

    public RuntimeWindowsCompatibilityEnvironmentProbe(bool? administratorOverride = null)
    {
        _administratorOverride = administratorOverride;
    }

    public async Task<WindowsCompatibilityEnvironment> CaptureAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null) return _cached;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null) return _cached;
            var version = WindowsVersionService.Current;
            var topology = new CpuTopologyService().GetTopology();
            var gpuNames = ReadGpuNames();
            var components = DetectComponents();
            var graphics = await DetectGraphicsCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            _cached = new WindowsCompatibilityEnvironment(
                OperatingSystem.IsWindows(),
                version.MajorVersion,
                version.BuildNumber,
                string.IsNullOrWhiteSpace(version.EditionId) ? "unknown" : version.EditionId,
                version.IsServer,
                _administratorOverride ?? WindowsElevation.IsElevated,
                topology.Vendor,
                DetectHybridProcessor(topology),
                gpuNames.Count == 0
                    ? null
                    : gpuNames.Any(name => name.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)),
                graphics.WddmVersion,
                graphics.HagsSupported,
                components);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlySet<WindowsSystemComponent> DetectComponents()
    {
        var result = new HashSet<WindowsSystemComponent>();
        var system = Environment.SystemDirectory;
        AddIfExists(result, WindowsSystemComponent.BcdEdit, Path.Combine(system, "bcdedit.exe"));
        AddIfExists(result, WindowsSystemComponent.PowerApi, Path.Combine(system, "powrprof.dll"));
        AddIfExists(result, WindowsSystemComponent.PowerShell,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"));
        AddIfExists(result, WindowsSystemComponent.PnpUtil, Path.Combine(system, "pnputil.exe"));
        AddIfExists(result, WindowsSystemComponent.ScheduledTasks, Path.Combine(system, "schtasks.exe"));

        var edgePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        if (edgePaths.Any(File.Exists)) result.Add(WindowsSystemComponent.MicrosoftEdge);
        if (edgePaths.Any(IsGameAssistCapableEdge)) result.Add(WindowsSystemComponent.MicrosoftEdge137);
        if (HasXboxGameBarPackage()) result.Add(WindowsSystemComponent.XboxGameBar);
        return result;
    }

    private static void AddIfExists(HashSet<WindowsSystemComponent> result, WindowsSystemComponent component, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) result.Add(component);
    }

    private static bool HasXboxGameBarPackage()
    {
        try
        {
            using var packages = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            return packages?.GetSubKeyNames().Any(name =>
                name.StartsWith("Microsoft.XboxGamingOverlay_", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGameAssistCapableEdge(string path)
    {
        try
        {
            return File.Exists(path) && FileVersionInfo.GetVersionInfo(path).FileMajorPart >= 137;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ReadGpuNames()
    {
        var result = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController")
            {
                Options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) }
            };
            foreach (ManagementObject item in searcher.Get())
            {
                var name = item["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
            }
        }
        catch
        {
            // Unknown hardware is handled by the per-operation fail-closed rule.
        }
        return result;
    }

    private static bool DetectHybridProcessor(CpuTopology topology)
    {
        if (topology.HasHybridCores) return true;
        if (topology.Vendor != CpuVendor.Intel) return false;
        var name = topology.CpuName.ToUpperInvariant();
        return Regex.IsMatch(name, @"\b(12|13|14|15)TH\b|\bI[3579]-1[2345]\d{3}|\bCORE\s+ULTRA\b",
            RegexOptions.CultureInvariant);
    }

    private static async Task<(Version? WddmVersion, bool? HagsSupported)> DetectGraphicsCapabilitiesAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return (null, null);
        var dxdiag = Path.Combine(Environment.SystemDirectory, "dxdiag.exe");
        if (!File.Exists(dxdiag)) return (null, null);

        var outputPath = Path.Combine(Path.GetTempPath(), $"yuqi-wddm-{Guid.NewGuid():N}.txt");
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = dxdiag,
                    Arguments = $"/whql:off /t \"{outputPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            if (!process.Start()) return (null, null);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(outputPath)) return (null, null);
            var text = await File.ReadAllTextAsync(outputPath, timeout.Token).ConfigureAwait(false);
            var versions = Regex.Matches(text, @"WDDM\s*(\d+)(?:\.(\d+))?", RegexOptions.IgnoreCase)
                .Select(match => new Version(
                    int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    match.Groups[2].Success
                        ? int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                        : 0))
                .ToArray();
            var supportMatch = Regex.Match(text, @"DriverSupportState\s*:\s*(?<state>[A-Za-z]+)", RegexOptions.IgnoreCase);
            bool? hagsSupported = supportMatch.Success
                ? !supportMatch.Groups["state"].Value.Equals("Unsupported", StringComparison.OrdinalIgnoreCase)
                    && !supportMatch.Groups["state"].Value.Equals("NotSupported", StringComparison.OrdinalIgnoreCase)
                : null;
            return (versions.Length == 0 ? null : versions.Max(), hagsSupported);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
            catch
            {
                // A stale probe file is non-authoritative and never used as compatibility evidence.
            }
        }
    }
}
