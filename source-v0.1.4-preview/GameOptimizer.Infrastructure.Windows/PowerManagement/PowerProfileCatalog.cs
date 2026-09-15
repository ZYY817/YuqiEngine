using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

/// <summary>
/// Read-only profile metadata.  A profile never creates, deletes, activates, or writes a
/// Windows power scheme; it only gives the UI a capability-backed preview for a later
/// prepare/confirm/receipt workflow.
/// </summary>
public enum PowerProfileId
{
    Balanced,
    GamingConservative,
    CurrentPlanAdvancedSettings
}

public enum PowerProfileAvailability
{
    Available,
    GuidanceOnly,
    Unavailable
}

public sealed record PowerProfilePreviewItem(
    string CapabilityId,
    string SettingId,
    ChangeRisk Risk,
    string Description);

public sealed record PowerProfileCandidate(
    PowerProfileId Id,
    string Name,
    string Description,
    PowerProfileAvailability Availability,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<PowerProfilePreviewItem> PreviewItems,
    bool DoesNotWriteOrSwitchSchemes = true);

public sealed record PowerProfilePlanningContext(
    PowerPlatformFacts Platform,
    CpuTopology Topology,
    bool ActiveSchemeIsReadable);

public sealed record PowerProfilePreview(IReadOnlyList<PowerProfileCandidate> Candidates)
{
    public bool DoesNotWriteOrSwitchSchemes => Candidates.All(candidate => candidate.DoesNotWriteOrSwitchSchemes);
}

public sealed class PowerProfileCatalog
{
    private static readonly IReadOnlyList<PowerProfilePreviewItem> GamingItems =
    [
        From(PowerSettingCapabilityDefinitions.PerfIncreaseThreshold, "检查处理器性能提升阈值是否由当前方案公开。"),
        From(PowerSettingCapabilityDefinitions.IdleDemoteThreshold, "检查处理器空闲降级阈值是否由当前方案公开。"),
        From(PowerSettingCapabilityDefinitions.PerfTimeCheck, "检查处理器性能检查间隔是否由当前方案公开。"),
        From(PowerSettingCapabilityDefinitions.DisableUsbSuspend, "检查 USB 选择性暂停是否可安全准备。"),
        From(PowerSettingCapabilityDefinitions.DisableAspm, "检查 PCIe 链路节能设置是否由当前方案公开。")
    ];

    /// <summary>
    /// Exposes only existing typed, low/medium-risk definitions.  Deliberately excluded:
    /// core parking, P/C-state, throttle, boost and hybrid-scheduler overrides.
    /// </summary>
    public IReadOnlyList<PowerProfileCandidate> CreateCandidates(PowerProfilePlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return
        [
            CreateBalanced(context),
            CreateGamingConservative(context),
            CreateAdvancedCurrentPlan(context)
        ];
    }

    private static PowerProfileCandidate CreateBalanced(PowerProfilePlanningContext context)
    {
        var reasons = CommonBlockReasons(context, requireActiveScheme: false);
        var availability = reasons.Count == 0 ? PowerProfileAvailability.Available : PowerProfileAvailability.GuidanceOnly;

        return new PowerProfileCandidate(
            PowerProfileId.Balanced,
            "平衡",
            "仅说明当前 Windows 电源方案应由系统和 OEM 共同管理；不会创建、删除或切换方案。",
            availability,
            ["Windows 10 2004 或更高版本", "可可靠识别平台状态"],
            reasons,
            Array.Empty<PowerProfilePreviewItem>());
    }

    private static PowerProfileCandidate CreateGamingConservative(PowerProfilePlanningContext context)
    {
        var reasons = CommonBlockReasons(context, requireActiveScheme: true);
        if (context.Platform.HasBattery && !context.Platform.IsOnAcPower)
        {
            reasons.Add("电池供电时不提供游戏预设预览，避免续航、温度和 OEM 电源策略风险。");
        }

        var availability = reasons.Count == 0 ? PowerProfileAvailability.Available : PowerProfileAvailability.Unavailable;
        return new PowerProfileCandidate(
            PowerProfileId.GamingConservative,
            "游戏（保守）",
            "只预览将来可逐项检查的低/中风险设置；不承诺帧率、DPC 延迟或功耗改善。",
            availability,
            [
                "活动电源方案可读取",
                "非 Modern Standby，且未被组织或 OEM 策略接管",
                "笔记本需接通交流电"
            ],
            reasons,
            GamingItems);
    }

    private static PowerProfileCandidate CreateAdvancedCurrentPlan(PowerProfilePlanningContext context)
    {
        var reasons = CommonBlockReasons(context, requireActiveScheme: true);
        var availability = reasons.Count == 0 ? PowerProfileAvailability.Available : PowerProfileAvailability.Unavailable;
        var preconditions = new List<string>
        {
            "活动电源方案可读取",
            "每项设置都必须在当前机器和当前方案上单独 Probe 后才可准备"
        };
        if (context.Topology.Architecture == CpuArchitecture.IntelHybrid)
        {
            preconditions.Add("Intel 混合架构仅显示事实；默认预览不包含异构调度改写。");
        }

        return new PowerProfileCandidate(
            PowerProfileId.CurrentPlanAdvancedSettings,
            "当前计划高级设置",
            "仅展示当前计划将来可以逐项检查的设置。每项仍必须通过 typed capability 的 Probe、确认、基线和回读。",
            availability,
            preconditions,
            reasons,
            GamingItems);
    }

    private static List<string> CommonBlockReasons(PowerProfilePlanningContext context, bool requireActiveScheme)
    {
        var platform = context.Platform;
        var reasons = new List<string>();
        if (!platform.IsWindows || platform.WindowsBuild < 19041)
        {
            reasons.Add("需要 Windows 10 2004 或更高版本。");
        }
        if (!platform.IsDetectionReliable)
        {
            reasons.Add("供电、策略或处理器拓扑无法可靠识别，已按 fail-closed 处理。");
        }
        if (platform.IsPolicyRestricted || platform.IsOemRestricted)
        {
            reasons.Add("检测到组织或 OEM 电源策略接管，不覆盖现有设置。");
        }
        if (platform.IsModernStandby)
        {
            reasons.Add("Modern Standby 平台不提供此预设；请使用 Windows 原生电源模式。" );
        }
        if (requireActiveScheme && !context.ActiveSchemeIsReadable)
        {
            reasons.Add("无法读取当前活动电源方案，因此不提供可准备的设置清单。");
        }
        if (context.Topology.Vendor == CpuVendor.Unknown || string.IsNullOrWhiteSpace(context.Topology.CpuName))
        {
            reasons.Add("CPU 型号无法可靠识别，处理器相关设置不提供预设。" );
        }
        return reasons;
    }

    private static PowerProfilePreviewItem From(PowerSettingChangeCapabilityDefinition definition, string description)
    {
        if (definition.Risk is not (ChangeRisk.LowReversible or ChangeRisk.MediumRebootOrExperience))
        {
            throw new InvalidOperationException($"{definition.CapabilityId} is not eligible for a conservative profile.");
        }

        return new PowerProfilePreviewItem(
            definition.CapabilityId,
            string.Join(", ", definition.Targets.Select(target => target.SettingId)),
            definition.Risk,
            description);
    }
}

/// <summary>
/// Pure planning façade kept separate so a future batch prepare/confirm/receipt coordinator can
/// consume the preview without gaining a write path.
/// </summary>
public sealed class PowerProfilePlanner
{
    private readonly PowerProfileCatalog _catalog;

    public PowerProfilePlanner(PowerProfileCatalog? catalog = null) => _catalog = catalog ?? new PowerProfileCatalog();

    public PowerProfilePreview Plan(PowerProfilePlanningContext context) =>
        new(_catalog.CreateCandidates(context));
}
