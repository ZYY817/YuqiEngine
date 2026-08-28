using GameOptimizer.Core.SystemRestore;

namespace GameOptimizer.Infrastructure.Windows.SystemRestore;

/// <summary>
/// Runs the durable System Restore claim reconciliation once after the UI is available.
/// This adapter never invokes a platform mutation; it only reads and classifies durable state.
/// </summary>
public sealed class SystemRestorePointStartupReconciliation : ISystemRestorePointStartupReconciliation
{
    private readonly ISystemRestorePointOwner _owner;
    private readonly TimeSpan _timeout;
    private int _runStarted;

    public SystemRestorePointStartupReconciliationResult? LastResult { get; private set; }

    public SystemRestorePointStartupReconciliation(
        ISystemRestorePointOwner owner,
        TimeSpan? timeout = null)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Startup reconciliation timeout must be positive.");
        }
    }

    public async Task<SystemRestorePointStartupReconciliationResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _runStarted, 1) != 0)
        {
            return new(
                SystemRestorePointStartupReconciliationStatus.AlreadyRun,
                Array.Empty<SystemRestorePointWorkflowResult>(),
                "System Restore startup reconciliation was already attempted in this process.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Remember(new(
                SystemRestorePointStartupReconciliationStatus.Cancelled,
                Array.Empty<SystemRestorePointWorkflowResult>(),
                "System Restore startup reconciliation was cancelled before it began."));
        }

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);
        try
        {
            var results = await _owner.ReconcileInterruptedCreatesAsync(linkedCts.Token)
                .ConfigureAwait(false);
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return Remember(new(
                    SystemRestorePointStartupReconciliationStatus.TimedOut,
                    results,
                    "System Restore startup reconciliation timed out; the UI remains available and durable state stays fail-closed.",
                    ManualInterventionRequired: true));
            }
            if (cancellationToken.IsCancellationRequested
                || results.Any(result => result.Status == SystemRestorePointWorkflowStatus.Cancelled))
            {
                return Remember(new(
                    SystemRestorePointStartupReconciliationStatus.Cancelled,
                    results,
                    "System Restore startup reconciliation was cancelled; no platform mutation was attempted."));
            }
            var manual = results.Any(result => result.ManualInterventionRequired || result.Changed is null);
            return Remember(new(
                SystemRestorePointStartupReconciliationStatus.Completed,
                results,
                results.Count == 0
                    ? "No interrupted System Restore create claim required reconciliation."
                    : $"System Restore startup reconciliation classified {results.Count} durable claim(s).",
                manual));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Remember(new(
                SystemRestorePointStartupReconciliationStatus.TimedOut,
                Array.Empty<SystemRestorePointWorkflowResult>(),
                "System Restore startup reconciliation timed out; the UI remains available and durable state stays fail-closed.",
                ManualInterventionRequired: true));
        }
        catch (OperationCanceledException)
        {
            return Remember(new(
                SystemRestorePointStartupReconciliationStatus.Cancelled,
                Array.Empty<SystemRestorePointWorkflowResult>(),
                "System Restore startup reconciliation was cancelled; no platform mutation was attempted."));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Remember(new(
                SystemRestorePointStartupReconciliationStatus.Faulted,
                Array.Empty<SystemRestorePointWorkflowResult>(),
                $"System Restore startup reconciliation could not read its durable state because access was denied: {ex.Message}",
                ManualInterventionRequired: true));
        }
        catch (Exception ex)
        {
            return Remember(new(
                SystemRestorePointStartupReconciliationStatus.Faulted,
                Array.Empty<SystemRestorePointWorkflowResult>(),
                $"System Restore startup reconciliation failed closed: {ex.Message}",
                ManualInterventionRequired: true));
        }
    }

    private SystemRestorePointStartupReconciliationResult Remember(
        SystemRestorePointStartupReconciliationResult result)
    {
        LastResult = result;
        return result;
    }
}
