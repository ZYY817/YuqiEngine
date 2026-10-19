using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class OptimizationGuardServiceSafetyTests
{
    [Fact]
    public async Task DetectionOnlyGuardNotifiesChangedOperationWithoutApplyingIt()
    {
        var operation = new ChangedOperation();
        var engine = new TestEngine(operation);
        using var guard = new OptimizationGuardService(engine, new TestRestoreStore(), NullLogger<OptimizationGuardService>.Instance);
        var notifications = new List<string>();

        guard.OperationsReverted += (_, args) => notifications.AddRange(args.RevertedOperations);
        await guard.AddProtectedOperationsAsync([operation.Id]);

        await guard.CheckNowAsync();

        Assert.Equal([operation.Id], notifications);
        Assert.Equal(0, operation.ApplyCalls);
    }

    private sealed class TestEngine(ChangedOperation operation) : IOptimizationEngine
    {
        public IReadOnlyList<OptimizationModuleDescriptor> Modules { get; } =
            [new("guard-test", "Guard test", "Guard test", [operation])];

        public Task<OptimizationBatchResult> ApplyAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OptimizationBatchResult> ApplyAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OptimizationBatchResult> ApplyAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<OperationStatus>> GetStatusesAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ChangedOperation : IOptimizationOperation
    {
        public string Id => "guard-test:changed-operation";
        public string Title => "Changed operation";
        public string Description => "Changed operation";
        public string Category => "test";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin => false;
        public int ApplyCalls { get; private set; }

        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationStatus(false, "changed outside the application"));

        public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return Task.FromResult(new OperationInvokeResult(true, true, "applied"));
        }

        public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationInvokeResult(true, true, "restored"));
    }

    private sealed class TestRestoreStore : IOperationRestoreStore
    {
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
