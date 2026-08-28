using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Infrastructure.Windows.SystemRestore;

namespace GameOptimizer.Tests.SystemRestore;

public sealed class SystemRestorePointStartupReconciliationTests
{
    [Fact]
    public async Task RunAsync_ClassifiesUnknownWithoutTurningItIntoUnchanged()
    {
        var owner = new FakeOwner
        {
            Reconcile = _ => Task.FromResult<IReadOnlyList<SystemRestorePointWorkflowResult>>(new[]
            {
                new SystemRestorePointWorkflowResult(
                    false,
                    null,
                    SystemRestorePointWorkflowStatus.ManualInterventionRequired,
                    SystemRestorePointPhase.Reconcile,
                    "unknown",
                    ManualInterventionRequired: true)
            })
        };
        var startup = new SystemRestorePointStartupReconciliation(owner);

        var result = await startup.RunAsync();

        Assert.Equal(SystemRestorePointStartupReconciliationStatus.Completed, result.Status);
        Assert.True(result.ManualInterventionRequired);
        Assert.Null(Assert.Single(result.Results).Changed);
        Assert.Equal(1, owner.Calls);
    }

    [Fact]
    public async Task RunAsync_DurableDamageFailsClosedWithoutBlockingUiContract()
    {
        var owner = new FakeOwner
        {
            Reconcile = _ => Task.FromException<IReadOnlyList<SystemRestorePointWorkflowResult>>(
                new InvalidDataException("corrupt authenticated claim"))
        };
        var startup = new SystemRestorePointStartupReconciliation(owner);

        var result = await startup.RunAsync();

        Assert.Equal(SystemRestorePointStartupReconciliationStatus.Faulted, result.Status);
        Assert.True(result.ManualInterventionRequired);
        Assert.Contains("failed closed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_AccessDeniedFailsClosedWithoutThrowingToStartup()
    {
        var owner = new FakeOwner
        {
            Reconcile = _ => Task.FromException<IReadOnlyList<SystemRestorePointWorkflowResult>>(
                new UnauthorizedAccessException("fake access denied"))
        };
        var startup = new SystemRestorePointStartupReconciliation(owner);

        var result = await startup.RunAsync();

        Assert.Equal(SystemRestorePointStartupReconciliationStatus.Faulted, result.Status);
        Assert.True(result.ManualInterventionRequired);
        Assert.Contains("access was denied", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeStartIsExplicitAndDoesNotCallOwner()
    {
        var owner = new FakeOwner();
        var startup = new SystemRestorePointStartupReconciliation(owner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await startup.RunAsync(cancellation.Token);

        Assert.Equal(SystemRestorePointStartupReconciliationStatus.Cancelled, result.Status);
        Assert.Equal(0, owner.Calls);
    }

    [Fact]
    public async Task RunAsync_IsIdempotentAfterRestartClassificationAttempt()
    {
        var owner = new FakeOwner
        {
            Reconcile = _ => Task.FromResult<IReadOnlyList<SystemRestorePointWorkflowResult>>(Array.Empty<SystemRestorePointWorkflowResult>())
        };
        var startup = new SystemRestorePointStartupReconciliation(owner);

        var first = await startup.RunAsync();
        var second = await startup.RunAsync();

        Assert.Equal(SystemRestorePointStartupReconciliationStatus.Completed, first.Status);
        Assert.Equal(SystemRestorePointStartupReconciliationStatus.AlreadyRun, second.Status);
        Assert.Equal(1, owner.Calls);
    }

    [Fact]
    public async Task RunAsync_TimeoutIsDiagnosticAndDoesNotEscape()
    {
        var owner = new FakeOwner
        {
            Reconcile = async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Array.Empty<SystemRestorePointWorkflowResult>();
            }
        };
        var startup = new SystemRestorePointStartupReconciliation(owner, TimeSpan.FromMilliseconds(20));

        var result = await startup.RunAsync();

        Assert.Equal(SystemRestorePointStartupReconciliationStatus.TimedOut, result.Status);
        Assert.True(result.ManualInterventionRequired);
    }

    private sealed class FakeOwner : ISystemRestorePointOwner
    {
        public Func<CancellationToken, Task<IReadOnlyList<SystemRestorePointWorkflowResult>>>? Reconcile { get; init; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<SystemRestorePointWorkflowResult>> ReconcileInterruptedCreatesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Reconcile?.Invoke(cancellationToken)
                ?? Task.FromResult<IReadOnlyList<SystemRestorePointWorkflowResult>>(Array.Empty<SystemRestorePointWorkflowResult>());
        }

        public Task<SystemRestorePointWorkflowResult> ReconcileInterruptedCreateAsync(Guid planId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SystemRestorePointWorkflowResult> ResolveInterruptedCreateAsync(SystemRestorePointManualResolution resolution, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SystemRestorePointWorkflowResult> InspectAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemRestorePointWorkflowResult> PrepareCreateAsync(SystemRestorePointCreateRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public SystemRestorePointConfirmationToken? ConfirmCreate(SystemRestorePointCreatePlan plan, SystemRestorePointConfirmationEvidence evidence) => null;
        public Task<SystemRestorePointWorkflowResult> ExecuteCreateAsync(SystemRestorePointCreatePlan plan, SystemRestorePointConfirmationToken confirmation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemRestorePointWorkflowResult> PrepareSystemRestoreAsync(SystemRestorePointIdentity target, string source, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemRestorePointWorkflowResult> ExecuteSystemRestoreAsync(SystemRestoreExecutionPlan plan, SystemRestorePointConfirmationToken? confirmation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
