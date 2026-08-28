// File: GameOptimizer.Core\Optimization\Operations\MultiServiceCompositeOperation.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// 多服务禁用复合操作。原始 <see cref="MultiServiceToggleOperation"/> 一次性调整多个服务的启动类型，
/// 缺少逐服务原子快照、漂移检测与逆序补偿；此处拆为多个单服务 OrchestratedServiceOptimizationOperation
/// 步骤，每个步骤复用既有 Machine 服务 Owner（ServiceChangeCapability），逐步骤原子补偿，
/// 失败时回滚已完成的步骤。目标为禁用（EnsureStopped + Disabled）。
/// </summary>
public sealed class MultiServiceCompositeOperation : OrchestratedChangeOperation
{
    private readonly IOptimizationOperation[] _steps;

    public MultiServiceCompositeOperation(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority? confirmationAuthority,
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string? benefit,
        string? drawback,
        IReadOnlyList<string> serviceNames)
        : base(
            orchestrator,
            id,
            title,
            description,
            category,
            OperationType.Service,
            riskLevel,
            benefit,
            drawback,
            confirmationAuthority,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        ArgumentNullException.ThrowIfNull(serviceNames);
        if (serviceNames.Count == 0)
        {
            throw new ArgumentException("至少需要一个服务名。", nameof(serviceNames));
        }

        _steps = serviceNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(serviceName => (IOptimizationOperation)new OrchestratedServiceOptimizationOperation(
                orchestrator,
                $"{id}.{serviceName.ToLowerInvariant()}",
                $"禁用 {serviceName}",
                $"将服务 {serviceName} 停止并设为禁用。",
                category,
                serviceName,
                riskLevel,
                benefit,
                drawback,
                confirmationAuthority))
            .ToArray();
    }

    public override async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var statuses = new List<OperationStatus>(_steps.Length);
        foreach (var step in _steps)
        {
            statuses.Add(await step.GetStatusAsync(ct).ConfigureAwait(false));
        }

        var disabled = statuses.All(status => status.IsCompliant);
        return OperationStatus.Aggregate(statuses, disabled, "所有目标服务已禁用。", "仍有目标服务未禁用。");
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

            return new OperationInvokeResult(true, completed.Count > 0, completed.Count > 0 ? "已禁用目标服务。" : "目标服务已处于禁用状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "系统服务");
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

            return new OperationInvokeResult(true, changed, changed ? "已恢复目标服务。" : "目标服务没有可恢复的快照或已是原状态。");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "系统服务");
        }
    }
}
