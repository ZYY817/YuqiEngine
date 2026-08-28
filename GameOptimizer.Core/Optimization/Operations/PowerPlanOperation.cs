using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class PowerPlanOperation : IOptimizationOperation
{
    private const int StateRetryCount = 3;
    private const int StateRetryDelayMs = 500;
    private static readonly Guid HighPerformanceScheme = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private readonly Guid _targetScheme;

    private sealed record PowerPlanSnapshot(string PreviousScheme);

    public PowerPlanOperation(
        string id,
        string title,
        string description,
        string category,
        Guid? targetScheme = null)
    {
        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _targetScheme = targetScheme ?? HighPerformanceScheme;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; }
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public OperationType OperationType => OperationType.PowerPlan;
    public string? ManualPath => "powercfg.cpl";
    public string? TargetName => "Power plan";
    public string? TargetValue => "High performance";
    public string? ManualSteps => "1. Open Run with Win+R\n2. Enter powercfg.cpl\n3. Select the High performance plan\n4. Close Control Panel";

    public async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var current = await ReadCurrentSchemeWithRetryAsync(cancellationToken).ConfigureAwait(false);
        if (!current.HasValue)
        {
            return new OperationStatus(false, "Unable to detect the active power plan.");
        }

        var compliant = current.Value == _targetScheme;
        var details = compliant
            ? $"Active power plan is {current.Value:D}."
            : $"Active power plan is {current.Value:D}, expected {_targetScheme:D}.";
        return new OperationStatus(compliant, details);
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var current = await ReadCurrentSchemeWithRetryAsync(cancellationToken).ConfigureAwait(false);
        if (!current.HasValue)
        {
            return new OperationInvokeResult(false, false, "Unable to detect the current power plan before apply.");
        }

        if (current.Value == _targetScheme)
        {
            return new OperationInvokeResult(true, false, "Power plan already matches the target.");
        }

        var snapshot = new PowerPlanSnapshot(current.Value.ToString("D"));
        await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

        var applyResult = await ActivateSchemeAsync(_targetScheme, "switch", cancellationToken).ConfigureAwait(false);
        if (applyResult.Success)
        {
            return new OperationInvokeResult(true, true, applyResult.Message);
        }

        return await FailApplyAndTryRollbackAsync(context, snapshot, applyResult.Message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<PowerPlanSnapshot>(Id, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || !Guid.TryParse(snapshot.PreviousScheme, out var previous))
        {
            return new OperationInvokeResult(false, false, "Cannot restore power plan because the snapshot is missing or invalid.");
        }

        var current = await ReadCurrentSchemeWithRetryAsync(cancellationToken).ConfigureAwait(false);
        if (!current.HasValue)
        {
            return new OperationInvokeResult(false, false, "Unable to detect the current power plan before restore.");
        }

        if (current.Value == previous)
        {
            return new OperationInvokeResult(true, false, $"Power plan already restored to {previous:D}.");
        }

        var restoreResult = await ActivateSchemeAsync(previous, "restore", cancellationToken).ConfigureAwait(false);
        return restoreResult.Success
            ? new OperationInvokeResult(true, true, restoreResult.Message)
            : new OperationInvokeResult(false, false, restoreResult.Message);
    }

    private async Task<OperationInvokeResult> FailApplyAndTryRollbackAsync(
        OperationExecutionContext context,
        PowerPlanSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalSchemeAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        return new OperationInvokeResult(false, false, $"{failureMessage} {rollback.Message}");
    }

    private async Task<(bool Success, string Message)> TryRestoreOriginalSchemeAsync(
        OperationExecutionContext context,
        PowerPlanSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(snapshot.PreviousScheme, out var previous))
        {
            return (false, "Original power plan snapshot is invalid, so automatic rollback could not run.");
        }

        var current = await ReadCurrentSchemeWithRetryAsync(cancellationToken).ConfigureAwait(false);
        if (current.HasValue && current.Value == previous)
        {
            await context.TryDeleteSnapshotAsync(Id, cancellationToken).ConfigureAwait(false);
            return (true, $"Original power plan remained {previous:D}; stale snapshot removed.");
        }

        var rollbackResult = await ActivateSchemeAsync(previous, "restore", cancellationToken).ConfigureAwait(false);
        if (!rollbackResult.Success)
        {
            return (false, $"Automatic rollback failed. {rollbackResult.Message}");
        }

        await context.TryDeleteSnapshotAsync(Id, cancellationToken).ConfigureAwait(false);
        return (true, $"Original power plan restored to {previous:D}.");
    }

    private static async Task<(bool Success, string Message)> ActivateSchemeAsync(Guid scheme, string actionLabel, CancellationToken cancellationToken)
    {
        var result = await PowerCfgUtility.RunAsync($"/SETACTIVE {scheme:D}", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return (false, BuildCommandFailureMessage($"Failed to {actionLabel} power plan.", result));
        }

        var verified = await VerifySchemeAsync(scheme, cancellationToken).ConfigureAwait(false);
        if (!verified.HasValue)
        {
            return (false, $"Power plan {actionLabel} command returned success, but the active plan could not be verified.");
        }

        if (verified.Value != scheme)
        {
            return (false, $"Power plan {actionLabel} verification failed. Current active plan is {verified.Value:D}, expected {scheme:D}.");
        }

        return (true, actionLabel == "restore"
            ? $"Power plan restored to {scheme:D}."
            : $"Power plan switched to {scheme:D}.");
    }

    private static async Task<Guid?> ReadCurrentSchemeWithRetryAsync(CancellationToken cancellationToken)
    {
        Guid? last = null;
        for (var attempt = 0; attempt < StateRetryCount; attempt++)
        {
            last = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
            if (last.HasValue)
            {
                return last;
            }

            if (attempt < StateRetryCount - 1)
            {
                await Task.Delay(StateRetryDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        return last;
    }

    private static async Task<Guid?> VerifySchemeAsync(Guid expectedScheme, CancellationToken cancellationToken)
    {
        Guid? last = null;
        for (var attempt = 0; attempt < StateRetryCount; attempt++)
        {
            last = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
            if (last.HasValue && last.Value == expectedScheme)
            {
                return last;
            }

            if (attempt < StateRetryCount - 1)
            {
                await Task.Delay(StateRetryDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        return last;
    }

    private static string BuildCommandFailureMessage(string prefix, (int ExitCode, string StdOut, string StdErr) result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StdErr)
            ? result.StdOut
            : result.StdErr;
        detail = string.IsNullOrWhiteSpace(detail)
            ? $"ExitCode={result.ExitCode}"
            : detail.Trim();
        return $"{prefix} {detail}";
    }
}