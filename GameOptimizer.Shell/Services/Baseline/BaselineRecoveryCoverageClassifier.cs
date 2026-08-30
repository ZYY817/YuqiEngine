// File: GameOptimizer.Shell/Services/Baseline/BaselineRecoveryCoverageClassifier.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// Classifies a captured baseline domain from the restore steps that the current
/// product can actually execute.  It deliberately does not infer recoverability
/// from a successful collection alone.
/// </summary>
internal static class BaselineRecoveryCoverageClassifier
{
	private static readonly string[] BaselineAdmissionDomainIds = ["D03", "D04", "D05", "D06"];

	internal static IReadOnlyList<string> GetBaselineAdmissionDomainIds() => BaselineAdmissionDomainIds;

	internal static bool IsRequiredForBaselineAdmission(string? domainId) =>
		!string.IsNullOrWhiteSpace(domainId)
		&& BaselineAdmissionDomainIds.Contains(domainId.Trim(), StringComparer.OrdinalIgnoreCase);

    // This catalog is intentionally conservative. Collection proves that data was
    // saved; it does not prove that replaying a Windows-wide setting is safe.
    // Keep this list aligned with concrete, domain-owned restore implementations.
    private static readonly IReadOnlyDictionary<string, BaselineDomainRecoveryCapability> CapabilityCatalog =
        new Dictionary<string, BaselineDomainRecoveryCapability>(StringComparer.OrdinalIgnoreCase)
        {
            ["D01"] = Restricted("D01", "回滚仓快照仅限同一设备和同一应用数据根目录；当前文件复制链尚未具备原子替换、冲突检测与回读验证。"),
            ["D02"] = Restricted("D02", "快照主数据仅限同一设备和同一应用数据根目录；当前文件复制链尚未具备原子替换、冲突检测与回读验证。"),
            ["D03"] = Confirmed("D03", "类型化设置快照只恢复已注册的资源；恢复前会重新生成计划并要求确认，执行后由原恢复链回读验证。"),
            ["D04"] = Automatic("D04", "应用配置仅限同机、同用户、同数据根目录；专属 File Owner 会原子替换并以内容指纹回读。"),
            ["D05"] = Automatic("D05", "业务档案仅限同机、同用户和兼容的档案格式；专属 File Owner 会事务恢复并以内容指纹回读。"),
            ["D06"] = Automatic("D06", "核心加速配置仅限同机、同用户和硬件拓扑兼容；专属 File Owner 会事务恢复并以内容指纹回读。"),
            ["D07"] = RecordOnly("D07", "历史清理备份可能已降级为摘要或包含外部数据，只保留记录，不自动回写。"),
            ["D08"] = RecordOnly("D08", "网络信息和历史网络备份仅用于诊断；当前没有按网卡、代理和地址配置逐项验证的恢复器。"),
            ["D09"] = Manual("D09", "电源计划已保留为恢复证据。请先在 Windows“设置 > 系统 > 电源和电池”选择计划；恢复 Windows 默认计划会移除自定义计划，必须由用户确认后在系统电源入口完成。"),
            ["D10"] = Manual("D10", "启动配置会影响系统可启动性，当前只能在明确故障场景下人工恢复。"),
            ["D11"] = Manual("D11", "服务状态需要按服务白名单、依赖和回读验证处理，当前只能人工恢复。"),
            ["D12"] = Manual("D12", "计划任务需要按任务所有者和触发器复核，当前只能人工恢复。"),
            ["D13"] = Manual("D13", "系统还原点由 Windows 服务创建和恢复，软件仅提供人工引导。"),
            ["D14"] = RecordOnly("D14", "诊断与审计材料用于排障和追溯，不应覆盖当前日志或基线状态。"),
            ["D15"] = Manual("D15", "本地组策略和安全策略影响面较大。优先使用已验证的 Windows 系统还原点；恢复默认安全策略可能覆盖本机自定义策略，当前只提供证据和人工路径。"),
            ["D16"] = Manual("D16", "防火墙和 Defender 策略需要专属预检与回读验证。请在 Windows 安全中心检查规则；恢复防火墙默认值会删除自定义规则，当前不自动执行。"),
            ["D17"] = Manual("D17", "启动项同时涉及注册表和启动文件夹，当前不能安全地整域自动回写。"),
            ["D18"] = RecordOnly("D18", "Hosts、WinHTTP 和 Winsock 状态仅用于诊断；网络重置必须走独立确认链。"),
            ["D19"] = Manual("D19", "系统功能和游戏开关包含注册表及 Windows 功能状态，当前只能人工恢复。"),
            ["D20"] = Manual("D20", "渲染与显示设置依赖当前用户和显示硬件，当前只能人工恢复。"),
            ["D21"] = Manual("D21", "内存与分页设置影响系统稳定性，当前只能人工恢复。"),
            ["D22"] = Manual("D22", "Windows Update 策略和服务状态需要专属恢复及回读验证，当前只能人工恢复。")
        };

    internal static BaselineRecoveryCoverageAssessment Evaluate(
        BaselineDomainArtifact domain,
        bool packageVerified,
        IReadOnlyList<BaselineDomainRestoreStep>? restoreSteps,
        string? readFailure)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (domain.Status != BaselineDomainStatus.Succeeded)
        {
            return FromCollectionStatus(domain);
        }

        if (!packageVerified)
        {
            return new("已捕获", "待验证", "已采集，但基线完整性或可恢复性自检尚未通过。", false);
        }

        if (!string.IsNullOrWhiteSpace(readFailure))
        {
            return new("已捕获", "不可用", readFailure, false);
        }

        if (!CapabilityCatalog.TryGetValue(domain.DomainId ?? string.Empty, out var capability))
        {
            return new("已捕获", "不支持", "该备份域不在当前产品的恢复能力目录中，已阻断自动恢复。", false);
        }

        if (capability.Classification == BaselineRecoveryClassification.RecordOnly)
        {
            return new("已捕获", "仅保留记录", capability.Detail, false);
        }

        if (capability.Classification == BaselineRecoveryClassification.Manual)
        {
            return new("已捕获", "仅手动恢复", capability.Detail, false);
        }

        if (restoreSteps is null || restoreSteps.Count == 0)
        {
            return new("已捕获", "不可用", "该域没有受支持的文件恢复步骤，已阻断自动恢复。", false);
        }

        var stepTypes = restoreSteps
            .Select(static step => (step.StepType ?? string.Empty).Trim())
            .Where(static type => type.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (stepTypes.Length == 0 || stepTypes.Any(static type => !IsKnownType(type)))
        {
            return new("已捕获", "不可用", "恢复步骤为空或包含当前产品不识别的类型，已阻断自动恢复。", false);
        }

		bool hasOnlyOwnedFileDomainStep = stepTypes.Length == 1 && stepTypes[0].Equals("OwnedFileDomain", StringComparison.OrdinalIgnoreCase);
		bool hasOnlyTypedSnapshotStep = stepTypes.Length == 1 && stepTypes[0].Equals("TypedSnapshot", StringComparison.OrdinalIgnoreCase);
		bool hasOnlyManualActionStep = stepTypes.Length == 1 && stepTypes[0].Equals("ManualAction", StringComparison.OrdinalIgnoreCase);
		bool hasOnlyLegacyCopySteps = stepTypes.All(static type => type is "CopyTree" or "CopyFile");
		if ((capability.Classification == BaselineRecoveryClassification.Automatic && !hasOnlyOwnedFileDomainStep)
			|| (capability.Classification == BaselineRecoveryClassification.ConfirmedRecovery && !hasOnlyTypedSnapshotStep)
			|| (capability.Classification == BaselineRecoveryClassification.Manual && !hasOnlyManualActionStep && !hasOnlyLegacyCopySteps)
			|| (capability.Classification is not BaselineRecoveryClassification.Automatic
				and not BaselineRecoveryClassification.ConfirmedRecovery
				and not BaselineRecoveryClassification.Manual
				&& !hasOnlyLegacyCopySteps))
		{
			return new("已捕获", "不可用", "该域恢复步骤与已注册的专属恢复器不匹配，当前不能建立受控自动恢复链。", false);
		}

		if (capability.Classification == BaselineRecoveryClassification.RestrictedAutomatic)
		{
			return new("已捕获", "受限自动恢复", capability.Detail, false);
		}

		if (capability.Classification == BaselineRecoveryClassification.ConfirmedRecovery)
		{
			return new("已捕获", "确认后可恢复", capability.Detail, true);
		}

        return new("已捕获", "可自动恢复", capability.Detail, true);
    }

    private static BaselineRecoveryCoverageAssessment FromCollectionStatus(BaselineDomainArtifact domain) => domain.Status switch
    {
        BaselineDomainStatus.ManualRequired => new("未形成自动恢复材料", "仅手动恢复", "当前系统需要按提示手动处理；软件不会伪造自动恢复。", false),
        BaselineDomainStatus.Skipped => new("未捕获", "不可用", "当前环境未采集该项，无法通过软件恢复。", false),
        BaselineDomainStatus.Failed => new("未捕获", "不可用", string.IsNullOrWhiteSpace(domain.ErrorMessage) ? "采集失败，无法确认恢复内容。" : domain.ErrorMessage, false),
        BaselineDomainStatus.Warning => new("部分捕获", "待复核", string.IsNullOrWhiteSpace(domain.ErrorMessage) ? "采集存在警告，需要恢复前人工复核。" : domain.ErrorMessage, false),
        _ => new("未捕获", "未完成", "该项尚未形成可用于恢复的记录。", false)
    };

	private static bool IsKnownType(string stepType) => stepType is "CopyTree" or "CopyFile" or "OwnedFileDomain" or "TypedSnapshot" or "ImportRegistry" or "RunCommand" or "ManualAction";

	private static BaselineDomainRecoveryCapability Automatic(string domainId, string detail) =>
		new(domainId, BaselineRecoveryClassification.Automatic, detail);

	private static BaselineDomainRecoveryCapability Restricted(string domainId, string detail) =>
        new(domainId, BaselineRecoveryClassification.RestrictedAutomatic, detail);

	private static BaselineDomainRecoveryCapability Confirmed(string domainId, string detail) =>
		new(domainId, BaselineRecoveryClassification.ConfirmedRecovery, detail);

    private static BaselineDomainRecoveryCapability Manual(string domainId, string detail) =>
        new(domainId, BaselineRecoveryClassification.Manual, detail);

    private static BaselineDomainRecoveryCapability RecordOnly(string domainId, string detail) =>
        new(domainId, BaselineRecoveryClassification.RecordOnly, detail);
}

internal enum BaselineRecoveryClassification
{
	Automatic,
	ConfirmedRecovery,
    RestrictedAutomatic,
    Manual,
    RecordOnly
}

/// <summary>
/// Declares the recovery promise for a baseline domain.  It is presentation and
/// execution metadata shared by the backup-center presentation and the baseline
/// self-check. The restore orchestrator remains the final execution gate.
/// </summary>
internal sealed record BaselineDomainRecoveryCapability(
    string DomainId,
    BaselineRecoveryClassification Classification,
    string Detail);

internal sealed record BaselineRecoveryCoverageAssessment(
    string CaptureStatus,
    string RecoveryStatus,
    string Detail,
    bool IsAutomaticRecovery);
