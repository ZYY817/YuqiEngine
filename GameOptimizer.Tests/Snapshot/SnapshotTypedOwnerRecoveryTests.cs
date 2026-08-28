using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Tests.Snapshot;

public sealed class SnapshotTypedOwnerRecoveryTests
{
    [Fact]
    public void V4_and_V5_restore_only_manifest_resources_with_unique_typed_owners()
    {
        var v4 = SnapshotV4();
        var v5 = SnapshotV5();

        var v4Steps = SnapshotService.BuildTypedSteps(v4);
        var v5Steps = SnapshotService.BuildTypedSteps(v5);

        Assert.Equal(4, v4Steps.Count);
        Assert.Equal(12, v5Steps.Count);
        Assert.All(v4Steps.Concat(v5Steps), step =>
        {
            Assert.True(step.Supported);
            Assert.Equal(SnapshotRestoreResourceKind.TypedCapability, step.ResourceKind);
            Assert.False(string.IsNullOrWhiteSpace(step.CapabilityId));
        });
    }

    [Fact]
    public void Legacy_tail_resources_are_explicitly_unsupported_and_never_count_as_typed_steps()
    {
        var snapshot = SnapshotV5();
        snapshot.OperationStates["legacy.operation"] = true;
        snapshot.Settings["HKEY_LOCAL_MACHINE\\SOFTWARE\\Legacy|Value|Legacy"] = 1;
        snapshot.ActivePowerPlanGuid = Guid.NewGuid().ToString();
        snapshot.ServiceStates["LegacySvc"] = 2;
        snapshot.BcdSettings["legacybcd"] = "yes";

        var unsupported = SnapshotService.BuildUnsupportedLegacySteps(snapshot);

        Assert.Equal(5, unsupported.Count);
        Assert.All(unsupported, step =>
        {
            Assert.False(step.Supported);
            Assert.Null(step.CapabilityId);
            Assert.StartsWith("snapshot.", step.ReasonCode, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Partial_typed_failure_runs_reverse_compensation_and_persists_evidence()
    {
        var snapshot = SnapshotV4();
        var steps = SnapshotService.BuildTypedSteps(snapshot);
        var plan = Plan(steps);
        var result = new SnapshotRestoreResult
        {
            BatchPlanId = plan.PlanId,
            BatchState = SnapshotRestoreBatchState.Restoring
        };
        var store = new InMemorySnapshotRestoreBatchStore();
        await store.RegisterAsync(plan, default);
        await store.SetClaimingAsync(plan, default);
        await store.BeginAsync(plan, Guid.NewGuid(), default);
        var orchestrator = new Orchestrator(
            [ChangeOutcome.SucceededVerified, ChangeOutcome.FailedNoChange]);
        var execution = new SnapshotService.SnapshotTypedBatchExecution(
            plan,
            result,
            orchestrator,
            store);

        var completed = await SnapshotService.RestoreTypedRegistryDwordsAsync(
            snapshot,
            result,
            orchestrator,
            new Confirmation(),
            TargetScope.CurrentUser("S-1-5-21-test"),
            execution,
            default);
        await execution.CompensateAsync();

        Assert.False(completed);
        Assert.Equal(1, orchestrator.RestoreCount);
        Assert.Equal(0, result.RestoredCount);
        Assert.Equal(1, result.CompensatedCount);
        Assert.Equal(SnapshotCompensationStatus.VerifiedBeforeState, result.CompensationStatus);
        Assert.Single(result.CompensationReceipts);
        Assert.Equal("RestoredVerified", result.CompensationReceipts[0].Outcome);
        var batch = await store.LoadAsync(plan.PlanId, plan.PlanSha256, default);
        Assert.NotNull(batch);
        Assert.Contains(batch.Receipts, receipt => receipt.State == SnapshotRestoreStepState.Compensating);
        Assert.Contains(batch.Receipts, receipt => receipt.State == SnapshotRestoreStepState.CompensatedVerified);
    }

    [Fact]
    public async Task Applied_but_unverified_readback_is_failure_and_is_compensated()
    {
        var snapshot = SnapshotV4();
        var plan = Plan(SnapshotService.BuildTypedSteps(snapshot));
        var result = new SnapshotRestoreResult
        {
            BatchPlanId = plan.PlanId,
            BatchState = SnapshotRestoreBatchState.Restoring
        };
        var store = new InMemorySnapshotRestoreBatchStore();
        await store.RegisterAsync(plan, default);
        await store.SetClaimingAsync(plan, default);
        await store.BeginAsync(plan, Guid.NewGuid(), default);
        var orchestrator = new Orchestrator([ChangeOutcome.AppliedButUnverified]);
        var execution = new SnapshotService.SnapshotTypedBatchExecution(
            plan,
            result,
            orchestrator,
            store);

        var completed = await SnapshotService.RestoreTypedRegistryDwordsAsync(
            snapshot,
            result,
            orchestrator,
            new Confirmation(),
            TargetScope.CurrentUser("S-1-5-21-test"),
            execution,
            default);
        await execution.CompensateAsync();

        Assert.False(completed);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, orchestrator.RestoreCount);
        Assert.Equal(1, result.CompensatedCount);
        Assert.Equal(SnapshotCompensationStatus.VerifiedBeforeState, result.CompensationStatus);
        Assert.Contains(result.TypedReceipts, receipt =>
            receipt.Outcome == nameof(ChangeOutcome.AppliedButUnverified));
        var batch = await store.LoadAsync(plan.PlanId, plan.PlanSha256, default);
        Assert.NotNull(batch);
        Assert.Contains(batch.Receipts, receipt => receipt.State == SnapshotRestoreStepState.Failed);
        Assert.Contains(batch.Receipts, receipt => receipt.State == SnapshotRestoreStepState.CompensatedVerified);
    }

    [Fact]
    public async Task Known_unsupported_steps_are_persisted_before_any_typed_owner_call()
    {
        var snapshot = SnapshotV4();
        snapshot.ServiceStates["LegacySvc"] = 2;
        var steps = SnapshotService.BuildTypedSteps(snapshot)
            .Concat(SnapshotService.BuildUnsupportedLegacySteps(snapshot))
            .ToArray();
        var plan = Plan(steps);
        var result = new SnapshotRestoreResult
        {
            BatchPlanId = plan.PlanId,
            BatchState = SnapshotRestoreBatchState.Restoring
        };
        var store = new InMemorySnapshotRestoreBatchStore();
        await store.RegisterAsync(plan, default);
        await store.SetClaimingAsync(plan, default);
        await store.BeginAsync(plan, Guid.NewGuid(), default);
        var orchestrator = new Orchestrator([ChangeOutcome.SucceededVerified]);
        var execution = new SnapshotService.SnapshotTypedBatchExecution(
            plan,
            result,
            orchestrator,
            store);

        var blocked = await SnapshotService.RecordUnsupportedBeforeWriteAsync(
            plan,
            result,
            execution);

        Assert.True(blocked);
        Assert.False(result.Success);
        Assert.Equal(1, result.UnsupportedCount);
        Assert.Equal(SnapshotMutationStatus.None, result.MutationStatus);
        Assert.Equal(0, orchestrator.ExecuteCount);
        Assert.Equal(0, orchestrator.RestoreCount);
        var batch = await store.LoadAsync(plan.PlanId, plan.PlanSha256, default);
        Assert.NotNull(batch);
        Assert.Single(batch.Receipts);
        Assert.All(batch.Receipts, receipt =>
            Assert.Equal(SnapshotRestoreStepState.Unsupported, receipt.State));
    }

    [Fact]
    public async Task Compensation_failure_is_unknown_and_persists_manual_recovery_evidence()
    {
        var snapshot = SnapshotV4();
        var plan = Plan(SnapshotService.BuildTypedSteps(snapshot));
        var result = new SnapshotRestoreResult
        {
            BatchPlanId = plan.PlanId,
            BatchState = SnapshotRestoreBatchState.Restoring
        };
        var store = new InMemorySnapshotRestoreBatchStore();
        await store.RegisterAsync(plan, default);
        await store.SetClaimingAsync(plan, default);
        await store.BeginAsync(plan, Guid.NewGuid(), default);
        var orchestrator = new Orchestrator(
            [ChangeOutcome.SucceededVerified, ChangeOutcome.FailedNoChange],
            ChangeOutcome.AppliedButUnverified);
        var execution = new SnapshotService.SnapshotTypedBatchExecution(
            plan,
            result,
            orchestrator,
            store);

        var completed = await SnapshotService.RestoreTypedRegistryDwordsAsync(
            snapshot,
            result,
            orchestrator,
            new Confirmation(),
            TargetScope.CurrentUser("S-1-5-21-test"),
            execution,
            default);
        await execution.CompensateAsync();

        Assert.False(completed);
        Assert.Equal(SnapshotCompensationStatus.FailedOrUnknown, result.CompensationStatus);
        Assert.Equal(SnapshotMutationStatus.Unknown, result.MutationStatus);
        var batch = await store.LoadAsync(plan.PlanId, plan.PlanSha256, default);
        Assert.NotNull(batch);
        Assert.Contains(batch.Receipts, receipt => receipt.State == SnapshotRestoreStepState.CompensationFailed);
    }

    [Fact]
    public void Snapshot_restore_surface_contains_no_raw_system_writer_or_legacy_operation_tail()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(
            root,
            @"GameOptimizer.Core\Services\Snapshot\SnapshotService.cs"));
        var helper = File.ReadAllText(Path.Combine(
            root,
            @"GameOptimizer.Core\Services\Snapshot\RegistryBackupHelper.cs"));

        Assert.DoesNotContain("SafeRegistryHelper.SetValue", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo(\"powercfg\"", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo(\"sc\"", service, StringComparison.Ordinal);
        Assert.DoesNotContain("reg\", $\"import", service, StringComparison.Ordinal);
        Assert.DoesNotContain("reg\", $\"import", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreOperationStatesAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreRegistrySetting", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreServiceState", service, StringComparison.Ordinal);
        Assert.DoesNotContain("new NullRestoreStore", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectOperationStatesAsync", service, StringComparison.Ordinal);
        Assert.True(
            service.IndexOf("RecordUnsupportedBeforeWriteAsync", StringComparison.Ordinal)
            < service.IndexOf("RestoreTypedRegistryDwordsAsync", StringComparison.Ordinal));
        Assert.Contains("!IsTypedRestoreSchemaVersionSupported(snapshot.Version)", service, StringComparison.Ordinal);
        Assert.Contains("snapshot.deep-registry-typed-owner-unavailable", service, StringComparison.Ordinal);
    }

    private static OptimizationSnapshot SnapshotV4() => new()
    {
        Version = 4,
        TypedRegistryDwordStates =
        [
            new(RepairChangeCapabilityIds.GameDvrAppCapture, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR|AppCaptureEnabled|游戏录制", true, 1),
            new(RepairChangeCapabilityIds.GameDvrEnabled, @"HKEY_CURRENT_USER\System\GameConfigStore|GameDVR_Enabled|游戏模式", true, 0),
            new(RepairChangeCapabilityIds.GameModeAutoEnable, @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\GameBar|AutoGameModeEnabled|Game Bar", false, null),
            new("basic.disable_game_bar", @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\GameBar|ShowStartupPanel|Game Bar", true, 1)
        ]
    };

    private static OptimizationSnapshot SnapshotV5()
    {
        var snapshot = SnapshotV4();
        snapshot.Version = 5;
        snapshot.TypedRegistryCapabilityStates =
        [
            Capability("peripheral.mouse_precision", @"Control Panel\Mouse",
                Value("MouseSpeed", "1"), Value("MouseThreshold1", "6"), Value("MouseThreshold2", "10")),
            Capability("peripheral.keyboard_response", @"Control Panel\Keyboard",
                Value("KeyboardDelay", "2"), Value("KeyboardSpeed", "25")),
            Capability("input.disable_pointer_trails", @"Control Panel\Mouse", Missing("MouseTrails")),
            Capability("input.disable_snap_to", @"Control Panel\Mouse", Value("SnapToDefaultButton", "1")),
            Capability("privacy.deny_app_access_to_calendar", Consent("appointments"), Value("Value", "Allow")),
            Capability("privacy.deny_app_access_to_contacts", Consent("contacts"), Missing("Value")),
            Capability("privacy.deny_app_access_to_tasks", Consent("userDataTasks"), Value("Value", "Deny")),
            Capability("privacy.deny_app_access_to_notifications", Consent("userNotificationListener"), Value("Value", "Prompt"))
        ];
        return snapshot;
    }

    private static SnapshotTypedRegistryCapabilityState Capability(
        string capabilityId,
        string subKeyPath,
        params SnapshotTypedRegistryValueState[] values) =>
        new(capabilityId, subKeyPath, values);

    private static SnapshotTypedRegistryValueState Value(string valueName, string value) =>
        new(valueName, SnapshotRegistryScalarKind.String, true, value, null);

    private static SnapshotTypedRegistryValueState Missing(string valueName) =>
        new(valueName, SnapshotRegistryScalarKind.String, false, null, null);

    private static string Consent(string leaf) =>
        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{leaf}";

    private static SnapshotRestorePlan Plan(IReadOnlyList<SnapshotRestoreStepPlan> steps)
    {
        var now = DateTimeOffset.UtcNow;
        return new SnapshotRestorePlan(
            SnapshotRestoreCoordinator.CurrentSchemaVersion,
            Guid.NewGuid(),
            "snapshot_test",
            SnapshotRestoreKind.Normal,
            new string('A', 64),
            steps,
            new string('B', 64),
            now,
            now.AddMinutes(2));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class Orchestrator(
        IEnumerable<ChangeOutcome> outcomes,
        ChangeOutcome restoreOutcome = ChangeOutcome.RestoredVerified) : IChangeOrchestrator
    {
        private readonly Queue<ChangeOutcome> _outcomes = new(outcomes);
        public int ExecuteCount { get; private set; }
        public int RestoreCount { get; private set; }

        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken)
        {
            var change = new PreparedCapabilityChange(
                request.CapabilityId,
                "1.0.0",
                request.Scope,
                "before",
                "before-fp",
                request.TargetPayload,
                "target-fp",
                "resource:" + request.CapabilityId);
            var plan = new PreparedChangePlan(
                Guid.NewGuid(),
                change,
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1),
                false,
                true,
                true,
                ChangeRisk.MediumRebootOrExperience);
            return Task.FromResult(new PreparationResult(
                ChangePreparationStatus.Ready,
                plan,
                "ready",
                "ready",
                null));
        }

        public Task<BatchReceipt> ExecuteAsync(
            PreparedChangePlan plan,
            ConfirmationToken confirmation,
            CancellationToken cancellationToken)
        {
            ExecuteCount++;
            var outcome = _outcomes.Dequeue();
            var mutation = outcome is ChangeOutcome.SucceededVerified
                or ChangeOutcome.AppliedButUnverified
                or ChangeOutcome.FailedPartial
                or ChangeOutcome.CancelledAfterPartialWrite
                or ChangeOutcome.RecoveryRequired;
            return Task.FromResult(Receipt(
                plan,
                outcome,
                mutation,
                mutation ? Guid.NewGuid() : null));
        }

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken)
        {
            RestoreCount++;
            var scope = TargetScope.CurrentUser("S-1-5-21-test");
            return Task.FromResult(new BatchReceipt(new OperationReceipt(
                Guid.NewGuid(),
                Guid.NewGuid(),
                request.RecoveryId,
                "test.capability",
                scope,
                restoreOutcome,
                "before",
                "target",
                "before",
                restoreOutcome == ChangeOutcome.RestoredVerified ? "restored" : "unverified",
                restoreOutcome == ChangeOutcome.RestoredVerified ? "restored" : "restore verification failed",
                restoreOutcome != ChangeOutcome.NoChange,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)));
        }

        private static BatchReceipt Receipt(
            PreparedChangePlan plan,
            ChangeOutcome outcome,
            bool mutation,
            Guid? recoveryId) =>
            new(new OperationReceipt(
                Guid.NewGuid(),
                plan.PlanId,
                recoveryId,
                plan.Change.CapabilityId,
                plan.Change.Scope,
                outcome,
                plan.Change.BeforeFingerprint,
                plan.Change.TargetFingerprint,
                outcome == ChangeOutcome.SucceededVerified ? plan.Change.TargetFingerprint : null,
                outcome == ChangeOutcome.SucceededVerified ? "succeeded" : "failed",
                outcome == ChangeOutcome.SucceededVerified ? "done" : "failed",
                mutation,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
    }

    private sealed class Confirmation : IChangeConfirmationAuthority
    {
        public Task<ConfirmationToken?> RequestAsync(
            PreparedChangePlan plan,
            string title,
            string summary,
            CancellationToken cancellationToken) =>
            Task.FromResult<ConfirmationToken?>(new ConfirmationToken(
                Guid.NewGuid(),
                plan.PlanHash,
                plan.Change.Scope,
                plan.ExpiresAtUtc,
                true));

        public bool TryConsume(PreparedChangePlan plan, ConfirmationToken token) => true;
    }
}
