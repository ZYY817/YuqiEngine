using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairChangeExecutorTests
{
    [Fact]
    public async Task GameMode_UsesExistingOwnerAndSecondUniqueOwner()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item("repair.game_mode"), null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(
            [RepairChangeCapabilityIds.ExistingGameModeEnable, RepairChangeCapabilityIds.GameModeAutoEnable],
            orchestrator.PreparedCapabilityIds);
        Assert.Equal(2, result.ChangeReceipt?.Operations.Count);
    }

    [Fact]
    public async Task PermissionDenied_IsFailedWithoutExecute()
    {
        var orchestrator = new ScriptedOrchestrator
        {
            PreparationFactory = request => Blocked(request, "permission.denied", "Access denied")
        };
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item("repair.hardware_acceleration"), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Access denied", result.Message);
        Assert.Empty(orchestrator.ExecutedCapabilityIds);
    }

    [Theory]
    [InlineData("repair.dpi_reset", RepairChangeCapabilityIds.DpiReset)]
    [InlineData("repair.vsync", RepairChangeCapabilityIds.VsyncReset)]
    public async Task ResetRepair_UsesExpectedDeletionOwner(string repairId, string capabilityId)
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item(repairId), null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal([capabilityId], orchestrator.PreparedCapabilityIds);
    }

    [Fact]
    public async Task AdvertisingRepair_ReusesOptimizationOwnerWithExplicitFrozenDeleteTarget()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item("repair.advertising_id"), null, CancellationToken.None);

        Assert.True(result.Success);
        var request = Assert.Single(orchestrator.PreparedRequests);
        Assert.Equal(RepairChangeCapabilityIds.AdvertisingId, request.CapabilityId);
        Assert.NotEqual("apply", request.TargetPayload);
        Assert.Contains("\"ValueName\":\"Enabled\"", request.TargetPayload, StringComparison.Ordinal);
        Assert.Contains("\"Action\":0", request.TargetPayload, StringComparison.Ordinal);
        Assert.Contains("\"ValueName\":\"Id\"", request.TargetPayload, StringComparison.Ordinal);
        Assert.Contains("\"Action\":1", request.TargetPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameDvrRepair_ReusesBothOptimizationOwnersWithFrozenEnableTargets()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item("repair.game_dvr"), null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(
            [RepairChangeCapabilityIds.GameDvrEnabled, RepairChangeCapabilityIds.GameDvrAppCapture],
            orchestrator.PreparedCapabilityIds);
        Assert.All(orchestrator.PreparedRequests, request =>
        {
            Assert.Contains("\"Action\":0", request.TargetPayload, StringComparison.Ordinal);
            Assert.Contains("\"Value\":1", request.TargetPayload, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GameDvrSecondOwnerFailure_CompensatesCurrentAndPriorInReverse()
    {
        var execution = 0;
        var orchestrator = new ScriptedOrchestrator
        {
            ExecuteFactory = plan => ++execution == 1
                ? Receipt(plan, ChangeOutcome.SucceededVerified, "execution.verified", true, Guid.NewGuid())
                : Receipt(plan, ChangeOutcome.AppliedButUnverified, "verification.failed", true, Guid.NewGuid())
        };
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item("repair.game_dvr"), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(2, orchestrator.RestoreRequests.Count);
        Assert.Equal(
            orchestrator.PreparedCapabilityIds.AsEnumerable().Reverse(),
            orchestrator.RestoreRequests.Select((_, index) => orchestrator.PreparedCapabilityIds[1 - index]));
        Assert.True(result.ChangeReceipt?.CompensationAttempted);
    }

    [Fact]
    public async Task PrivacySettings_UsesThreeUniqueConsentOwnersWithAllowTargets()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item("repair.privacy_settings"), null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(
            [RepairChangeCapabilityIds.LocationConsent, RepairChangeCapabilityIds.WebcamConsent, RepairChangeCapabilityIds.MicrophoneConsent],
            orchestrator.PreparedCapabilityIds);
        Assert.All(orchestrator.PreparedRequests, request => Assert.Contains("\"Value\":\"Allow\"", request.TargetPayload, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("repair.hags", RepairChangeCapabilityIds.HagsEnable, true, 2)]
    [InlineData("repair.diagnostic_data", RepairChangeCapabilityIds.DiagnosticDataReset, true, 3)]
    [InlineData("repair.fast_startup", RepairChangeCapabilityIds.FastStartupEnable, true, 1)]
    [InlineData("repair.telemetry", RepairChangeCapabilityIds.TelemetryServiceReset, false, 0)]
    [InlineData("repair.memory_compression", RepairChangeCapabilityIds.MemoryCompressionEnable, false, 0)]
    [InlineData("repair.hibernation", RepairChangeCapabilityIds.HibernationEnable, false, 0)]
    [InlineData("repair.power_plan", RepairChangeCapabilityIds.PowerPlansReset, false, 0)]
    [InlineData("repair.power", RepairChangeCapabilityIds.PowerPlansReset, false, 0)]
    [InlineData("repair.high_performance", RepairChangeCapabilityIds.HighPerformancePlanRestore, false, 0)]
    [InlineData("repair.ultimate_performance", RepairChangeCapabilityIds.UltimatePerformancePlanRestore, false, 0)]
    [InlineData("repair.sleep", RepairChangeCapabilityIds.SleepPolicyReset, false, 0)]
    [InlineData("repair.core_parking", RepairChangeCapabilityIds.CoreParkingReset, false, 0)]
    [InlineData("repair.cpu_frequency", RepairChangeCapabilityIds.CpuFrequencyReset, false, 0)]
    [InlineData("repair.battery_report", RepairChangeCapabilityIds.BatteryReportGenerate, false, 0)]
    [InlineData("repair.power_troubleshoot", RepairChangeCapabilityIds.PowerTroubleshooterLaunch, false, 0)]
    [InlineData("repair.services_reset", RepairChangeCapabilityIds.ServicesReset, false, 0)]
    [InlineData("repair.bits", RepairChangeCapabilityIds.BitsEnsureRunning, false, 0)]
    [InlineData("repair.cryptsvc", RepairChangeCapabilityIds.CryptSvcEnsureRunning, false, 0)]
    [InlineData("repair.appinfo", RepairChangeCapabilityIds.AppInfoEnsureRunning, false, 0)]
    [InlineData("repair.themes", RepairChangeCapabilityIds.ThemesEnsureRunning, false, 0)]
    [InlineData("repair.audio_service", RepairChangeCapabilityIds.AudioServiceEnsureRunning, false, 0)]
    [InlineData("repair.shellhwdetection", RepairChangeCapabilityIds.ShellHardwareDetectionEnsureRunning, false, 0)]
    [InlineData("repair.plugplay", RepairChangeCapabilityIds.PlugPlayEnsureRunning, false, 0)]
    [InlineData("repair.netprofm", RepairChangeCapabilityIds.NetworkListEnsureRunning, false, 0)]
    [InlineData("repair.nlasvc", RepairChangeCapabilityIds.NetworkLocationEnsureRunning, false, 0)]
    [InlineData("repair.rpcss", RepairChangeCapabilityIds.RpcSsEnsureRunning, false, 0)]
    public async Task SixthBatch_UsesMachineOwnerAndNeverInvokesLegacyDelegate(
        string repairId,
        string capabilityId,
        bool typedDword,
        int expectedValue)
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item(repairId), null, CancellationToken.None);

        Assert.True(result.Success);
        var request = Assert.Single(orchestrator.PreparedRequests);
        Assert.Equal(capabilityId, request.CapabilityId);
        Assert.Equal(TargetScopeKind.Machine, request.Scope.Kind);
        Assert.Equal(Environment.MachineName, request.Scope.Identity, ignoreCase: true);
        if (typedDword)
        {
            Assert.Contains("\"Action\":0", request.TargetPayload, StringComparison.Ordinal);
            Assert.Contains($"\"Value\":{expectedValue}", request.TargetPayload, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("apply", request.TargetPayload);
        }
    }


    [Fact]
    public async Task CancellationBeforePrepare_IsZeroWrite()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await executor.ExecuteAsync(Item("repair.feedback"), null, cts.Token);

        Assert.False(result.Success);
        Assert.Empty(orchestrator.PreparedCapabilityIds);
        Assert.Empty(orchestrator.ExecutedCapabilityIds);
    }

    [Fact]
    public async Task CancellationBetweenCompositeSteps_CompensatesCompletedWrite()
    {
        using var cts = new CancellationTokenSource();
        var orchestrator = new ScriptedOrchestrator
        {
            ExecuteFactory = plan =>
            {
                cts.Cancel();
                return Receipt(plan, ChangeOutcome.SucceededVerified, "execution.verified", true, Guid.NewGuid());
            }
        };
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item("repair.game_mode"), null, cts.Token);

        Assert.False(result.Success);
        Assert.Single(orchestrator.ExecutedCapabilityIds);
        Assert.Single(orchestrator.RestoreRequests);
        Assert.True(result.ChangeReceipt?.CompensationAttempted);
    }

    [Theory]
    [InlineData(ChangeOutcome.Conflict, "state.drift", false)]
    [InlineData(ChangeOutcome.AppliedButUnverified, "verification.read-failed", true)]
    public async Task ExecuteFailure_IsReportedAndMutationReceiptIsCompensated(
        ChangeOutcome outcome,
        string reason,
        bool mutationOccurred)
    {
        var orchestrator = new ScriptedOrchestrator
        {
            ExecuteFactory = plan => Receipt(
                plan,
                outcome,
                reason,
                mutationOccurred,
                mutationOccurred ? Guid.NewGuid() : null)
        };
        var executor = CreateExecutor(orchestrator);

        var result = await executor.ExecuteAsync(Item("repair.hardware_acceleration"), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(mutationOccurred, result.ChangeReceipt?.CompensationAttempted);
        Assert.Equal(mutationOccurred ? 1 : 0, orchestrator.RestoreRequests.Count);
    }

    [Fact]
    public async Task Restore_UsesDurablePointersInReverseOrder_AndReplayFails()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);
        var applied = await executor.ExecuteAsync(Item("repair.game_mode"), null, CancellationToken.None);
        var request = new RepairRecoveryRequest("repair.game_mode", Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt));

        var restored = await executor.RestoreAsync(request, null, CancellationToken.None);
        orchestrator.RestoreFactory = restore => RestoreReceipt(restore, ChangeOutcome.Blocked, "recovery.replayed");
        var replay = await executor.RestoreAsync(request, null, CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal(2, orchestrator.RestoreRequests.Take(2).Count());
        Assert.Equal(request.Receipt.Operations[1].ReceiptId, orchestrator.RestoreRequests[0].ReceiptId);
        Assert.False(replay.Success);
        Assert.Equal(RepairResultCode.RecoveryRequired, replay.Code);
        Assert.Contains("recovery.replayed", replay.Message);
    }

    [Fact]
    public async Task Restore_RejectsMissingRecoveryIdWithoutCallingOrchestrator()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);
        var applied = await executor.ExecuteAsync(Item("repair.feedback"), null, CancellationToken.None);
        var source = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);
        var request = new RepairRecoveryRequest(
            "repair.feedback",
            source with { Operations = [source.Operations[0] with { RecoveryId = null }] });

        var result = await executor.RestoreAsync(request, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.RecoveryRequired, result.Code);
        Assert.Equal("restore.recovery-id-missing", result.ReasonCode);
        Assert.Empty(orchestrator.RestoreRequests);
    }

    [Fact]
    public async Task Restore_RejectsDuplicatePointerAndCapabilityForgeryBeforeOrchestrator()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);
        var applied = await executor.ExecuteAsync(Item("repair.feedback"), null, CancellationToken.None);
        var source = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);
        var pointer = source.Operations[0];

        var duplicate = await executor.RestoreAsync(
            new RepairRecoveryRequest(
                "repair.feedback",
                source with { Operations = [pointer, pointer] }),
            null,
            CancellationToken.None);
        var forgedCapability = await executor.RestoreAsync(
            new RepairRecoveryRequest(
                "repair.feedback",
                source with { Operations = [pointer with { CapabilityId = "forged.capability" }] }),
            null,
            CancellationToken.None);

        Assert.Equal(RepairResultCode.Blocked, duplicate.Code);
        Assert.Equal("restore.request-pointer-duplicate", duplicate.ReasonCode);
        Assert.Equal(RepairResultCode.Blocked, forgedCapability.Code);
        Assert.Equal("restore.request-capability-mismatch", forgedCapability.ReasonCode);
        Assert.Empty(orchestrator.RestoreRequests);
    }

    [Theory]
    [InlineData(ChangeOutcome.NoChange, "restore.no-op", RepairResultCode.RecoveryRequired)]
    [InlineData(ChangeOutcome.Blocked, "restore.material-invalid", RepairResultCode.RecoveryRequired)]
    [InlineData(ChangeOutcome.Blocked, "restore.already-restored", RepairResultCode.RecoveryRequired)]
    [InlineData(ChangeOutcome.RestoreFailed, "restore.read-current-failed", RepairResultCode.RecoveryRequired)]
    [InlineData(ChangeOutcome.RecoveryRequired, "restore.state-unknown", RepairResultCode.RecoveryRequired)]
    [InlineData(ChangeOutcome.CancelledAfterPartialWrite, "cancelled.after-write", RepairResultCode.RecoveryRequired)]
    [InlineData(ChangeOutcome.Denied, "permission.denied", RepairResultCode.PermissionDenied)]
    [InlineData(ChangeOutcome.Blocked, "version.unsupported", RepairResultCode.VersionUnsupported)]
    [InlineData(ChangeOutcome.Blocked, "policy.denied", RepairResultCode.PolicyDenied)]
    public async Task Restore_NeverReportsUnknownOrUnverifiedOutcomeAsSuccess(
        ChangeOutcome outcome,
        string reasonCode,
        RepairResultCode expectedCode)
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);
        var applied = await executor.ExecuteAsync(Item("repair.feedback"), null, CancellationToken.None);
        var request = new RepairRecoveryRequest("repair.feedback", Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt));
        orchestrator.RestoreFactory = restore => RestoreReceipt(restore, outcome, reasonCode);

        var result = await executor.RestoreAsync(request, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Code);
        Assert.NotNull(result.ChangeReceipt);
    }

    [Fact]
    public async Task Restore_CancellationPreservesDurableRequestAndDoesNotWrite()
    {
        var orchestrator = new ScriptedOrchestrator();
        var executor = CreateExecutor(orchestrator);
        var applied = await executor.ExecuteAsync(Item("repair.feedback"), null, CancellationToken.None);
        var request = new RepairRecoveryRequest("repair.feedback", Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await executor.RestoreAsync(request, null, cts.Token);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.Cancelled, result.Code);
        Assert.Equal(request.Receipt.ReceiptId, result.ChangeReceipt?.ReceiptId);
        Assert.Empty(orchestrator.RestoreRequests);
    }

    [Fact]
    public async Task Restore_OrchestratorExceptionKeepsRecoveryRequired()
    {
        var orchestrator = new ScriptedOrchestrator
        {
            RestoreExceptionFactory = _ => new InvalidOperationException("readback unavailable")
        };
        var executor = CreateExecutor(orchestrator);
        var applied = await executor.ExecuteAsync(Item("repair.feedback"), null, CancellationToken.None);
        var request = new RepairRecoveryRequest("repair.feedback", Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt));

        var result = await executor.RestoreAsync(request, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.RecoveryRequired, result.Code);
        Assert.Equal(request.Receipt.ReceiptId, result.ChangeReceipt?.ReceiptId);
        Assert.Single(orchestrator.RestoreRequests);
    }

    [Fact]
    public async Task Restore_PartialFailurePreservesOriginalReceiptAndRecoveryPointers()
    {
        var calls = 0;
        var orchestrator = new ScriptedOrchestrator
        {
            RestoreFactory = restore => ++calls == 1
                ? RestoreReceipt(restore, ChangeOutcome.RestoredVerified, "restore.verified")
                : RestoreReceipt(restore, ChangeOutcome.Blocked, "restore.state-drift")
        };
        var executor = CreateExecutor(orchestrator);
        var applied = await executor.ExecuteAsync(Item("repair.game_mode"), null, CancellationToken.None);
        var request = new RepairRecoveryRequest("repair.game_mode", Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt));

        var result = await executor.RestoreAsync(request, null, CancellationToken.None);

        Assert.Equal(RepairResultCode.RecoveryRequired, result.Code);
        Assert.Equal(request.Receipt.ReceiptId, result.ChangeReceipt?.ReceiptId);
        Assert.Equal(
            request.Receipt.Operations.Select(operation => (operation.ReceiptId, operation.RecoveryId)),
            result.ChangeReceipt?.Operations.Select(operation => (operation.ReceiptId, operation.RecoveryId)));
        Assert.Contains(
            result.ChangeReceipt!.Operations,
            operation => operation.Outcome == ChangeOutcome.RestoredVerified);
    }

    [Fact]
    public async Task Restore_RejectsCoordinatorSuccessWithDifferentRecoveryId()
    {
        var orchestrator = new ScriptedOrchestrator
        {
            RestoreFactory = restore => RestoreReceipt(restore, ChangeOutcome.RestoredVerified, "restore.verified") with
            {
                Operation = RestoreReceipt(restore, ChangeOutcome.RestoredVerified, "restore.verified").Operation with
                {
                    RecoveryId = Guid.NewGuid()
                }
            }
        };
        var executor = CreateExecutor(orchestrator);
        var applied = await executor.ExecuteAsync(Item("repair.feedback"), null, CancellationToken.None);
        var request = new RepairRecoveryRequest("repair.feedback", Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt));

        var result = await executor.RestoreAsync(request, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.RecoveryRequired, result.Code);
        Assert.Equal("restore.result-recovery-mismatch", result.ReasonCode);
    }

    [Fact]
    public async Task UnavailableExecutor_FailsClosedWithoutInvokingLegacyRepair()
    {
        var legacyInvoked = false;
        var item = Item("repair.feedback") with
        {
            RepairAsync = _ =>
            {
                legacyInvoked = true;
                return Task.FromResult(RepairResult.Succeeded("legacy"));
            }
        };

        var result = await UnavailableRepairChangeExecutor.Instance.ExecuteAsync(item, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(legacyInvoked);
    }

    private static OrchestratedRepairChangeExecutor CreateExecutor(ScriptedOrchestrator orchestrator) =>
        new(orchestrator, new AutoConfirmationAuthority(), TimeProvider.System);

    private static RepairItem Item(string id) => new()
    {
        Id = id,
        Name = id,
        Description = id,
        CheckHealthAsync = () => Task.FromResult(false),
        RepairAsync = _ => Task.FromResult(RepairResult.Failed("Legacy path must not run."))
    };

    private static PreparationResult Blocked(ChangeRequest request, string code, string message) =>
        new(ChangePreparationStatus.Blocked, null, code, message, null);

    private static BatchReceipt Receipt(
        PreparedChangePlan plan,
        ChangeOutcome outcome,
        string reasonCode,
        bool mutationOccurred,
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
            reasonCode,
            reasonCode,
            mutationOccurred,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

    private static BatchReceipt RestoreReceipt(RestoreRequest request, ChangeOutcome outcome, string reasonCode) =>
        new(new OperationReceipt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            request.RecoveryId,
            "restore",
            TargetScope.CurrentUser("test"),
            outcome,
            "before",
            "target",
            outcome == ChangeOutcome.RestoredVerified ? "before" : null,
            reasonCode,
            reasonCode,
            outcome == ChangeOutcome.RestoredVerified,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

    private sealed class AutoConfirmationAuthority : IChangeConfirmationAuthority
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
                explicitlyConfirmed: true));

        public bool TryConsume(PreparedChangePlan plan, ConfirmationToken token) => true;
    }

    private sealed class ScriptedOrchestrator : IChangeOrchestrator
    {
        public Func<ChangeRequest, PreparationResult>? PreparationFactory { get; init; }
        public Func<PreparedChangePlan, BatchReceipt>? ExecuteFactory { get; init; }
        public Func<RestoreRequest, BatchReceipt>? RestoreFactory { get; set; }
        public Func<RestoreRequest, Exception?>? RestoreExceptionFactory { get; init; }
        public List<string> PreparedCapabilityIds { get; } = [];
        public List<ChangeRequest> PreparedRequests { get; } = [];
        public List<string> ExecutedCapabilityIds { get; } = [];
        public List<RestoreRequest> RestoreRequests { get; } = [];

        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<StateObservation?>(null);

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreparedCapabilityIds.Add(request.CapabilityId);
            PreparedRequests.Add(request);
            if (PreparationFactory is not null)
            {
                return Task.FromResult(PreparationFactory(request));
            }

            var now = DateTimeOffset.UtcNow;
            var change = new PreparedCapabilityChange(
                request.CapabilityId,
                "1.0.0",
                request.Scope,
                "before",
                $"before:{request.CapabilityId}",
                "target",
                $"target:{request.CapabilityId}",
                $"resource:{request.CapabilityId}");
            var plan = new PreparedChangePlan(
                Guid.NewGuid(),
                change,
                Guid.NewGuid().ToString("N"),
                now,
                now.AddMinutes(5),
                false,
                true,
                true,
                ChangeRisk.LowReversible);
            return Task.FromResult(new PreparationResult(ChangePreparationStatus.Ready, plan, "ready", "ready", null));
        }

        public Task<BatchReceipt> ExecuteAsync(
            PreparedChangePlan plan,
            ConfirmationToken confirmation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutedCapabilityIds.Add(plan.Change.CapabilityId);
            return Task.FromResult(ExecuteFactory?.Invoke(plan)
                ?? Receipt(plan, ChangeOutcome.SucceededVerified, "execution.verified", true, Guid.NewGuid()));
        }

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken)
        {
            RestoreRequests.Add(request);
            if (RestoreExceptionFactory?.Invoke(request) is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(RestoreFactory?.Invoke(request)
                ?? RestoreReceipt(request, ChangeOutcome.RestoredVerified, "restore.verified"));
        }
    }
}
