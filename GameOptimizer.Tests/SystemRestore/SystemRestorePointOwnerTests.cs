using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Infrastructure.Windows.SystemRestore;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Services.Baseline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.SystemRestore;

public sealed class SystemRestorePointOwnerTests
{
    [Theory]
    [InlineData(false, SystemProtectionState.Enabled, SystemRestorePointWorkflowStatus.PermissionDenied)]
    [InlineData(true, SystemProtectionState.Disabled, SystemRestorePointWorkflowStatus.ProtectionDisabled)]
    [InlineData(true, SystemProtectionState.PolicyDisabled, SystemRestorePointWorkflowStatus.PolicyDenied)]
    [InlineData(true, SystemProtectionState.Unknown, SystemRestorePointWorkflowStatus.Unknown)]
    public async Task CreatePreflightFailsClosedForPermissionAndProtection(
        bool administrator,
        SystemProtectionState protection,
        SystemRestorePointWorkflowStatus expected)
    {
        var fixture = new Fixture();
        fixture.Platform.Current = fixture.Probe(administrator: administrator, protection: protection);

        var result = await fixture.Owner.PrepareCreateAsync(new("baseline", "test"));

        Assert.False(result.Success);
        Assert.Equal(expected, result.Status);
        Assert.Equal(0, fixture.Platform.CreateCalls);
    }

    [Fact]
    public async Task AdministratorWithVerifiedProtectionCanPrepareButCooldownBlocks()
    {
        var fixture = new Fixture();
        var ready = await fixture.Owner.PrepareCreateAsync(new("ready", "test"));
        Assert.True(ready.Success);
        Assert.NotNull(ready.CreatePlan);

        fixture.Platform.Current = fixture.Probe(nextAllowed: fixture.Clock.GetUtcNow().AddMinutes(20));
        var blocked = await fixture.Owner.PrepareCreateAsync(new("cooldown", "test"));
        Assert.False(blocked.Success);
        Assert.Equal(SystemRestorePointWorkflowStatus.CooldownActive, blocked.Status);
    }

    [Fact]
    public async Task NativeDescriptionLimitIsEnforcedBeforePersistence()
    {
        var fixture = new Fixture();

        var accepted = await fixture.Owner.PrepareCreateAsync(new(new string('A', 255), "test"));
        var rejected = await fixture.Owner.PrepareCreateAsync(new(new string('B', 256), "test"));

        Assert.True(accepted.Success);
        Assert.False(rejected.Success);
        Assert.Equal(SystemRestorePointWorkflowStatus.InvalidRequest, rejected.Status);
        Assert.Equal(0, fixture.Platform.CreateCalls);
    }

    [Fact]
    public async Task VerifiedCreationRequiresOneNewExactIdentityAndWritesReceipt()
    {
        var fixture = new Fixture();
        var prepared = await fixture.PrepareAsync("new exact identity", baselineId: "BL-1");
        var token = fixture.Confirm(prepared);

        var result = await fixture.Owner.ExecuteCreateAsync(prepared, token);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(SystemRestorePointWorkflowStatus.Succeeded, result.Status);
        Assert.True(result.Receipt?.ReadBackVerified);
        Assert.Equal("BL-1", result.Receipt?.BaselineId);
        Assert.NotNull(result.Receipt?.CreatedIdentity);
        Assert.DoesNotContain(
            typeof(SystemRestorePointReceipt).GetProperties(),
            property => property.Name.Contains("RecoveryId", StringComparison.Ordinal));
        Assert.Equal(1, fixture.Platform.CreateCalls);
        Assert.Equal(SystemRestorePointReceiptState.Verified, fixture.Store.Receipts.Single().State);
    }

    [Fact]
    public async Task OldPointCannotImpersonateTheCurrentCreation()
    {
        var fixture = new Fixture();
        var old = fixture.Platform.Current.RestorePoints.Single();
        fixture.Platform.OnCreate = (_, _) => Task.FromResult(new SystemRestorePointMutationResult(
            SystemRestorePointMutationOutcome.Succeeded,
            old.SequenceNumber,
            0,
            "Windows reported success but reused an old identity.",
            SystemRestorePointMutationState.Changed));
        var prepared = await fixture.PrepareAsync("must be new");

        var result = await fixture.Owner.ExecuteCreateAsync(prepared, fixture.Confirm(prepared));

        Assert.False(result.Success);
        Assert.Null(result.Changed);
        Assert.Equal(SystemRestorePointWorkflowStatus.ReadBackMismatch, result.Status);
        Assert.True(result.ManualInterventionRequired);
        Assert.Null(result.Receipt?.CreatedIdentity);
    }

    [Fact]
    public async Task PlatformFailureNeverPassesCreationOrBaseline()
    {
        var fixture = new Fixture();
        fixture.Platform.OnCreate = (_, _) => Task.FromResult(new SystemRestorePointMutationResult(
            SystemRestorePointMutationOutcome.Failed,
            null,
            5,
            "access denied",
            SystemRestorePointMutationState.NotChanged));
        var prepared = await fixture.PrepareAsync("failure");

        var result = await fixture.Owner.ExecuteCreateAsync(prepared, fixture.Confirm(prepared));

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Equal(SystemRestorePointWorkflowStatus.PlatformFailure, result.Status);
        Assert.False(Assert.IsType<SystemRestorePointReceipt>(result.Receipt).ReadBackVerified);
        Assert.Equal(SystemRestorePointMutationState.NotChanged, result.Receipt.MutationState);
    }

    [Fact]
    public async Task BaselineGuideRejectsCreateFailureAndAReusedOldPoint()
    {
        var oldPoint = new RestorePointInfo
        {
            SequenceNumber = 7,
            Description = "old",
            CreationTime = DateTime.Now.AddMinutes(-10)
        };
        var failedService = new FakeLegacyRestoreService(false, [oldPoint]);
        var failedGuide = new BaselineManualGuideService(
            failedService,
            NullLogger<BaselineManualGuideService>.Instance);

        var failed = await failedGuide.RecheckRestorePointAsync("BL-FAILED", DateTime.UtcNow);

        Assert.False(failed.Success);
        Assert.Equal(1, failedService.ReadCalls);

        var reusedService = new FakeLegacyRestoreService(true, [oldPoint], [oldPoint]);
        var reusedGuide = new BaselineManualGuideService(
            reusedService,
            NullLogger<BaselineManualGuideService>.Instance);

        var reused = await reusedGuide.RecheckRestorePointAsync("BL-REUSED", DateTime.UtcNow);

        Assert.False(reused.Success);
        Assert.Equal(2, reusedService.ReadCalls);
    }

    [Fact]
    public async Task ReceiptFailureBeforeWriteBlocksAndTerminalFailureRequiresManualIntervention()
    {
        var beforeFixture = new Fixture();
        beforeFixture.Store.FailOnReceiptSaveNumber = 1;
        var beforePlan = await beforeFixture.PrepareAsync("receipt before");
        var before = await beforeFixture.Owner.ExecuteCreateAsync(beforePlan, beforeFixture.Confirm(beforePlan));
        Assert.Equal(SystemRestorePointWorkflowStatus.StoreFailure, before.Status);
        Assert.Equal(0, beforeFixture.Platform.CreateCalls);
        var beforeClaim = Assert.Single(beforeFixture.Store.Claims);
        Assert.Equal(SystemRestorePointCreateClaimState.Failed, beforeClaim.State);
        Assert.Equal(SystemRestorePointMutationState.NotChanged, beforeClaim.MutationState);

        var terminalFixture = new Fixture();
        terminalFixture.Store.FailOnReceiptSaveNumber = 2;
        var terminalPlan = await terminalFixture.PrepareAsync("receipt terminal");
        var terminal = await terminalFixture.Owner.ExecuteCreateAsync(terminalPlan, terminalFixture.Confirm(terminalPlan));
        Assert.Equal(SystemRestorePointWorkflowStatus.ManualInterventionRequired, terminal.Status);
        Assert.True(terminal.Changed);
        Assert.True(terminal.ManualInterventionRequired);
        Assert.Equal(1, terminalFixture.Platform.CreateCalls);
        var terminalClaim = Assert.Single(terminalFixture.Store.Claims);
        Assert.Equal(SystemRestorePointCreateClaimState.Unknown, terminalClaim.State);
        Assert.Equal(SystemRestorePointMutationState.Changed, terminalClaim.MutationState);
    }

    [Fact]
    public async Task ClaimTerminalPersistenceFailureNeverReturnsSuccess()
    {
        var fixture = new Fixture();
        fixture.Store.FailOnClaimSaveNumber = 1;
        var plan = await fixture.PrepareAsync("claim terminal fault");

        var result = await fixture.Owner.ExecuteCreateAsync(plan, fixture.Confirm(plan));

        Assert.False(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(SystemRestorePointWorkflowStatus.ManualInterventionRequired, result.Status);
        Assert.True(result.ManualInterventionRequired);
        Assert.Equal(SystemRestorePointCreateClaimState.Executing, Assert.Single(fixture.Store.Claims).State);
        Assert.Equal(1, fixture.Platform.CreateCalls);
    }

    [Fact]
    public async Task IdentityDriftAfterPreparationBlocksBeforeWriter()
    {
        var fixture = new Fixture();
        var prepared = await fixture.PrepareAsync("drift");
        fixture.Platform.AddPoint(new(
            fixture.Volume,
            99,
            fixture.Clock.GetUtcNow(),
            "external point",
            12));

        var result = await fixture.Owner.ExecuteCreateAsync(prepared, fixture.Confirm(prepared));

        Assert.False(result.Success);
        Assert.Equal(SystemRestorePointWorkflowStatus.IdentityDrifted, result.Status);
        Assert.Equal(0, fixture.Platform.CreateCalls);
        Assert.Empty(fixture.Store.Claims);
    }

    [Fact]
    public async Task LeaseTimeoutDoesNotBurnThePlanClaimOrReachWriter()
    {
        var fixture = new Fixture();
        fixture.Store.LeaseError = new TimeoutException("simulated lease timeout");
        var prepared = await fixture.PrepareAsync("lease timeout");

        var result = await fixture.Owner.ExecuteCreateAsync(prepared, fixture.Confirm(prepared));

        Assert.Equal(SystemRestorePointWorkflowStatus.TimedOut, result.Status);
        Assert.False(result.Changed);
        Assert.Empty(fixture.Store.Claims);
        Assert.Equal(0, fixture.Platform.CreateCalls);
    }

    [Fact]
    public async Task NonCancellableInProcessWriterIsBlockedBeforePlanPersistence()
    {
        var fixture = new Fixture();
        fixture.Platform.Current = fixture.Probe() with
        {
            CreationBoundary = SystemRestorePointCreationBoundary.InProcessNonCancellable
        };

        var result = await fixture.Owner.PrepareCreateAsync(new("unsafe native boundary", "test"));

        Assert.False(result.Success);
        Assert.Equal(SystemRestorePointWorkflowStatus.PolicyDenied, result.Status);
        Assert.Equal(0, fixture.Platform.CreateCalls);
        Assert.Empty(fixture.Store.Claims);
    }

    [Fact]
    public async Task TamperedExpiredAndReplayConfirmationsNeverWriteTwice()
    {
        var fixture = new Fixture();
        var prepared = await fixture.PrepareAsync("confirmation");
        var badEvidence = new SystemRestorePointConfirmationEvidence(
            prepared.PlanId,
            new string('A', 64),
            fixture.Clock.GetUtcNow(),
            true);
        Assert.Null(fixture.Owner.ConfirmCreate(prepared, badEvidence));

        var token = fixture.Confirm(prepared);
        var tampered = token with { Signature = new string('A', 64) };
        var tamperedResult = await fixture.Owner.ExecuteCreateAsync(prepared, tampered);
        Assert.Equal(SystemRestorePointWorkflowStatus.ConfirmationRequired, tamperedResult.Status);
        Assert.Equal(0, fixture.Platform.CreateCalls);

        var expiring = fixture.Confirm(prepared);
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        var expiredResult = await fixture.Owner.ExecuteCreateAsync(prepared, expiring);
        Assert.Equal(SystemRestorePointWorkflowStatus.ConfirmationRequired, expiredResult.Status);
        Assert.Equal(0, fixture.Platform.CreateCalls);

        fixture.Clock.Advance(TimeSpan.FromMinutes(-3));
        var firstToken = fixture.Confirm(prepared);
        var first = await fixture.Owner.ExecuteCreateAsync(prepared, firstToken);
        Assert.True(first.Success);
        var secondToken = fixture.Confirm(prepared);
        var replay = await fixture.Owner.ExecuteCreateAsync(prepared, secondToken);
        Assert.Equal(SystemRestorePointWorkflowStatus.ReplayBlocked, replay.Status);
        Assert.Equal(1, fixture.Platform.CreateCalls);
    }

    [Fact]
    public async Task CancellationBeforeWriteAndInternalTimeoutAreDistinctAndFailClosed()
    {
        var cancelledFixture = new Fixture();
        var cancelledPlan = await cancelledFixture.PrepareAsync("cancel");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledResult = await cancelledFixture.Owner.ExecuteCreateAsync(
            cancelledPlan,
            cancelledFixture.Confirm(cancelledPlan),
            cancelled.Token);
        Assert.Equal(SystemRestorePointWorkflowStatus.Cancelled, cancelledResult.Status);
        Assert.Equal(0, cancelledFixture.Platform.CreateCalls);

        var timeoutFixture = new Fixture(new SystemRestorePointTimingOptions(
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromSeconds(1)));
        timeoutFixture.Platform.OnCreate = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new SystemRestorePointMutationResult(
                SystemRestorePointMutationOutcome.Succeeded,
                2,
                0,
                MutationState: SystemRestorePointMutationState.Changed);
        };
        var timeoutPlan = await timeoutFixture.PrepareAsync("timeout");
        var timeoutResult = await timeoutFixture.Owner.ExecuteCreateAsync(
            timeoutPlan,
            timeoutFixture.Confirm(timeoutPlan));
        Assert.Equal(SystemRestorePointWorkflowStatus.Unknown, timeoutResult.Status);
        Assert.False(timeoutResult.Success);
        Assert.Null(timeoutResult.Changed);
        Assert.Equal(SystemRestorePointCreateClaimState.Unknown, Assert.Single(timeoutFixture.Store.Claims).State);
    }

    [Fact]
    public async Task CallerCancellationAfterWriterStartsIsRecordedWithoutForgingCancelledOutcome()
    {
        var fixture = new Fixture();
        using var callerCancellation = new CancellationTokenSource();
        fixture.Platform.OnCreate = (command, _) =>
        {
            callerCancellation.Cancel();
            var sequence = fixture.Platform.Current.RestorePoints.Max(point => point.SequenceNumber) + 1;
            fixture.Platform.AddPoint(new(
                command.VolumeIdentity,
                sequence,
                fixture.Clock.GetUtcNow(),
                command.Description,
                12));
            return Task.FromResult(new SystemRestorePointMutationResult(
                SystemRestorePointMutationOutcome.Succeeded,
                sequence,
                0,
                MutationState: SystemRestorePointMutationState.Changed));
        };
        var plan = await fixture.PrepareAsync("late caller cancellation");

        var result = await fixture.Owner.ExecuteCreateAsync(
            plan,
            fixture.Confirm(plan),
            callerCancellation.Token);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(SystemRestorePointWorkflowStatus.Succeeded, result.Status);
        Assert.True(result.Receipt?.CallerCancellationRequested);
        Assert.Equal(SystemRestorePointReceiptState.Verified, result.Receipt?.State);
    }

    [Fact]
    public async Task SystemRestoreExecutionIsPermanentlyBlockedWithHonestRebootSemantics()
    {
        var fixture = new Fixture();
        var target = fixture.Platform.Current.RestorePoints.Single();

        var prepared = await fixture.Owner.PrepareSystemRestoreAsync(target, "test");
        Assert.False(prepared.Success);
        Assert.Equal(SystemRestorePointWorkflowStatus.PolicyDenied, prepared.Status);
        Assert.True(prepared.ManualInterventionRequired);
        Assert.True(prepared.RequiresReboot);
        Assert.NotNull(prepared.RestorePlan);
        Assert.False(prepared.RestorePlan!.ProductionExecutionAllowed);

        var executed = await fixture.Owner.ExecuteSystemRestoreAsync(prepared.RestorePlan, null);
        Assert.False(executed.Success);
        Assert.Equal(SystemRestorePointWorkflowStatus.PolicyDenied, executed.Status);
        Assert.True(executed.ManualInterventionRequired);
        Assert.True(executed.RequiresReboot);
        Assert.Null(executed.Receipt);
        Assert.Equal(0, fixture.Platform.CreateCalls);
    }

    [Fact]
    public async Task PendingRestartAndMissingTargetBlockRestorePreflight()
    {
        var fixture = new Fixture();
        var target = fixture.Platform.Current.RestorePoints.Single();
        fixture.Platform.Current = fixture.Probe(pendingRestart: true);
        var reboot = await fixture.Owner.PrepareSystemRestoreAsync(target, "test");
        Assert.Equal(SystemRestorePointWorkflowStatus.RebootRequired, reboot.Status);

        fixture.Platform.Current = fixture.Probe(points: Array.Empty<SystemRestorePointIdentity>());
        var missing = await fixture.Owner.PrepareSystemRestoreAsync(target, "test");
        Assert.Equal(SystemRestorePointWorkflowStatus.IdentityDrifted, missing.Status);
    }

    [Fact]
    public void ProductionCompositionHasOneOwnerAndNoRestoreWriterPort()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSystemRestorePointWindowsInfrastructure(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISystemRestorePointOwner));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISystemRestorePointStartupReconciliation));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISystemRestorePointPlatform));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISystemRestorePointWriterProcessRunner));
        Assert.Same(
            provider.GetRequiredService<ISystemRestorePointOwner>(),
            provider.GetRequiredService<ISystemRestorePointOwner>());
        Assert.Same(
            provider.GetRequiredService<ISystemRestorePointStartupReconciliation>(),
            provider.GetRequiredService<ISystemRestorePointStartupReconciliation>());
        Assert.DoesNotContain(
            typeof(ISystemRestorePointPlatform).GetMethods(),
            method => method.Name.Contains("Restore", StringComparison.Ordinal)
                && !method.Name.Contains("RestorePoint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProductionPlatformCreatePortDelegatesOnlyToIsolatedWriterBoundary()
    {
        var writer = new FakeWriterRunner(new(
            SystemRestorePointMutationOutcome.Succeeded,
            42,
            0,
            "fake writer",
            SystemRestorePointMutationState.Changed));
        var platform = new WindowsSystemRestorePointPlatform(writer);
        var result = await platform.CreateRestorePointAsync(
            new SystemRestorePointCreateCommand(
                Guid.NewGuid(),
                new string('A', 64),
                @"\\?\Volume{11111111-1111-1111-1111-111111111111}\",
                "must not write"),
            default);

        Assert.Equal(SystemRestorePointMutationOutcome.Succeeded, result.Outcome);
        Assert.Equal(SystemRestorePointMutationState.Changed, result.MutationState);
        Assert.Equal(42, result.SequenceNumber);
        Assert.Equal(1, writer.Calls);
    }

    [Fact]
    public async Task ReconcileInterruptedCreate_VerifiesOnlyUniqueAuthoritativeReadBack()
    {
        var fixture = new Fixture();
        var (plan, claim) = await SeedExecutingClaimAsync(fixture, includeReceipt: true);
        var created = new SystemRestorePointIdentity(
            plan.VolumeIdentity,
            22,
            fixture.Clock.GetUtcNow().AddMinutes(1),
            plan.Description,
            12);
        fixture.Platform.AddPoint(created);

        var result = await fixture.Owner.ReconcileInterruptedCreateAsync(plan.PlanId);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        Assert.Equal(SystemRestorePointCreateClaimState.Verified, Assert.Single(fixture.Store.Claims).State);
        var receipt = Assert.Single(fixture.Store.Receipts);
        Assert.Equal(SystemRestorePointReceiptState.Verified, receipt.State);
        Assert.Equal(claim.ClaimId, receipt.ClaimId);
        Assert.Equal(created, receipt.CreatedIdentity);
        Assert.Equal(0, fixture.Platform.CreateCalls);
    }

    [Fact]
    public async Task ReconcileInterruptedCreate_AbsenceNeverBecomesNotChanged()
    {
        var fixture = new Fixture();
        var (plan, _) = await SeedExecutingClaimAsync(fixture, includeReceipt: true);

        var result = await fixture.Owner.ReconcileInterruptedCreateAsync(plan.PlanId);

        Assert.False(result.Success);
        Assert.Null(result.Changed);
        Assert.True(result.ManualInterventionRequired);
        Assert.Equal(SystemRestorePointCreateClaimState.Interrupted, Assert.Single(fixture.Store.Claims).State);
        Assert.Equal(SystemRestorePointMutationState.Unknown, Assert.Single(fixture.Store.Claims).MutationState);
        Assert.Equal(SystemRestorePointReceiptState.Unknown, Assert.Single(fixture.Store.Receipts).State);
    }

    [Fact]
    public async Task ReconcileInterruptedCreate_RecoversMissingAndCorruptReceiptAsUnknown()
    {
        var missingFixture = new Fixture();
        var (missingPlan, _) = await SeedExecutingClaimAsync(missingFixture, includeReceipt: false);

        var missing = await missingFixture.Owner.ReconcileInterruptedCreateAsync(missingPlan.PlanId);

        Assert.Null(missing.Changed);
        Assert.Equal(SystemRestorePointReceiptState.Unknown, Assert.Single(missingFixture.Store.Receipts).State);
        Assert.Equal(SystemRestorePointReceiptRecoveryDisposition.CreatedMissing, missingFixture.Store.LastRecoveryDisposition);

        var corruptFixture = new Fixture();
        var (corruptPlan, _) = await SeedExecutingClaimAsync(corruptFixture, includeReceipt: true);
        corruptFixture.Store.ReceiptIsCorrupt = true;

        var corrupt = await corruptFixture.Owner.ReconcileInterruptedCreateAsync(corruptPlan.PlanId);

        Assert.Null(corrupt.Changed);
        Assert.Equal(SystemRestorePointReceiptRecoveryDisposition.ReplacedCorrupt, corruptFixture.Store.LastRecoveryDisposition);
        Assert.Equal(SystemRestorePointCreateClaimState.Interrupted, Assert.Single(corruptFixture.Store.Claims).State);
    }

    [Fact]
    public async Task ReconcileInterruptedCreate_ReceiptRecoveryFailureLeavesClaimExecuting()
    {
        var fixture = new Fixture();
        var (plan, _) = await SeedExecutingClaimAsync(fixture, includeReceipt: false);
        fixture.Store.RecoverReceiptError = new IOException("simulated recovery write failure");

        var result = await fixture.Owner.ReconcileInterruptedCreateAsync(plan.PlanId);

        Assert.Equal(SystemRestorePointWorkflowStatus.StoreFailure, result.Status);
        Assert.Null(result.Changed);
        Assert.True(result.ManualInterventionRequired);
        Assert.Equal(SystemRestorePointCreateClaimState.Executing, Assert.Single(fixture.Store.Claims).State);
    }

    [Fact]
    public async Task ResolveInterruptedCreate_RequiresExactTypedIdentityAndEvidence()
    {
        var fixture = new Fixture();
        var (plan, claim) = await SeedExecutingClaimAsync(fixture, includeReceipt: true);
        _ = await fixture.Owner.ReconcileInterruptedCreateAsync(plan.PlanId);
        var created = new SystemRestorePointIdentity(
            plan.VolumeIdentity,
            23,
            fixture.Clock.GetUtcNow().AddMinutes(1),
            plan.Description,
            12);
        fixture.Platform.AddPoint(created);

        var rejected = await fixture.Owner.ResolveInterruptedCreateAsync(new(
            plan.PlanId,
            claim.ClaimId,
            SystemRestorePointManualResolutionDisposition.ConfirmVerifiedChanged,
            "operator compared the typed identity",
            created with { SequenceNumber = 24 }));
        var verified = await fixture.Owner.ResolveInterruptedCreateAsync(new(
            plan.PlanId,
            claim.ClaimId,
            SystemRestorePointManualResolutionDisposition.ConfirmVerifiedChanged,
            "operator compared the typed identity",
            created));

        Assert.Equal(SystemRestorePointWorkflowStatus.ReadBackMismatch, rejected.Status);
        Assert.Null(rejected.Changed);
        Assert.True(verified.Success, verified.Message);
        Assert.True(verified.Changed);
        Assert.Equal(SystemRestorePointCreateClaimState.Verified, Assert.Single(fixture.Store.Claims).State);
    }

    [Fact]
    public async Task ReconcileInterruptedCreates_ClaimSetAuthenticationFailureFailsClosed()
    {
        var fixture = new Fixture();
        fixture.Store.ListClaimsError = new InvalidDataException("simulated claim corruption");

        var result = Assert.Single(await fixture.Owner.ReconcileInterruptedCreatesAsync());

        Assert.Equal(SystemRestorePointWorkflowStatus.StoreFailure, result.Status);
        Assert.Null(result.Changed);
        Assert.True(result.ManualInterventionRequired);
    }

    [Fact]
    public void ShellAndBaselineContainNoLegacyWriterOrOldPointFallback()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Services", "SystemRestoreService.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "ViewModels", "RestorePointViewModel.cs"));
        var baseline = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Services", "Baseline", "BaselineManualGuideService.cs"));
        var platformContract = File.ReadAllText(Path.Combine(root, "GameOptimizer.Core", "SystemRestore", "SystemRestorePointContracts.cs"));
        var windowsPlatform = File.ReadAllText(Path.Combine(root, "GameOptimizer.Infrastructure.Windows", "SystemRestore", "WindowsSystemRestorePointPlatform.cs"));

        Assert.Contains("ISystemRestorePointOwner", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("ISystemRestoreService", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("powershell.exe", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ManagementClass", service, StringComparison.Ordinal);
        Assert.DoesNotContain("rstrui.exe", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EnsureFrequencyLimitBypassed", service, StringComparison.Ordinal);
        var platformMembers = platformContract.Split("interface ISystemRestorePointPlatform", StringSplitOptions.None)[1]
            .Split('}')[0];
        Assert.DoesNotContain("ExecuteSystemRestore", platformMembers, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreAsync", platformMembers, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateWithTypedApi", windowsPlatform, StringComparison.Ordinal);
        Assert.DoesNotContain("SrSetRestorePointDelegate", windowsPlatform, StringComparison.Ordinal);
        Assert.DoesNotContain("已按可用处理", baseline, StringComparison.Ordinal);
        Assert.Contains("!identitiesBeforeCreate.Contains", baseline, StringComparison.Ordinal);
        Assert.Contains("string.Equals(point.Description, marker, StringComparison.Ordinal)", baseline, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "GameOptimizer.Shell", "Services", "IRestorePointService.cs")));
        Assert.False(File.Exists(Path.Combine(root, "GameOptimizer.Shell", "Services", "RestorePointService.cs")));
        Assert.False(File.Exists(Path.Combine(root, "GameOptimizer.Core", "Services", "Safety", "ISafetyService.cs")));
        Assert.False(File.Exists(Path.Combine(root, "GameOptimizer.Core", "Services", "Safety", "SafetyService.cs")));
    }

    private static async Task<(SystemRestorePointCreatePlan Plan, SystemRestorePointCreateClaim Claim)> SeedExecutingClaimAsync(
        Fixture fixture,
        bool includeReceipt)
    {
        var plan = await fixture.PrepareAsync("P1 restart reconciliation");
        var now = fixture.Clock.GetUtcNow();
        var claim = new SystemRestorePointCreateClaim(
            Guid.NewGuid(),
            plan.PlanId,
            plan.PlanHash,
            Guid.NewGuid(),
            now,
            now,
            SystemRestorePointCreateClaimState.Executing,
            SystemRestorePointMutationState.Unknown);
        Assert.NotNull(await fixture.Store.TryClaimCreatePlanAsync(claim, default));
        if (includeReceipt)
        {
            await fixture.Store.SaveReceiptAsync(new(
                claim.ReceiptId,
                plan.PlanId,
                plan.PlanHash,
                now,
                null,
                plan.VolumeIdentity,
                plan.Description,
                plan.Source,
                plan.BaselineId,
                SystemRestorePointReceiptState.Executing,
                SystemRestorePointMutationOutcome.Unknown,
                null,
                false,
                false,
                MutationState: SystemRestorePointMutationState.Unknown,
                ClaimId: claim.ClaimId), default);
        }
        return (plan, claim);
    }

    private sealed class Fixture
    {
        public Fixture(SystemRestorePointTimingOptions? timing = null)
        {
            Clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.Zero));
            Platform = new FakePlatform();
            Platform.Current = Probe();
            Store = new MemoryStore();
            Confirmation = new SystemRestorePointConfirmationAuthority(Enumerable.Repeat((byte)0x4A, 32).ToArray());
            Owner = new SystemRestorePointOwner(Platform, Store, Confirmation, Clock, timing);
        }

        public string Volume { get; } = @"\\?\Volume{11111111-1111-1111-1111-111111111111}\";
        public ManualTimeProvider Clock { get; }
        public FakePlatform Platform { get; }
        public MemoryStore Store { get; }
        public ISystemRestorePointConfirmationAuthority Confirmation { get; }
        public SystemRestorePointOwner Owner { get; }

        public SystemRestorePointProbe Probe(
            bool administrator = true,
            SystemProtectionState protection = SystemProtectionState.Enabled,
            DateTimeOffset? nextAllowed = null,
            bool pendingRestart = false,
            IReadOnlyList<SystemRestorePointIdentity>? points = null) => new(
                true,
                22631,
                administrator,
                protection,
                Volume,
                points ?? new[]
                {
                    new SystemRestorePointIdentity(
                        Volume,
                        1,
                        Clock.GetUtcNow().AddDays(-2),
                        "old point",
                        12)
                },
                TimeSpan.FromHours(24),
                nextAllowed,
                IsCreationApiAvailable: true,
                HasPendingRestart: pendingRestart,
                IsRestoreExecutionPolicyAllowed: false,
                CreationBoundary: SystemRestorePointCreationBoundary.IsolatedAndTerminable);

        public async Task<SystemRestorePointCreatePlan> PrepareAsync(string description, string? baselineId = null)
        {
            var result = await Owner.PrepareCreateAsync(new(description, "test", baselineId));
            Assert.True(result.Success, result.Message);
            return Assert.IsType<SystemRestorePointCreatePlan>(result.CreatePlan);
        }

        public SystemRestorePointConfirmationToken Confirm(SystemRestorePointCreatePlan plan)
        {
            var token = Owner.ConfirmCreate(plan, new(
                plan.PlanId,
                plan.PlanHash,
                Clock.GetUtcNow(),
                true));
            return Assert.IsType<SystemRestorePointConfirmationToken>(token);
        }
    }

    private sealed class FakeLegacyRestoreService(
        bool createResult,
        params List<RestorePointInfo>[] reads) : ISystemRestoreService
    {
        private readonly Queue<List<RestorePointInfo>> _reads = new(reads);

        public int ReadCalls { get; private set; }
        public string LastError => createResult ? string.Empty : "create failed";

        public Task<bool> CreateRestorePointAsync(string description, CancellationToken ct = default) =>
            Task.FromResult(createResult);

        public Task<List<RestorePointInfo>> GetAllRestorePointsAsync(CancellationToken ct = default)
        {
            ReadCalls++;
            return Task.FromResult(_reads.Count > 0 ? _reads.Dequeue() : new List<RestorePointInfo>());
        }

        public Task<bool> DeleteRestorePointAsync(int sequenceNumber, CancellationToken ct = default) =>
            Task.FromResult(false);

        public bool IsSystemRestoreEnabled() => false;
        public Task<SystemRestoreStatus> GetSystemRestoreStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new SystemRestoreStatus());
        public RestorePointInfo? GetLatestRestorePoint() => null;
        public void OpenSystemRestoreWizard() { }
        public void OpenSystemRestoreSettings() { }
        public Task<bool> EnableSystemRestoreAsync(CancellationToken ct = default) => Task.FromResult(false);
        public (bool CanCreate, string Reason) CanCreateRestorePoint() => (createResult, string.Empty);
    }

    private sealed class FakePlatform : ISystemRestorePointPlatform
    {
        public SystemRestorePointProbe Current { get; set; } = SystemRestorePointProbe.Unsupported("not initialized");
        public int CreateCalls { get; private set; }
        public Func<SystemRestorePointCreateCommand, CancellationToken, Task<SystemRestorePointMutationResult>>? OnCreate { get; set; }

        public Task<SystemRestorePointProbe> ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Current);
        }

        public async Task<SystemRestorePointMutationResult> CreateRestorePointAsync(
            SystemRestorePointCreateCommand command,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            if (OnCreate is not null)
            {
                return await OnCreate(command, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var sequence = Current.RestorePoints.Select(point => point.SequenceNumber).DefaultIfEmpty().Max() + 1;
            var createdAt = Current.RestorePoints.Count == 0
                ? DateTimeOffset.UtcNow
                : Current.RestorePoints.Max(point => point.CreatedAtUtc).AddDays(2).AddMinutes(1);
            AddPoint(new(
                command.VolumeIdentity,
                sequence,
                createdAt,
                command.Description,
                12));
            return new(
                SystemRestorePointMutationOutcome.Succeeded,
                sequence,
                0,
                MutationState: SystemRestorePointMutationState.Changed);
        }

        public void AddPoint(SystemRestorePointIdentity point) => Current = Current with
        {
            RestorePoints = Current.RestorePoints.Concat(new[] { point }).ToArray()
        };
    }

    private sealed class FakeWriterRunner(SystemRestorePointMutationResult result)
        : ISystemRestorePointWriterProcessRunner
    {
        public bool IsAvailable => true;
        public int Calls { get; private set; }

        public Task<SystemRestorePointMutationResult> RunAsync(
            SystemRestorePointCreateCommand command,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class MemoryStore : ISystemRestorePointWorkflowStore
    {
        private readonly Dictionary<Guid, SystemRestorePointCreatePlan> _createPlans = new();
        private readonly Dictionary<Guid, SystemRestoreExecutionPlan> _restorePlans = new();
        private readonly Dictionary<Guid, SystemRestorePointReceipt> _receipts = new();
        private readonly Dictionary<string, SystemRestorePointCreateClaim> _claims = new(StringComparer.Ordinal);
        private int _receiptSaveCount;
        private int _claimSaveCount;

        public IReadOnlyCollection<SystemRestorePointReceipt> Receipts => _receipts.Values;
        public IReadOnlyCollection<SystemRestorePointCreateClaim> Claims => _claims.Values;
        public int? FailOnReceiptSaveNumber { get; set; }
        public int? FailOnClaimSaveNumber { get; set; }
        public Exception? LeaseError { get; set; }
        public Exception? ListClaimsError { get; set; }
        public Exception? RecoverReceiptError { get; set; }
        public bool ReceiptIsCorrupt { get; set; }
        public SystemRestorePointReceiptRecoveryDisposition? LastRecoveryDisposition { get; private set; }

        public Task SaveCreatePlanAsync(SystemRestorePointCreatePlan plan, CancellationToken cancellationToken)
        {
            _createPlans[plan.PlanId] = plan;
            return Task.CompletedTask;
        }

        public Task<SystemRestorePointCreatePlan?> LoadCreatePlanAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(_createPlans.GetValueOrDefault(planId));

        public Task<SystemRestorePointCreateClaim?> TryClaimCreatePlanAsync(
            SystemRestorePointCreateClaim claim,
            CancellationToken cancellationToken)
        {
            var key = $"{claim.PlanId:D}|{claim.PlanHash}";
            if (!_claims.TryAdd(key, claim))
            {
                return Task.FromResult<SystemRestorePointCreateClaim?>(null);
            }
            return Task.FromResult<SystemRestorePointCreateClaim?>(claim);
        }

        public Task<SystemRestorePointCreateClaim?> LoadCreateClaimAsync(
            Guid planId,
            string planHash,
            CancellationToken cancellationToken) =>
            Task.FromResult(_claims.GetValueOrDefault($"{planId:D}|{planHash}"));

        public Task SaveCreateClaimAsync(
            SystemRestorePointCreateClaim claim,
            CancellationToken cancellationToken)
        {
            _claimSaveCount++;
            if (_claimSaveCount == FailOnClaimSaveNumber)
            {
                throw new IOException("simulated claim persistence failure");
            }
            _claims[$"{claim.PlanId:D}|{claim.PlanHash}"] = claim;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SystemRestorePointCreateClaim>> ListCreateClaimsAsync(
            CancellationToken cancellationToken) => ListClaimsError is null
                ? Task.FromResult<IReadOnlyList<SystemRestorePointCreateClaim>>(_claims.Values.ToArray())
                : Task.FromException<IReadOnlyList<SystemRestorePointCreateClaim>>(ListClaimsError);

        public Task SaveRestorePlanAsync(SystemRestoreExecutionPlan plan, CancellationToken cancellationToken)
        {
            _restorePlans[plan.PlanId] = plan;
            return Task.CompletedTask;
        }

        public Task<SystemRestoreExecutionPlan?> LoadRestorePlanAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(_restorePlans.GetValueOrDefault(planId));

        public Task SaveReceiptAsync(SystemRestorePointReceipt receipt, CancellationToken cancellationToken)
        {
            _receiptSaveCount++;
            if (_receiptSaveCount == FailOnReceiptSaveNumber)
            {
                throw new IOException("simulated receipt persistence failure");
            }
            _receipts[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }

        public Task<SystemRestorePointReceipt?> LoadReceiptAsync(Guid receiptId, CancellationToken cancellationToken) =>
            ReceiptIsCorrupt
                ? Task.FromException<SystemRestorePointReceipt?>(new InvalidDataException("simulated corrupt receipt"))
                : Task.FromResult(_receipts.GetValueOrDefault(receiptId));

        public Task<SystemRestorePointReceiptRecoveryResult> RecoverReceiptAsync(
            SystemRestorePointReceipt replacement,
            CancellationToken cancellationToken)
        {
            if (RecoverReceiptError is not null)
            {
                return Task.FromException<SystemRestorePointReceiptRecoveryResult>(RecoverReceiptError);
            }
            var disposition = ReceiptIsCorrupt
                ? SystemRestorePointReceiptRecoveryDisposition.ReplacedCorrupt
                : _receipts.ContainsKey(replacement.ReceiptId)
                    ? SystemRestorePointReceiptRecoveryDisposition.ExistingValid
                    : SystemRestorePointReceiptRecoveryDisposition.CreatedMissing;
            if (disposition != SystemRestorePointReceiptRecoveryDisposition.ExistingValid)
            {
                _receipts[replacement.ReceiptId] = replacement;
            }
            ReceiptIsCorrupt = false;
            LastRecoveryDisposition = disposition;
            return Task.FromResult(new SystemRestorePointReceiptRecoveryResult(disposition, _receipts[replacement.ReceiptId]));
        }

        public Task<ISystemRestorePointResourceLease> AcquireResourceLeaseAsync(
            string resourceKey,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            LeaseError is null
                ? Task.FromResult<ISystemRestorePointResourceLease>(new Lease(resourceKey))
                : Task.FromException<ISystemRestorePointResourceLease>(LeaseError);

        private sealed class Lease(string resourceKey) : ISystemRestorePointResourceLease
        {
            public string ResourceKey { get; } = resourceKey;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameOptimizer.Main.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate GameOptimizer.Main.sln.");
    }
}
