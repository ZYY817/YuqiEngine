using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// Compatibility facade for the retired ad-hoc power-plan workflow.
/// </summary>
/// <remarks>
/// The historical implementation created and activated arbitrary power plans directly.
/// The production <c>IPowerManagementOwner</c> owns a different, frozen display-timeout
/// workflow with confirmation, baseline, receipt, and recovery semantics. This facade
/// cannot faithfully translate an arbitrary plan request into that contract, so every
/// mutation and unmanaged inspection path is deliberately fail-closed.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class UnifiedPowerService
{
    private const string RetiredWorkflowMessage =
        "The legacy arbitrary power-plan workflow is unavailable because it is not backed by the Power owner contract.";

    private const string RetiredWorkflowAction =
        "Use the Power Management workflow, which prepares, confirms, verifies, and records a recoverable receipt.";

    /// <summary>
    /// Retained only for compatibility with startup diagnostics. It never creates a plan.
    /// </summary>
    public Task<PowerPlanResult> GetOrCreateGameOptimizerPlanAsync(CancellationToken ct = default)
    {
        return ct.IsCancellationRequested
            ? Task.FromCanceled<PowerPlanResult>(ct)
            : Task.FromResult(PowerPlanResult.Failure(RetiredWorkflowMessage, RetiredWorkflowAction));
    }

    /// <summary>
    /// Arbitrary scheme activation has no equivalent durable owner request and is blocked.
    /// </summary>
    public Task<bool> ActivatePlanAsync(Guid planGuid, CancellationToken ct = default)
    {
        return ct.IsCancellationRequested
            ? Task.FromCanceled<bool>(ct)
            : Task.FromResult(false);
    }

    /// <summary>
    /// Compatibility no-op: this facade no longer caches or probes platform state.
    /// </summary>
    public void ClearCache()
    {
    }

    /// <summary>
    /// No active-plan result is exposed until it can be represented by the Power owner.
    /// </summary>
    public Task<Guid?> GetActivePlanGuidAsync(CancellationToken ct = default)
    {
        return ct.IsCancellationRequested
            ? Task.FromCanceled<Guid?>(ct)
            : Task.FromResult<Guid?>(null);
    }
}

public sealed class PowerPlanResult
{
    public bool IsSuccess { get; init; }
    public Guid? PlanGuid { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
    public string? SuggestedAction { get; init; }

    public static PowerPlanResult Success(Guid planGuid, string message)
        => new() { IsSuccess = true, PlanGuid = planGuid, Message = message };

    public static PowerPlanResult Failure(string errorMessage, string suggestedAction)
        => new() { IsSuccess = false, ErrorMessage = errorMessage, SuggestedAction = suggestedAction };
}
