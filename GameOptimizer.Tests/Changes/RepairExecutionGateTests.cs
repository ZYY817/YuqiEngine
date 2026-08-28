using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairExecutionGateTests
{
    [Fact]
    public void All_static_registry_items_default_to_apply_intent()
    {
        var registry = new RepairItemRegistry();
        registry.Initialize();

        IReadOnlyList<RepairItem> items = registry.GetAll();
        Assert.Equal(123, items.Count);
        Assert.Equal(123, items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(items, item => Assert.Equal(RepairExecutionIntent.Apply, item.ExecutionIntent));
    }

    [Fact]
    public async Task Allowed_gate_invokes_managed_writer_once()
    {
        var gate = new FakeGate(_ => RepairExecutionGateDecision.Allowed());
        int writes = 0;
        RepairItem item = Item("repair.allowed", () => writes++);
        RepairService service = CreateService(gate, item);

        RepairResult result = await service.RepairAsync(item);

        Assert.True(result.Success);
        Assert.Equal(1, writes);
        Assert.Equal([("repair.allowed", RepairExecutionIntent.Apply)], gate.Calls);
    }

    [Theory]
    [InlineData(RepairExecutionGateDecisionKind.BlockedNeedBaseline)]
    [InlineData(RepairExecutionGateDecisionKind.ReadonlyOnly)]
    public async Task Non_allowed_gate_is_zero_write(RepairExecutionGateDecisionKind kind)
    {
        var gate = new FakeGate(_ => new(kind, "baseline blocked"));
        int writes = 0;
        RepairItem item = Item("repair.blocked", () => writes++);
        RepairService service = CreateService(gate, item);

        RepairResult result = await service.RepairAsync(item);

        Assert.False(result.Success);
        Assert.Equal("baseline blocked", result.Message);
        Assert.Equal(
            kind == RepairExecutionGateDecisionKind.ReadonlyOnly
                ? RepairResultCode.Blocked
                : RepairResultCode.PolicyDenied,
            result.Code);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Gate_exception_fails_closed_with_zero_write()
    {
        var gate = new FakeGate(_ => throw new IOException("gate unavailable"));
        int writes = 0;
        RepairItem item = Item("repair.exception", () => writes++);
        RepairService service = CreateService(gate, item);

        RepairResult result = await service.RepairAsync(item);

        Assert.False(result.Success);
        Assert.Equal(0, writes);
        Assert.IsType<IOException>(result.Exception);
    }

    [Fact]
    public async Task Caller_cancellation_before_gate_is_zero_write()
    {
        var gate = new FakeGate(_ => RepairExecutionGateDecision.Allowed());
        int writes = 0;
        RepairItem item = Item("repair.cancelled", () => writes++);
        RepairService service = CreateService(gate, item);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        RepairResult result = await service.RepairAsync(item, cancellationToken: cts.Token);

        Assert.False(result.Success);
        Assert.Empty(gate.Calls);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Cancellation_raised_by_gate_is_zero_write()
    {
        var gate = new FakeGate(_ => throw new OperationCanceledException());
        int writes = 0;
        RepairItem item = Item("repair.gate-cancel", () => writes++);
        RepairService service = CreateService(gate, item);

        RepairResult result = await service.RepairAsync(item);

        Assert.False(result.Success);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Cancellation_observed_when_gate_returns_is_zero_write()
    {
        using var cts = new CancellationTokenSource();
        var gate = new FakeGate(_ =>
        {
            cts.Cancel();
            return RepairExecutionGateDecision.Allowed();
        });
        int writes = 0;
        RepairItem item = Item("repair.cancel-after-gate", () => writes++);
        RepairService service = CreateService(gate, item);

        RepairResult result = await service.RepairAsync(item, cancellationToken: cts.Token);

        Assert.False(result.Success);
        Assert.Single(gate.Calls);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Allowed_gate_does_not_enable_unmanaged_static_legacy_writer()
    {
        var gate = new FakeGate(_ => RepairExecutionGateDecision.Allowed());
        int writes = 0;
        RepairItem item = Item("repair.unmanaged", () => writes++);
        RepairService service = CreateUnmanagedService(gate, item);

        RepairResult result = await service.RepairAsync(item);

        Assert.False(result.Success);
        Assert.Contains("typed Owner/Executor", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, writes);
        Assert.Single(gate.Calls);
    }

    [Fact]
    public async Task Batch_rechecks_gate_before_every_item_and_stops_writing_after_state_change()
    {
        int gateCall = 0;
        var gate = new FakeGate(_ => ++gateCall == 1
            ? RepairExecutionGateDecision.Allowed()
            : new(RepairExecutionGateDecisionKind.ReadonlyOnly, "baseline became readonly"));
        var writes = new List<string>();
        RepairItem[] items =
        [
            Item("repair.one", () => writes.Add("one")),
            Item("repair.two", () => writes.Add("two")),
            Item("repair.three", () => writes.Add("three"))
        ];
        RepairService service = CreateService(gate, items);

        IReadOnlyList<RepairResult> results = await service.RepairAllAsync(items);

        Assert.Equal(2, gate.Calls.Count);
        Assert.Equal(["one"], writes);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.Equal(RepairResultCode.Blocked, results[2].Code);
        Assert.Equal("repair.batch-aborted", results[2].ReasonCode);
    }

    [Fact]
    public async Task Restore_intent_is_passed_explicitly_to_gate()
    {
        var gate = new FakeGate(call => call.Intent == RepairExecutionIntent.Restore
            ? RepairExecutionGateDecision.Allowed()
            : new(RepairExecutionGateDecisionKind.BlockedNeedBaseline, "apply blocked"));
        int writes = 0;
        RepairItem restore = Item("revert.operation", () => writes++) with
        {
            ExecutionIntent = RepairExecutionIntent.Restore,
            Category = RepairCategory.OptimizationRevert,
            RelatedOptimization = "operation"
        };
        RepairService service = CreateService(gate, restore);

        RepairResult result = await service.RepairAsync(restore);

        Assert.True(result.Success);
        Assert.Equal(1, writes);
        Assert.Equal(RepairExecutionIntent.Restore, Assert.Single(gate.Calls).Intent);
    }

    [Fact]
    public void Missing_gate_dependency_fails_closed_at_construction()
    {
        Assert.Throws<ArgumentNullException>(() => new RepairService(new RepairItemRegistry(), null!));
    }

    [Fact]
    public async Task Caller_cannot_forge_restore_intent_or_replace_registered_writer()
    {
        var gate = new FakeGate(call => call.Intent == RepairExecutionIntent.Restore
            ? RepairExecutionGateDecision.Allowed()
            : new(RepairExecutionGateDecisionKind.BlockedNeedBaseline, "apply blocked"));
        int canonicalWrites = 0;
        int forgedWrites = 0;
        RepairItem canonical = Item("repair.canonical", () => canonicalWrites++);
        RepairService service = CreateService(gate, canonical);
        RepairItem forged = canonical with
        {
            ExecutionIntent = RepairExecutionIntent.Restore,
            RepairAsync = _ =>
            {
                forgedWrites++;
                return Task.FromResult(RepairResult.Succeeded("forged"));
            }
        };

        RepairResult result = await service.RepairAsync(forged);

        Assert.False(result.Success);
        Assert.Equal(0, canonicalWrites);
        Assert.Equal(0, forgedWrites);
        Assert.Equal(RepairExecutionIntent.Apply, Assert.Single(gate.Calls).Intent);
    }

    [Fact]
    public async Task Unregistered_item_is_rejected_before_gate_and_writer()
    {
        var gate = new FakeGate(_ => RepairExecutionGateDecision.Allowed());
        int writes = 0;
        RepairService service = CreateService(gate);

        RepairResult result = await service.RepairAsync(Item("repair.unknown", () => writes++));

        Assert.False(result.Success);
        Assert.Empty(gate.Calls);
        Assert.Equal(0, writes);
    }

    private static RepairService CreateService(IRepairExecutionGate gate, params RepairItem[] items)
    {
        var revertService = new FakeRevertService(items);
        return new RepairService(
            new RepairItemRegistry(revertService),
            gate,
            new FakeManagedExecutor(items.Select(item => item.Id)),
            optimizationRevertService: revertService);
    }

    private static RepairService CreateUnmanagedService(IRepairExecutionGate gate, params RepairItem[] items)
    {
        var revertService = new FakeRevertService(items);
        return new RepairService(
            new RepairItemRegistry(revertService),
            gate,
            optimizationRevertService: revertService);
    }

    private static RepairItem Item(string id, Action writer) => new()
    {
        Id = id,
        Name = id,
        Description = id,
        CheckHealthAsync = () => Task.FromResult(false),
        RepairAsync = _ =>
        {
            writer();
            return Task.FromResult(RepairResult.Succeeded("ok"));
        }
    };

    private sealed class FakeGate(
        Func<(string ItemId, RepairExecutionIntent Intent), RepairExecutionGateDecision> decide)
        : IRepairExecutionGate
    {
        public List<(string ItemId, RepairExecutionIntent Intent)> Calls { get; } = [];

        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = (repairItemId, intent);
            Calls.Add(call);
            return Task.FromResult(decide(call));
        }
    }

    private sealed class FakeManagedExecutor(IEnumerable<string> managedIds) : IRepairChangeExecutor
    {
        private readonly HashSet<string> _managedIds = new(managedIds, StringComparer.OrdinalIgnoreCase);

        public bool IsManaged(string repairItemId) => _managedIds.Contains(repairItemId);

        public Task<RepairResult> ExecuteAsync(
            RepairItem item,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return item.RepairAsync(progress);
        }

        public Task<RepairResult> RestoreAsync(
            RepairRecoveryRequest request,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRevertService(IReadOnlyList<RepairItem> items) : IOptimizationRevertService
    {
        public IReadOnlyList<RepairItem> GetAllRevertItems() => items;

        public bool IsDynamicRevertItem(string repairItemId) =>
            items.Any(item => string.Equals(item.Id, repairItemId, StringComparison.OrdinalIgnoreCase));

        public Task<OptimizationRevertStatus> GetStatusAsync(string operationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OptimizationRevertPreparation> PrepareAsync(string operationId, CancellationToken cancellationToken = default) =>
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
            cancellationToken.ThrowIfCancellationRequested();
            var item = items.Single(candidate =>
                string.Equals(candidate.RelatedOptimization ?? candidate.Id["revert.".Length..], operationId, StringComparison.OrdinalIgnoreCase));
            return item.RepairAsync(progress);
        }
    }
}
