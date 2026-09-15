using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;
using Xunit;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairBatchLifecycleTests
{
    [Fact]
    public async Task Failure_stops_following_items_and_preserves_real_receipts()
    {
        var executor = new FakeExecutor();
        executor.Handlers["repair.hardware_acceleration"] = (_, _) =>
            Task.FromResult(RepairResult.Succeeded("first") with
            {
                ChangeReceipt = Receipt("repair.hardware_acceleration", mutationOccurred: true)
            });
        executor.Handlers["repair.feedback"] = (_, _) =>
            Task.FromResult(RepairResult.PermissionDenied(
                "permission revoked",
                "permission.denied") with
            {
                ChangeReceipt = Receipt(
                    "repair.feedback",
                    mutationOccurred: true,
                    compensationAttempted: true)
            });
        executor.Handlers["repair.dpi_reset"] = (_, _) =>
            throw new Xunit.Sdk.XunitException("the aborted item must not execute");

        var service = CreateService(executor);
        var items = Items(service, "repair.hardware_acceleration", "repair.feedback", "repair.dpi_reset");

        var results = await service.RepairAllAsync(items);

        Assert.Equal(3, results.Count);
        Assert.Equal(
            ["repair.hardware_acceleration", "repair.feedback"],
            executor.ExecuteCalls);
        Assert.True(results[0].Success);
        Assert.NotNull(results[0].ChangeReceipt?.Operations.Single().RecoveryId);
        Assert.Equal(RepairResultCode.PermissionDenied, results[1].Code);
        Assert.True(results[1].ChangeReceipt?.CompensationAttempted == true);
        Assert.Equal(RepairResultCode.Blocked, results[2].Code);
        Assert.Equal("repair.batch-aborted", results[2].ReasonCode);
        Assert.False(results[2].Success);
    }

    [Fact]
    public async Task Cancellation_stops_following_items_with_explicit_cancelled_results()
    {
        var executor = new FakeExecutor();
        executor.Handlers["repair.hardware_acceleration"] = (_, _) =>
            Task.FromResult(RepairResult.Succeeded("first"));
        executor.Handlers["repair.feedback"] = (_, _) =>
            Task.FromResult(RepairResult.Cancelled("cancelled", "cancelled.repair"));
        executor.Handlers["repair.dpi_reset"] = (_, _) =>
            throw new Xunit.Sdk.XunitException("the cancelled batch must not execute later items");

        var service = CreateService(executor);
        var items = Items(service, "repair.hardware_acceleration", "repair.feedback", "repair.dpi_reset");

        var results = await service.RepairAllAsync(items);

        Assert.Equal(3, results.Count);
        Assert.Equal(2, executor.ExecuteCalls.Count);
        Assert.Equal(RepairResultCode.Cancelled, results[1].Code);
        Assert.Equal(RepairResultCode.Cancelled, results[2].Code);
        Assert.Equal("cancelled.batch-aborted", results[2].ReasonCode);
    }

    [Theory]
    [InlineData(RepairResultCode.Blocked)]
    [InlineData(RepairResultCode.PolicyDenied)]
    [InlineData(RepairResultCode.VersionUnsupported)]
    [InlineData(RepairResultCode.Failed)]
    public async Task Blocking_or_execution_failure_stops_the_batch(RepairResultCode code)
    {
        var executor = new FakeExecutor();
        executor.Handlers["repair.hardware_acceleration"] = (_, _) =>
            Task.FromResult(ResultFor(code));
        executor.Handlers["repair.feedback"] = (_, _) =>
            throw new Xunit.Sdk.XunitException("a stopped batch must not execute later items");

        var service = CreateService(executor);
        var items = Items(service, "repair.hardware_acceleration", "repair.feedback");

        var results = await service.RepairAllAsync(items);

        Assert.Equal(2, results.Count);
        Assert.Single(executor.ExecuteCalls);
        Assert.Equal(code, results[0].Code);
        Assert.Equal(RepairResultCode.Blocked, results[1].Code);
        Assert.Equal("repair.batch-aborted", results[1].ReasonCode);
    }

    [Fact]
    public async Task Retry_after_failure_runs_again_after_batch_gate_is_released()
    {
        var attempt = 0;
        var executor = new FakeExecutor();
        executor.Handlers["repair.hardware_acceleration"] = (_, _) =>
            Task.FromResult(++attempt == 1
                ? RepairResult.Failed("transient failure", reasonCode: "execution.transient")
                : RepairResult.Succeeded("retry succeeded"));
        var service = CreateService(executor);
        var item = Items(service, "repair.hardware_acceleration").Single();

        var first = await service.RepairAllAsync([item]);
        var second = await service.RepairAllAsync([item]);

        Assert.Equal(RepairResultCode.Failed, first.Single().Code);
        Assert.True(second.Single().Success);
        Assert.Equal(2, executor.ExecuteCalls.Count);
    }

    [Fact]
    public async Task Dynamic_revert_can_be_mixed_without_using_a_legacy_delegate()
    {
        var dynamicItem = DynamicRevert("revert.batch.dynamic", "optimization.dynamic");
        var executor = new FakeExecutor();
        executor.Handlers["repair.hardware_acceleration"] = (_, _) =>
            Task.FromResult(RepairResult.Succeeded("static one"));
        executor.Handlers["repair.feedback"] = (_, _) =>
            Task.FromResult(RepairResult.Succeeded("static two"));
        var revert = new FakeRevertService([dynamicItem])
        {
            RevertHandler = (_, _) => Task.FromResult(RepairResult.Succeeded("dynamic revert"))
        };
        var service = CreateService(executor, revert);
        var items = Items(service, "repair.hardware_acceleration", dynamicItem.Id, "repair.feedback");

        var results = await service.RepairAllAsync(items);

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(["repair.hardware_acceleration", "repair.feedback"], executor.ExecuteCalls);
        Assert.Equal(["optimization.dynamic"], revert.RevertCalls);
    }

    [Fact]
    public async Task Duplicate_batch_trigger_is_blocked_and_gate_is_released_after_completion()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new FakeExecutor();
        executor.Handlers["repair.hardware_acceleration"] = async (_, _) =>
        {
            entered.TrySetResult(true);
            await release.Task.ConfigureAwait(false);
            return RepairResult.Succeeded("completed");
        };
        var service = CreateService(executor);
        var item = Items(service, "repair.hardware_acceleration").Single();

        Task<IReadOnlyList<RepairResult>> firstTask = service.RepairAllAsync([item]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var duplicate = await service.RepairAllAsync([item]);

        Assert.Equal(RepairResultCode.Blocked, duplicate.Single().Code);
        Assert.Equal("repair.batch-busy", duplicate.Single().ReasonCode);
        Assert.Single(executor.ExecuteCalls);

        release.TrySetResult(true);
        var first = await firstTask;
        var afterRelease = await service.RepairAllAsync([item]);

        Assert.True(first.Single().Success);
        Assert.True(afterRelease.Single().Success);
        Assert.Equal(2, executor.ExecuteCalls.Count);
    }

    [Fact]
    public async Task PreCancelled_batch_returns_cancelled_items_without_locking_future_batches()
    {
        var executor = new FakeExecutor();
        executor.Handlers["repair.hardware_acceleration"] = (_, _) =>
            Task.FromResult(RepairResult.Succeeded("ok"));
        var service = CreateService(executor);
        var item = Items(service, "repair.hardware_acceleration").Single();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await service.RepairAllAsync([item], cancellationToken: cancellation.Token);
        var afterCancellation = await service.RepairAllAsync([item]);

        Assert.Equal(RepairResultCode.Cancelled, cancelled.Single().Code);
        Assert.True(afterCancellation.Single().Success);
        Assert.Single(executor.ExecuteCalls);
    }

    private static RepairService CreateService(
        FakeExecutor executor,
        FakeRevertService? revert = null)
    {
        revert ??= new FakeRevertService([]);
        return new RepairService(
            new RepairItemRegistry(revert),
            new AllowGate(),
            executor,
            revert);
    }

    private static RepairItem[] Items(RepairService service, params string[] ids) =>
        ids.Select(id => service.GetAllRepairItems().Single(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))).ToArray();

    private static RepairItem DynamicRevert(string id, string operationId) => new()
    {
        Id = id,
        Name = id,
        Description = id,
        Category = RepairCategory.OptimizationRevert,
        RelatedOptimization = operationId,
        CheckHealthAsync = () => Task.FromResult(false),
        RepairAsync = _ => throw new Xunit.Sdk.XunitException("dynamic revert must use its dedicated service")
    };

    private static RepairResult ResultFor(RepairResultCode code) => code switch
    {
        RepairResultCode.Blocked => RepairResult.Blocked("blocked", "execution.blocked"),
        RepairResultCode.PolicyDenied => RepairResult.PolicyDenied("policy", "policy.denied"),
        RepairResultCode.VersionUnsupported => RepairResult.VersionUnsupported("unsupported", "os.unsupported"),
        RepairResultCode.Failed => RepairResult.Failed("failed", reasonCode: "execution.failed"),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null)
    };

    private static RepairChangeReceipt Receipt(
        string itemId,
        bool mutationOccurred,
        bool compensationAttempted = false) =>
        new(
            Guid.NewGuid(),
            itemId,
            [new(
                "fake.capability",
                Guid.NewGuid(),
                Guid.NewGuid(),
                ChangeOutcome.SucceededVerified,
                mutationOccurred)],
            compensationAttempted,
            DateTimeOffset.UtcNow);

    private sealed class AllowGate : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RepairExecutionGateDecision.Allowed());
    }

    private sealed class FakeExecutor : IRepairChangeExecutor
    {
        public Dictionary<string, Func<RepairItem, CancellationToken, Task<RepairResult>>> Handlers { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> ExecuteCalls { get; } = [];

        public bool IsManaged(string repairItemId) => Handlers.ContainsKey(repairItemId);

        public Task<RepairResult> ExecuteAsync(
            RepairItem item,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken)
        {
            ExecuteCalls.Add(item.Id);
            return Handlers[item.Id](item, cancellationToken);
        }

        public Task<RepairResult> RestoreAsync(
            RepairRecoveryRequest request,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRevertService(IReadOnlyList<RepairItem> items) : IOptimizationRevertService
    {
        public Func<string, CancellationToken, Task<RepairResult>> RevertHandler { get; init; } =
            (_, _) => Task.FromResult(RepairResult.Succeeded("dynamic"));

        public List<string> RevertCalls { get; } = [];

        public IReadOnlyList<RepairItem> GetAllRevertItems() => items;

        public bool IsDynamicRevertItem(string repairItemId) =>
            items.Any(item => string.Equals(item.Id, repairItemId, StringComparison.OrdinalIgnoreCase));

        public Task<OptimizationRevertStatus> GetStatusAsync(
            string operationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OptimizationRevertPreparation> PrepareAsync(
            string operationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RepairResult> ExecuteAsync(
            OptimizationRevertPlan plan,
            IProgress<RepairProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RepairResult> RevertOperationAsync(
            string operationId,
            IProgress<RepairProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            RevertCalls.Add(operationId);
            return RevertHandler(operationId, cancellationToken);
        }
    }
}
