// File: GameOptimizer.Core\Optimization\Operations\PrivacyTelemetryCompositeOperation.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// 退出 CEIP（客户体验改善计划）。原始 <see cref="MultiRegistryValueOperation"/> 跨两个 HKLM 键，
/// 无法用单值 RegistryDwordCapability 表达；此处拆为两个单值 OrchestratedRegistryDwordOperation 步骤，
/// 复用现有 Machine 注册表 Owner，逐步骤原子补偿，失败时回滚已完成的步骤。
/// </summary>
public sealed class DisableCeipCompositeOperation : OrchestratedChangeOperation
{
    private const string BenefitText = "阻止使用数据被收集和上传。";
    private const string DrawbackText = "微软无法收集数据改进产品。";

    private readonly IOptimizationOperation[] _steps;

    public DisableCeipCompositeOperation(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority? confirmationAuthority)
        : base(
            orchestrator,
            "privacy.disable_ceip",
            "退出 CEIP",
            "同步关闭 SQM 客户体验改善计划。",
            "隐私防护",
            OperationType.Registry,
            OperationRiskLevel.Safe,
            BenefitText,
            DrawbackText,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        _steps =
        [
            new OrchestratedRegistryDwordOperation(
                orchestrator,
                "privacy.disable_ceip.sqm_client",
                "退出 CEIP（SQM 客户端）",
                "将 SQMClient\\Windows 的 CEIPEnable 置 0。",
                "隐私防护",
                @"SOFTWARE\Microsoft\SQMClient\Windows",
                "CEIPEnable",
                0,
                OperationRiskLevel.Safe,
                BenefitText,
                DrawbackText,
                confirmationAuthority,
                requiresExplicitConfirmation: true,
                scopeKind: TargetScopeKind.Machine,
                requiresAdministrator: true),
            new OrchestratedRegistryDwordOperation(
                orchestrator,
                "privacy.disable_ceip.sqm_policy",
                "退出 CEIP（SQM 策略）",
                "将 Policies\\SQMClient\\Windows 的 CEIPEnable 置 0。",
                "隐私防护",
                @"SOFTWARE\Policies\Microsoft\SQMClient\Windows",
                "CEIPEnable",
                0,
                OperationRiskLevel.Safe,
                BenefitText,
                DrawbackText,
                confirmationAuthority,
                requiresExplicitConfirmation: true,
                scopeKind: TargetScopeKind.Machine,
                requiresAdministrator: true)
        ];
    }

    public override async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var statuses = new List<OperationStatus>(_steps.Length);
        foreach (var step in _steps)
        {
            statuses.Add(await step.GetStatusAsync(ct).ConfigureAwait(false));
        }

        var disabled = statuses.All(status => status.IsCompliant);
        return OperationStatus.Aggregate(statuses, disabled, "CEIP 已完全退出。", "CEIP 仍有项目启用。");
    }

    public override async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        try
        {
            var completed = new List<IOptimizationOperation>(_steps.Length);
            foreach (var step in _steps)
            {
                var result = await step.ApplyAsync(ctx, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    var compensationSucceeded = true;
                    if (result.Changed)
                    {
                        var currentRestore = await step.RestoreAsync(ctx, CancellationToken.None).ConfigureAwait(false);
                        compensationSucceeded &= currentRestore.Success;
                    }

                    foreach (var prior in completed.AsEnumerable().Reverse())
                    {
                        var priorRestore = await prior.RestoreAsync(ctx, CancellationToken.None).ConfigureAwait(false);
                        compensationSucceeded &= priorRestore.Success;
                    }

                    return new OperationInvokeResult(
                        false,
                        !compensationSucceeded,
                        compensationSucceeded
                            ? $"{result.Message} 已回滚先前步骤。"
                            : $"{result.Message} 补偿不完整。");
                }

                if (result.Changed)
                {
                    completed.Add(step);
                }
            }

            return new OperationInvokeResult(true, completed.Count > 0, completed.Count > 0 ? "已退出 CEIP。" : "CEIP 已处于退出状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "隐私防护");
        }
    }

    public override async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        try
        {
            var changed = false;
            foreach (var step in _steps.Reverse())
            {
                var result = await step.RestoreAsync(ctx, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    return result;
                }

                changed |= result.Changed;
            }

            return new OperationInvokeResult(true, changed, changed ? "已恢复 CEIP 设置。" : "CEIP 没有可恢复的快照或已是默认状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "隐私防护");
        }
    }
}

/// <summary>
/// 屏蔽反馈提示。原始 <see cref="MultiRegistryValueOperation"/> 跨 HKCU 与 HKLM 两个键，
/// 拆为两个单值 OrchestratedRegistryDwordOperation 步骤，逐步骤原子补偿。
/// </summary>
public sealed class DisableFeedbackCompositeOperation : OrchestratedChangeOperation
{
    private const string BenefitText = "消除烦人的反馈请求弹窗。";
    private const string DrawbackText = "无法向微软反馈问题。";

    private readonly IOptimizationOperation[] _steps;

    public DisableFeedbackCompositeOperation(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority? confirmationAuthority)
        : base(
            orchestrator,
            "privacy.disable_feedback",
            "屏蔽反馈提示",
            "禁用 SIUF 计数与反馈通知。",
            "隐私防护",
            OperationType.Registry,
            OperationRiskLevel.Safe,
            BenefitText,
            DrawbackText,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        _steps =
        [
            new OrchestratedRegistryDwordOperation(
                orchestrator,
                "privacy.disable_feedback.siuf_period",
                "屏蔽反馈提示（SIUF 计数）",
                "将当前用户 Siuf\\Rules 的 PeriodInNanoSeconds 置 0。",
                "隐私防护",
                @"SOFTWARE\Microsoft\Siuf\Rules",
                "PeriodInNanoSeconds",
                0,
                OperationRiskLevel.Safe,
                BenefitText,
                DrawbackText,
                confirmationAuthority,
                requiresExplicitConfirmation: true,
                scopeKind: TargetScopeKind.CurrentUser,
                requiresAdministrator: false),
            new OrchestratedRegistryDwordOperation(
                orchestrator,
                "privacy.disable_feedback.notification_policy",
                "屏蔽反馈提示（通知策略）",
                "将 DataCollection 的 DoNotShowFeedbackNotifications 置 1。",
                "隐私防护",
                @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                "DoNotShowFeedbackNotifications",
                1,
                OperationRiskLevel.Safe,
                BenefitText,
                DrawbackText,
                confirmationAuthority,
                requiresExplicitConfirmation: true,
                scopeKind: TargetScopeKind.Machine,
                requiresAdministrator: true)
        ];
    }

    public override async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var statuses = new List<OperationStatus>(_steps.Length);
        foreach (var step in _steps)
        {
            statuses.Add(await step.GetStatusAsync(ct).ConfigureAwait(false));
        }

        var disabled = statuses.All(status => status.IsCompliant);
        return OperationStatus.Aggregate(statuses, disabled, "反馈提示已完全屏蔽。", "反馈提示仍有项目启用。");
    }

    public override async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        try
        {
            var completed = new List<IOptimizationOperation>(_steps.Length);
            foreach (var step in _steps)
            {
                var result = await step.ApplyAsync(ctx, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    var compensationSucceeded = true;
                    if (result.Changed)
                    {
                        var currentRestore = await step.RestoreAsync(ctx, CancellationToken.None).ConfigureAwait(false);
                        compensationSucceeded &= currentRestore.Success;
                    }

                    foreach (var prior in completed.AsEnumerable().Reverse())
                    {
                        var priorRestore = await prior.RestoreAsync(ctx, CancellationToken.None).ConfigureAwait(false);
                        compensationSucceeded &= priorRestore.Success;
                    }

                    return new OperationInvokeResult(
                        false,
                        !compensationSucceeded,
                        compensationSucceeded
                            ? $"{result.Message} 已回滚先前步骤。"
                            : $"{result.Message} 补偿不完整。");
                }

                if (result.Changed)
                {
                    completed.Add(step);
                }
            }

            return new OperationInvokeResult(true, completed.Count > 0, completed.Count > 0 ? "已屏蔽反馈提示。" : "反馈提示已处于屏蔽状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "隐私防护");
        }
    }

    public override async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct)
    {
        try
        {
            var changed = false;
            foreach (var step in _steps.Reverse())
            {
                var result = await step.RestoreAsync(ctx, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    return result;
                }

                changed |= result.Changed;
            }

            return new OperationInvokeResult(true, changed, changed ? "已恢复反馈提示设置。" : "反馈提示没有可恢复的快照或已是默认状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "隐私防护");
        }
    }
}
