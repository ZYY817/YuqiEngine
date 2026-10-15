using System.Collections.Concurrent;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

public sealed class OptimizationRevertDurableTests
{
    [Fact]
    public async Task Repair_service_routes_dynamic_revert_to_durable_orchestrator_and_never_legacy_engine()
    {
        var harness = Harness.Create();
        var registry = new RepairItemRegistry(harness.Service);
        var repair = new RepairService(registry, new AllowGate(), optimizationRevertService: harness.Service);
        registry.Initialize();
        var item = registry.GetById("revert.test.operation");
        Assert.NotNull(item);

        using var cancellation = new CancellationTokenSource();
        GameOptimizer.Core.Services.Repair.RepairResult result = await repair.RepairAsync(item, cancellationToken: cancellation.Token);

        Assert.True(result.Success);
        Assert.Equal(0, harness.Engine.LegacyRestoreCalls);
        Assert.Equal(1, harness.Orchestrator.RestoreCalls);
        Assert.True(harness.Orchestrator.LastToken.CanBeCanceled);
    }

    [Fact]
    public async Task Legacy_two_field_pointer_is_recoverable_only_when_durable_material_completes_binding()
    {
        var harness = Harness.Create(legacyPointer: true);
        var preparation = await harness.Service.PrepareAsync("test.operation");
        Assert.NotNull(preparation.Plan);

        harness.Recovery.Material = null;
        var unavailable = Harness.Create(legacyPointer: true, recovery: harness.Recovery);
        OptimizationRevertPreparation blocked = await unavailable.Service.PrepareAsync("test.operation");

        Assert.Null(blocked.Plan);
        Assert.Equal(OptimizationRevertAvailability.Unrecoverable, blocked.Status.Availability);
    }

    [Fact]
    public async Task Audit_read_failure_preserves_independent_recovery_id_path()
    {
        var audit = new FakeAuditStore { ReadException = new IOException("damaged audit") };
        var harness = Harness.Create(audit: audit);

        var preparation = await harness.Service.PrepareAsync("test.operation");
        var result = await harness.Service.ExecuteAsync(Assert.IsType<OptimizationRevertPlan>(preparation.Plan));

        Assert.True(result.Success);
        Assert.Equal(1, harness.Orchestrator.RestoreCalls);
    }

    [Fact]
    public async Task Forged_or_replayed_plan_is_rejected_before_restore()
    {
        var harness = Harness.Create();
        var prepared = Assert.IsType<OptimizationRevertPlan>((await harness.Service.PrepareAsync("test.operation")).Plan);

        var forged = prepared with { ResourceKey = "registry:forged" };
        Assert.False((await harness.Service.ExecuteAsync(forged)).Success);
        Assert.Equal(0, harness.Orchestrator.RestoreCalls);

        var valid = Assert.IsType<OptimizationRevertPlan>((await harness.Service.PrepareAsync("test.operation")).Plan);
        Assert.True((await harness.Service.ExecuteAsync(valid)).Success);
        Assert.False((await harness.Service.ExecuteAsync(valid)).Success);
        Assert.Equal(1, harness.Orchestrator.RestoreCalls);
    }

    [Fact]
    public async Task Unknown_restore_outcome_is_durable_and_not_automatically_retried()
    {
        var harness = Harness.Create(ChangeOutcome.RecoveryRequired, "restore.verification-timeout");
        var plan = Assert.IsType<OptimizationRevertPlan>((await harness.Service.PrepareAsync("test.operation")).Plan);

        Assert.False((await harness.Service.ExecuteAsync(plan)).Success);
        var after = await harness.Service.PrepareAsync("test.operation");

        Assert.Null(after.Plan);
        Assert.Equal(OptimizationRevertAvailability.RestoringInterrupted, after.Status.Availability);
        Assert.Equal(1, harness.Orchestrator.RestoreCalls);
    }

    [Fact]
    public async Task Permission_denial_does_not_consume_adapter_plan_source()
    {
        var harness = Harness.Create(ChangeOutcome.Denied, "permission.denied");
        var plan = Assert.IsType<OptimizationRevertPlan>((await harness.Service.PrepareAsync("test.operation")).Plan);

        Assert.False((await harness.Service.ExecuteAsync(plan)).Success);
        Assert.NotNull((await harness.Service.PrepareAsync("test.operation")).Plan);
    }

    [Theory]
    [InlineData(ChangeOutcome.CancelledBeforeWrite, "restore.cancelled-before-write")]
    [InlineData(ChangeOutcome.Conflict, "restore.state-drift")]
    [InlineData(ChangeOutcome.Blocked, "capability.unknown")]
    [InlineData(ChangeOutcome.RestoreFailed, "restore.explicit-no-write")]
    public async Task Explicit_prewrite_or_zero_write_terminal_keeps_material_available(
        ChangeOutcome outcome,
        string reasonCode)
    {
        var harness = Harness.Create(outcome, reasonCode);
        var plan = Assert.IsType<OptimizationRevertPlan>((await harness.Service.PrepareAsync("test.operation")).Plan);

        Assert.False((await harness.Service.ExecuteAsync(plan)).Success);

        Assert.NotNull((await harness.Service.PrepareAsync("test.operation")).Plan);
    }

    [Fact]
    public async Task Cross_instance_race_allows_only_one_verified_restore()
    {
        var sharedStoreBacking = new FakeRestoreStoreBacking();
        var sharedClaim = new FakeDurableRestoreClaim();
        var firstStore = new FakeRestoreStore(sharedStoreBacking);
        var secondStore = new FakeRestoreStore(sharedStoreBacking);
        var firstOrchestrator = new FakeOrchestrator { DurableClaim = sharedClaim };
        var secondOrchestrator = new FakeOrchestrator { DurableClaim = sharedClaim };
        var first = Harness.Create(store: firstStore, orchestrator: firstOrchestrator);
        var second = Harness.Create(
            store: secondStore,
            audit: first.Audit,
            recovery: first.Recovery,
            orchestrator: secondOrchestrator,
            seed: false);
        var firstPlan = Assert.IsType<OptimizationRevertPlan>((await first.Service.PrepareAsync("test.operation")).Plan);
        var secondPlan = Assert.IsType<OptimizationRevertPlan>((await second.Service.PrepareAsync("test.operation")).Plan);

        var results = await Task.WhenAll(first.Service.ExecuteAsync(firstPlan), second.Service.ExecuteAsync(secondPlan));

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => !result.Success);
        Assert.Equal(1, sharedClaim.PlatformRestoreCalls);
    }

    [Fact]
    public async Task Already_restored_claim_is_global_state_not_a_second_successful_execution()
    {
        var harness = Harness.Create();
        var plan = Assert.IsType<OptimizationRevertPlan>((await harness.Service.PrepareAsync("test.operation")).Plan);
        harness.Orchestrator.ForceAlreadyRestored = true;

        var result = await harness.Service.ExecuteAsync(plan);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Orchestrator.PlatformRestoreCalls);
        Assert.Equal(OptimizationRevertAvailability.Restored, (await harness.Service.GetStatusAsync("test.operation")).Availability);
    }

    [Fact]
    public async Task Orchestrator_exception_persists_interrupted_state_and_blocks_replay()
    {
        var harness = Harness.Create();
        harness.Orchestrator.RestoreException = new IOException("fake restore transport failure");
        var plan = Assert.IsType<OptimizationRevertPlan>((await harness.Service.PrepareAsync("test.operation")).Plan);

        var result = await harness.Service.ExecuteAsync(plan);
        var replay = await harness.Service.PrepareAsync("test.operation");

        Assert.False(result.Success);
        Assert.Null(replay.Plan);
        Assert.Equal(OptimizationRevertAvailability.RestoringInterrupted, replay.Status.Availability);
        Assert.Equal(1, harness.Orchestrator.RestoreCalls);
    }

    [Fact]
    public async Task Cancelled_execution_does_not_reach_orchestrator_and_material_can_be_prepared_again()
    {
        var harness = Harness.Create();
        var plan = Assert.IsType<OptimizationRevertPlan>((await harness.Service.PrepareAsync("test.operation")).Plan);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.Service.ExecuteAsync(plan, cancellationToken: cancellation.Token));

        Assert.Equal(0, harness.Orchestrator.RestoreCalls);
        Assert.NotNull((await harness.Service.PrepareAsync("test.operation")).Plan);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("null")]
    public async Task Corrupt_durable_status_fails_closed_before_confirmation_or_restore(string payload)
    {
        var harness = Harness.Create();
        harness.Store.Values["durable-revert-status.test.operation"] = payload;

        var preparation = await harness.Service.PrepareAsync("test.operation");

        Assert.Null(preparation.Plan);
        Assert.Equal(OptimizationRevertAvailability.Unrecoverable, preparation.Status.Availability);
        Assert.Equal("revert.status-invalid", preparation.Status.ReasonCode);
        Assert.Equal(0, harness.Prompt.Calls);
        Assert.Equal(0, harness.Orchestrator.RestoreCalls);
    }

    [Fact]
    public async Task Version_drift_is_blocked_before_confirmation_and_restore()
    {
        var harness = Harness.Create();
        harness.Recovery.Material = harness.Material with { DefinitionVersion = "v2" };

        var preparation = await harness.Service.PrepareAsync("test.operation");

        Assert.Null(preparation.Plan);
        Assert.Equal(0, harness.Prompt.Calls);
        Assert.Equal(0, harness.Orchestrator.RestoreCalls);
    }

    [Fact]
    public async Task Compatibility_constructor_fails_closed_without_invoking_legacy_restore()
    {
        var engine = new FakeEngine(new FakeOperation());
        var service = new OptimizationRevertService(engine);

        var result = await service.RevertOperationAsync("test.operation");

        Assert.False(result.Success);
        Assert.Equal(0, engine.LegacyRestoreCalls);
        Assert.Contains("安全阻断", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corrupt_pointer_is_unrecoverable_and_never_reaches_confirmation()
    {
        var harness = Harness.Create();
        harness.Store.Values["test.operation"] = "{not-json";

        var preparation = await harness.Service.PrepareAsync("test.operation");

        Assert.Null(preparation.Plan);
        Assert.Equal(OptimizationRevertAvailability.Unrecoverable, preparation.Status.Availability);
        Assert.Equal(0, harness.Prompt.Calls);
        Assert.Equal(0, harness.Orchestrator.RestoreCalls);
    }

    private sealed class Harness
    {
        private Harness(
            FakeEngine engine,
            OptimizationRevertService service,
            FakeRestoreStore store,
            FakeAuditStore audit,
            FakeRecoveryRepository recovery,
            FakeOrchestrator orchestrator,
            FakePrompt prompt,
            RecoveryMaterial material)
        {
            Engine = engine;
            Service = service;
            Store = store;
            Audit = audit;
            Recovery = recovery;
            Orchestrator = orchestrator;
            Prompt = prompt;
            Material = material;
        }

        public FakeEngine Engine { get; }
        public OptimizationRevertService Service { get; }
        public FakeRestoreStore Store { get; }
        public FakeAuditStore Audit { get; }
        public FakeRecoveryRepository Recovery { get; }
        public FakeOrchestrator Orchestrator { get; }
        public FakePrompt Prompt { get; }
        public RecoveryMaterial Material { get; }

        public static Harness Create(
            ChangeOutcome outcome = ChangeOutcome.RestoredVerified,
            string reasonCode = "restore.verified",
            bool legacyPointer = false,
            FakeRestoreStore? store = null,
            FakeAuditStore? audit = null,
            FakeRecoveryRepository? recovery = null,
            FakeOrchestrator? orchestrator = null,
            bool seed = true)
        {
            var operation = new FakeOperation();
            var engine = new FakeEngine(operation);
            store ??= new FakeRestoreStore();
            var planId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var receiptId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var recoveryId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            var scope = TargetScope.CurrentUser("S-1-5-21-test");
            var material = new RecoveryMaterial(
                recoveryId,
                planId,
                operation.Id,
                "v1",
                scope,
                "before",
                "before-fp",
                "target-fp",
                "registry:test",
                DateTimeOffset.Parse("2026-08-13T00:00:00Z"),
                new string('A', 64));
            var source = new OperationReceipt(
                receiptId,
                planId,
                recoveryId,
                operation.Id,
                scope,
                ChangeOutcome.SucceededVerified,
                material.BeforeFingerprint,
                material.TargetFingerprint,
                material.TargetFingerprint,
                "execute.verified",
                "ok",
                true,
                material.CreatedAtUtc,
                material.CreatedAtUtc.AddSeconds(1));
            audit ??= new FakeAuditStore { Receipt = source };
            recovery ??= new FakeRecoveryRepository { Material = material };
            orchestrator ??= new FakeOrchestrator { Outcome = outcome, ReasonCode = reasonCode, Source = source };
            if (orchestrator.Source is null)
            {
                orchestrator.Source = source;
            }
            var prompt = new FakePrompt();
            if (seed)
            {
                object pointer = legacyPointer
                    ? new { ReceiptId = receiptId, RecoveryId = (Guid?)recoveryId }
                    : new OptimizationRecoveryPointer
                    {
                        SchemaVersion = 2,
                        OperationId = operation.Id,
                        ReceiptId = receiptId,
                        RecoveryId = recoveryId,
                        CapabilityId = operation.Id,
                        DefinitionVersion = material.DefinitionVersion,
                        ResourceKey = material.ResourceKey,
                        Scope = material.Scope,
                        BeforeFingerprint = material.BeforeFingerprint,
                        TargetFingerprint = material.TargetFingerprint
                    };
                store.Values[operation.Id] = JsonSerializer.Serialize(pointer, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }

            var service = new OptimizationRevertService(
                engine,
                store,
                audit,
                recovery,
                orchestrator,
                prompt,
                new FakeHistory());
            return new Harness(engine, service, store, audit, recovery, orchestrator, prompt, material);
        }
    }

    private sealed class FakeOperation : IOptimizationOperation
    {
        public string Id => "test.operation";
        public string Title => "Test operation";
        public string Description => "Test";
        public string Category => "system";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Balanced;
        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(new OperationStatus(true, "applied"));
        public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeEngine(IOptimizationOperation operation) : IOptimizationEngine
    {
        public int LegacyRestoreCalls { get; private set; }
        public IReadOnlyList<OptimizationModuleDescriptor> Modules { get; } =
            [new("test", "Test", "Test", [operation])];
        public Task<OptimizationBatchResult> ApplyAsync(string moduleKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OptimizationBatchResult> ApplyAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OptimizationBatchResult> ApplyAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, CancellationToken cancellationToken = default) { LegacyRestoreCalls++; throw new InvalidOperationException("legacy restore invoked"); }
        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, CancellationToken cancellationToken = default) { LegacyRestoreCalls++; throw new InvalidOperationException("legacy restore invoked"); }
        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, IEnumerable<string> operationIds, IOperationProgress? progress, CancellationToken cancellationToken = default) { LegacyRestoreCalls++; throw new InvalidOperationException("legacy restore invoked"); }
        public Task<IReadOnlyList<OperationStatus>> GetStatusesAsync(string moduleKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRestoreStoreBacking
    {
        public ConcurrentDictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakeRestoreStore(FakeRestoreStoreBacking? backing = null) : IOperationRestoreStore
    {
        private readonly FakeRestoreStoreBacking _backing = backing ?? new FakeRestoreStoreBacking();
        public ConcurrentDictionary<string, string> Values => _backing.Values;
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Values[operationId] = payload; return Task.CompletedTask; }
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Values.TryGetValue(operationId, out var value) ? value : null); }
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) { Values.TryRemove(operationId, out _); return Task.CompletedTask; }
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.FromResult(Values.ContainsKey(operationId));
    }

    private sealed class FakeAuditStore : IChangeAuditStore
    {
        public OperationReceipt? Receipt { get; init; }
        public Exception? ReadException { get; init; }
        public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<OperationReceipt?> LoadAsync(Guid receiptId, CancellationToken cancellationToken) =>
            ReadException is null ? Task.FromResult(Receipt) : Task.FromException<OperationReceipt?>(ReadException);
    }

    private sealed class FakeRecoveryRepository : IRecoveryRepository
    {
        public RecoveryMaterial? Material { get; set; }
        public Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken) { Material = material; return Task.CompletedTask; }
        public Task<RecoveryMaterial?> LoadAsync(Guid recoveryId, CancellationToken cancellationToken) => Task.FromResult(Material);
    }

    private sealed class FakeOrchestrator : IChangeOrchestrator
    {
        public int RestoreCalls;
        public int PlatformRestoreCalls;
        public bool ForceAlreadyRestored { get; set; }
        public Exception? RestoreException { get; set; }
        public FakeDurableRestoreClaim? DurableClaim { get; init; }
        public ChangeOutcome Outcome { get; init; } = ChangeOutcome.RestoredVerified;
        public string ReasonCode { get; init; } = "restore.verified";
        public OperationReceipt? Source { get; set; }
        public CancellationToken LastToken { get; private set; }
        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken, bool registerPlan = true) => throw new NotSupportedException();
        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref RestoreCalls);
            LastToken = cancellationToken;
            await Task.Yield();
            if (RestoreException is not null)
            {
                throw RestoreException;
            }

            if (ForceAlreadyRestored)
            {
                var alreadyRestored = Source!;
                return new BatchReceipt(alreadyRestored with
                {
                    ReceiptId = Guid.NewGuid(),
                    Outcome = ChangeOutcome.Blocked,
                    ReasonCode = "restore.already-restored",
                    Message = "already restored",
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });
            }

            var won = DurableClaim?.TryClaim() ?? true;
            if (won)
            {
                Interlocked.Increment(ref PlatformRestoreCalls);
            }
            var source = Source!;
            return new BatchReceipt(source with
            {
                ReceiptId = Guid.NewGuid(),
                Outcome = won ? Outcome : ChangeOutcome.Blocked,
                ReasonCode = won ? ReasonCode : "restore.already-restored",
                Message = won ? "restored" : "already restored",
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class FakeDurableRestoreClaim
    {
        private int _claimed;
        public int PlatformRestoreCalls;

        public bool TryClaim()
        {
            var claimed = Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
            if (claimed)
            {
                Interlocked.Increment(ref PlatformRestoreCalls);
            }

            return claimed;
        }
    }

    private sealed class FakePrompt : IChangeConfirmationPrompt
    {
        public int Calls { get; private set; }
        public Task<bool> ConfirmAsync(ChangeConfirmationPrompt prompt, CancellationToken cancellationToken) { Calls++; cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }

    private sealed class FakeHistory : IOperationHistoryService
    {
        public int TotalCount => Records.Count;
        public List<OperationHistoryRecord> Records { get; } = [];
        public void Record(OperationHistoryRecord record) => Records.Add(record);
        public void RecordBatch(IEnumerable<OperationHistoryRecord> records) => Records.AddRange(records);
        public IReadOnlyList<OperationHistoryRecord> GetRecent(int count = 50) => Records.Take(count).ToArray();
        public IReadOnlyList<OperationHistoryRecord> GetByDateRange(DateTime from, DateTime to) => [];
        public IReadOnlyList<OperationHistoryRecord> GetByModule(string moduleKey, int count = 50) => [];
        public IReadOnlyList<OperationHistorySummary> GetDailySummary(int days = 7) => [];
        public void ClearOlderThan(DateTime cutoff) { }
    }

    private sealed class AllowGate : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(string repairItemId, RepairExecutionIntent intent, CancellationToken cancellationToken = default) =>
            Task.FromResult(RepairExecutionGateDecision.Allowed());
    }
}
