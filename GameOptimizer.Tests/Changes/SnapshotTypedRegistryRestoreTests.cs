using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Snapshot;
using System.Text.Json;

namespace GameOptimizer.Tests.Changes;

public sealed class SnapshotTypedRegistryRestoreTests
{
    [Fact]
    public void V4_checksum_binds_typed_manifest_value_and_absence()
    {
        var snapshot = Snapshot();
        var original = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
        snapshot.TypedRegistryDwordStates[0] = snapshot.TypedRegistryDwordStates[0] with { Value = 0 };
        var changedValue = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
        snapshot.TypedRegistryDwordStates[0] = snapshot.TypedRegistryDwordStates[0] with { ValueExists = false, Value = null };
        var changedAbsence = SnapshotChecksumCalculator.ComputeChecksum(snapshot);

        Assert.NotEqual(original, changedValue);
        Assert.NotEqual(changedValue, changedAbsence);
    }

    [Fact]
    public async Task Failure_stops_following_items_and_preserves_prior_durable_receipt()
    {
        var orchestrator = new FakeOrchestrator([ChangeOutcome.SucceededVerified, ChangeOutcome.FailedNoChange]);
        var result = new SnapshotRestoreResult();

        var completed = await SnapshotService.RestoreTypedRegistryDwordsAsync(
            Snapshot(), result, orchestrator, new FakeConfirmationAuthority(),
            TargetScope.CurrentUser("S-1-5-21-test"), default);

        Assert.False(completed);
        Assert.Equal(2, orchestrator.ExecuteCount);
        Assert.Equal(2, result.TypedReceipts.Count);
        Assert.NotNull(result.TypedReceipts[0].RecoveryId);
        Assert.Equal("FailedNoChange", result.TypedReceipts[1].Outcome);
        Assert.Equal(1, result.RestoredCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public async Task All_four_domains_return_structured_receipts()
    {
        var orchestrator = new FakeOrchestrator(Enumerable.Repeat(ChangeOutcome.SucceededVerified, 4));
        var result = new SnapshotRestoreResult();

        var completed = await SnapshotService.RestoreTypedRegistryDwordsAsync(
            Snapshot(), result, orchestrator, new FakeConfirmationAuthority(),
            TargetScope.CurrentUser("S-1-5-21-test"), default);

        Assert.True(completed);
        Assert.Equal(4, result.TypedReceipts.Count);
        Assert.All(result.TypedReceipts, receipt =>
        {
            Assert.NotEqual(Guid.Empty, receipt.ReceiptId);
            Assert.NotNull(receipt.RecoveryId);
            Assert.Equal("SucceededVerified", receipt.Outcome);
        });
    }

    [Fact]
    public async Task Incomplete_manifest_is_blocked_before_prepare()
    {
        var snapshot = Snapshot();
        snapshot.TypedRegistryDwordStates.RemoveAt(0);
        var orchestrator = new FakeOrchestrator([]);
        var result = new SnapshotRestoreResult();

        var completed = await SnapshotService.RestoreTypedRegistryDwordsAsync(
            snapshot, result, orchestrator, new FakeConfirmationAuthority(),
            TargetScope.CurrentUser("S-1-5-21-test"), default);

        Assert.False(completed);
        Assert.Equal(0, orchestrator.PrepareCount);
        Assert.Contains("snapshot.typed-manifest-incomplete", result.Errors);
    }

    [Theory]
    [InlineData(ChangeOutcome.Denied)]
    [InlineData(ChangeOutcome.Conflict)]
    [InlineData(ChangeOutcome.CancelledBeforeWrite)]
    [InlineData(ChangeOutcome.FailedPartial)]
    public async Task Permission_drift_cancel_and_partial_failure_stop_batch(ChangeOutcome terminal)
    {
        var orchestrator = new FakeOrchestrator([terminal, ChangeOutcome.SucceededVerified]);
        var result = new SnapshotRestoreResult();

        var completed = await SnapshotService.RestoreTypedRegistryDwordsAsync(
            Snapshot(), result, orchestrator, new FakeConfirmationAuthority(),
            TargetScope.CurrentUser("S-1-5-21-test"), default);

        Assert.False(completed);
        Assert.Equal(1, orchestrator.ExecuteCount);
        Assert.Single(result.TypedReceipts);
        Assert.Equal(terminal.ToString(), result.TypedReceipts[0].Outcome);
        Assert.Equal(terminal == ChangeOutcome.FailedPartial, result.TypedReceipts[0].RecoveryId.HasValue);
    }

    [Fact]
    public void V5_checksum_binds_string_value_kind_and_absence()
    {
        var snapshot = SnapshotV5();
        var original = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
        var first = snapshot.TypedRegistryCapabilityStates[0];
        snapshot.TypedRegistryCapabilityStates[0] = first with
        {
            Values = first.Values.Select((value, index) => index == 0 ? value with { StringValue = "2" } : value).ToArray()
        };
        var changedValue = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
        first = snapshot.TypedRegistryCapabilityStates[0];
        snapshot.TypedRegistryCapabilityStates[0] = first with
        {
            Values = first.Values.Select((value, index) => index == 0
                ? value with { ValueExists = false, StringValue = null }
                : value).ToArray()
        };
        var changedAbsence = SnapshotChecksumCalculator.ComputeChecksum(snapshot);

        Assert.NotEqual(original, changedValue);
        Assert.NotEqual(changedValue, changedAbsence);
    }

    [Fact]
    public async Task V5_all_eight_domains_return_receipts_and_preserve_exact_targets()
    {
        var orchestrator = new FakeOrchestrator(Enumerable.Repeat(ChangeOutcome.SucceededVerified, 8));
        var result = new SnapshotRestoreResult();

        var completed = await SnapshotService.RestoreTypedRegistryCapabilitiesAsync(
            SnapshotV5(), result, orchestrator, new FakeConfirmationAuthority(),
            TargetScope.CurrentUser("S-1-5-21-test"), default);

        Assert.True(completed);
        Assert.Equal(8, orchestrator.ExecuteCount);
        Assert.Equal(8, result.TypedReceipts.Count);
        using var mouse = JsonDocument.Parse(orchestrator.Requests[0].TargetPayload);
        var mouseValues = mouse.RootElement.GetProperty("Values");
        Assert.Equal(3, mouseValues.GetArrayLength());
        Assert.Equal("1", mouseValues[0].GetProperty("StringValue").GetString());
        Assert.Equal(0, mouseValues[0].GetProperty("Action").GetInt32());
        using var pointer = JsonDocument.Parse(orchestrator.Requests[2].TargetPayload);
        Assert.Equal(1, pointer.RootElement.GetProperty("Action").GetInt32());
        Assert.Equal(JsonValueKind.Null, pointer.RootElement.GetProperty("Value").ValueKind);
    }

    [Fact]
    public async Task V5_failure_stops_following_items_and_keeps_prior_recovery()
    {
        var orchestrator = new FakeOrchestrator([ChangeOutcome.SucceededVerified, ChangeOutcome.Conflict]);
        var result = new SnapshotRestoreResult();

        var completed = await SnapshotService.RestoreTypedRegistryCapabilitiesAsync(
            SnapshotV5(), result, orchestrator, new FakeConfirmationAuthority(),
            TargetScope.CurrentUser("S-1-5-21-test"), default);

        Assert.False(completed);
        Assert.Equal(2, orchestrator.ExecuteCount);
        Assert.NotNull(result.TypedReceipts[0].RecoveryId);
        Assert.Equal("Conflict", result.TypedReceipts[1].Outcome);
    }

    [Fact]
    public async Task V5_incomplete_or_kind_mismatched_manifest_blocks_before_prepare()
    {
        var snapshot = SnapshotV5();
        var first = snapshot.TypedRegistryCapabilityStates[0];
        snapshot.TypedRegistryCapabilityStates[0] = first with
        {
            Values = first.Values.Select((value, index) => index == 0
                ? value with
            {
                Kind = SnapshotRegistryScalarKind.DWord,
                StringValue = null,
                DwordValue = 1
            }
                : value).ToArray()
        };
        var orchestrator = new FakeOrchestrator([]);
        var result = new SnapshotRestoreResult();

        var completed = await SnapshotService.RestoreTypedRegistryCapabilitiesAsync(
            snapshot, result, orchestrator, new FakeConfirmationAuthority(),
            TargetScope.CurrentUser("S-1-5-21-test"), default);

        Assert.False(completed);
        Assert.Equal(0, orchestrator.PrepareCount);
        Assert.Contains("snapshot.v5-typed-entry-invalid:peripheral.mouse_precision", result.Errors);
    }

    private static OptimizationSnapshot Snapshot() => new()
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
        var snapshot = Snapshot();
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
        string capabilityId, string subKeyPath, params SnapshotTypedRegistryValueState[] values) =>
        new(capabilityId, subKeyPath, values);

    private static SnapshotTypedRegistryValueState Value(string valueName, string value) =>
        new(valueName, SnapshotRegistryScalarKind.String, true, value, null);

    private static SnapshotTypedRegistryValueState Missing(string valueName) =>
        new(valueName, SnapshotRegistryScalarKind.String, false, null, null);

    private static string Consent(string leaf) =>
        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{leaf}";

    private sealed class FakeOrchestrator : IChangeOrchestrator
    {
        private readonly Queue<ChangeOutcome> _outcomes;
        public FakeOrchestrator(IEnumerable<ChangeOutcome> outcomes) => _outcomes = new(outcomes);
        public int PrepareCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public List<ChangeRequest> Requests { get; } = [];

        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken)
        {
            PrepareCount++;
            Requests.Add(request);
            var change = new PreparedCapabilityChange(request.CapabilityId, "1.0.0", request.Scope, "before", "before-fp", request.TargetPayload, "target-fp", "resource:" + request.CapabilityId);
            var plan = new PreparedChangePlan(Guid.NewGuid(), change, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), false, true, true, ChangeRisk.MediumRebootOrExperience);
            return Task.FromResult(new PreparationResult(ChangePreparationStatus.Ready, plan, "ready", "ready", null));
        }

        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken)
        {
            ExecuteCount++;
            var outcome = _outcomes.Dequeue();
            var success = outcome == ChangeOutcome.SucceededVerified;
            var mutation = success || outcome is ChangeOutcome.FailedPartial or ChangeOutcome.CancelledAfterPartialWrite or ChangeOutcome.RecoveryRequired;
            return Task.FromResult(new BatchReceipt(new OperationReceipt(
                Guid.NewGuid(), plan.PlanId, mutation ? Guid.NewGuid() : null, plan.Change.CapabilityId,
                plan.Change.Scope, outcome, plan.Change.BeforeFingerprint, plan.Change.TargetFingerprint,
                success ? plan.Change.TargetFingerprint : null, success ? "succeeded" : "failed",
                success ? "done" : "failed", mutation, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        }

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeConfirmationAuthority : IChangeConfirmationAuthority
    {
        public Task<ConfirmationToken?> RequestAsync(PreparedChangePlan plan, string title, string summary, CancellationToken cancellationToken) =>
            Task.FromResult<ConfirmationToken?>(new ConfirmationToken(Guid.NewGuid(), plan.PlanHash, plan.Change.Scope, plan.ExpiresAtUtc, true));

        public bool TryConsume(PreparedChangePlan plan, ConfirmationToken token) => true;
    }
}
