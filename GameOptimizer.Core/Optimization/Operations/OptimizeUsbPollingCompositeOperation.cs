// File: GameOptimizer.Core\Optimization\Operations\OptimizeUsbPollingCompositeOperation.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// 优化 USB 数据队列。原始 <see cref="OptimizeUsbPollingOperation"/> 跨两个 HKLM 服务键
/// （mouclass/kbdclass 的 Parameters），无法用单值 RegistryDwordCapability 表达；
/// 此处拆为两个单值 OrchestratedRegistryDwordOperation 步骤，复用现有 Machine 注册表 Owner，
/// 逐步骤原子补偿，失败时回滚已完成的步骤。
/// 目标值固定为 200（与 PeripheralSettingsProvider 的默认队列大小一致），恢复时由各步骤
/// 依据捕获的原值回写，缺失值恢复为删除。
/// </summary>
public sealed class OptimizeUsbPollingCompositeOperation : OrchestratedChangeOperation
{
    private const string BenefitText = "配合高轮询率鼠标使用，减少输入丢失，提升响应。";
    private const string DrawbackText = "轻微增加内存占用，且队列大小可能与个别驱动不兼容。";
    private const int TargetQueueSize = 200;

    private readonly IOptimizationOperation[] _steps;

    public OptimizeUsbPollingCompositeOperation(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority? confirmationAuthority)
        : base(
            orchestrator,
            "input.optimize_usb_polling",
            "优化 USB 数据队列",
            "增加鼠标和键盘的数据队列大小，配合高轮询率鼠标使用，减少输入丢失。",
            "输入延迟",
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
                "input.optimize_usb_polling.mouse_queue",
                "优化鼠标数据队列",
                "将 mouclass 的 MouseDataQueueSize 置为 200。",
                "输入延迟",
                @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
                "MouseDataQueueSize",
                TargetQueueSize,
                OperationRiskLevel.Safe,
                BenefitText,
                DrawbackText,
                confirmationAuthority,
                requiresExplicitConfirmation: true,
                scopeKind: TargetScopeKind.Machine,
                requiresAdministrator: true),
            new OrchestratedRegistryDwordOperation(
                orchestrator,
                "input.optimize_usb_polling.keyboard_queue",
                "优化键盘数据队列",
                "将 kbdclass 的 KeyboardDataQueueSize 置为 200。",
                "输入延迟",
                @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters",
                "KeyboardDataQueueSize",
                TargetQueueSize,
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

        var optimized = statuses.All(status => status.IsCompliant);
        return OperationStatus.Aggregate(statuses, optimized, "USB 数据队列已优化。", "USB 数据队列仍有项目未应用。");
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

            return new OperationInvokeResult(true, completed.Count > 0, completed.Count > 0 ? "已优化 USB 数据队列。" : "USB 数据队列已处于目标状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "输入延迟");
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

            return new OperationInvokeResult(true, changed, changed ? "已恢复 USB 数据队列设置。" : "USB 数据队列没有可恢复的快照或已是默认状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "输入延迟");
        }
    }
}
