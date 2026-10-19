using System.Text.Json.Nodes;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.NetworkReset;
using GameOptimizer.Infrastructure.Windows.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class NetworkResetOwnerTests
{
    [Fact]
    public async Task Read_only_status_never_registers_a_plan_or_invokes_confirmation_or_writer()
    {
        var plans = new FakePlanStore();
        var fixture = new Fixture(plans: plans);

        var status = await fixture.Owner.ReadStatusAsync(NetworkResetProfile.Full);

        Assert.True(status.IsSupported);
        Assert.True(status.IsAdministrator);
        Assert.True(status.SnapshotAvailable);
        Assert.NotNull(status.Snapshot);
        Assert.Equal(0, plans.RegisterCount);
        Assert.Equal(0, fixture.Confirmation.RequestCount);
        Assert.Empty(fixture.Platform.Mutations);

        fixture.Platform.Assessment = new(true, false, "permission.denied", "not admin");
        var nonAdmin = await fixture.Owner.ReadStatusAsync(NetworkResetProfile.Full);
        Assert.False(nonAdmin.IsAdministrator);
        Assert.False(nonAdmin.SnapshotAvailable);
        Assert.Equal(0, plans.RegisterCount);
        Assert.Empty(fixture.Platform.Mutations);
    }

    [Fact]
    public async Task High_impact_network_configuration_blocks_winsock_tcpip_and_full_before_confirmation_or_write()
    {
        var fixture = new Fixture();
        fixture.Platform.Safety = new NetworkResetSafetyAssessment(
            false,
            "safety.high-impact-network-configuration",
            "VPN active",
            [new NetworkResetSafetyFinding("network.vpn-or-tunnel-active", "VPN active")]);

        foreach (var profile in new[] { NetworkResetProfile.Winsock, NetworkResetProfile.TcpIp, NetworkResetProfile.Full })
        {
            var status = await fixture.Owner.ReadStatusAsync(profile);
            var receipt = await fixture.Owner.RunInteractiveAsync(profile);

            Assert.False(status.IsSupported);
            Assert.Equal("safety.high-impact-network-configuration", status.ReasonCode);
            Assert.NotNull(status.SafetyAssessment);
            Assert.Equal(NetworkResetOutcome.Blocked, receipt.Outcome);
            Assert.Empty(fixture.Platform.Mutations);
        }

        Assert.Equal(0, fixture.Confirmation.RequestCount);
    }

    [Fact]
    public async Task Dns_cache_profile_ignores_network_stack_safety_block()
    {
        var fixture = new Fixture();
        fixture.Platform.Safety = new NetworkResetSafetyAssessment(
            false,
            "safety.high-impact-network-configuration",
            "VPN active",
            [new NetworkResetSafetyFinding("network.vpn-or-tunnel-active", "VPN active")]);

        var status = await fixture.Owner.ReadStatusAsync(NetworkResetProfile.DnsCache);

        Assert.True(status.IsSupported);
        Assert.True(status.SnapshotAvailable);
        Assert.Null(status.SafetyAssessment);
    }

    [Fact]
    public async Task Durable_plan_store_persists_full_plan_and_claims_once_across_instances()
    {
        var root = TempDirectory("plan");
        try
        {
            var fixture = new Fixture(plans: new FileNetworkResetPlanStore(root));
            var plan = await fixture.Owner.PrepareAsync(NetworkResetProfile.DnsCache);
            var loaded = await fixture.Plans.LoadAsync(plan.ChangePlan.PlanId, default);

            Assert.NotNull(loaded);
            Assert.Equal(plan.ChangePlan.PlanId, loaded.ChangePlan.PlanId);
            Assert.Equal(plan.ChangePlan.PlanHash, loaded.ChangePlan.PlanHash);
            Assert.Equal(plan.Profile, loaded.Profile);
            Assert.Equal(plan.Steps, loaded.Steps);
            Assert.Equal(plan.Snapshot.IdentityHash, loaded.Snapshot.IdentityHash);
            Assert.Equal(plan.Snapshot.IsMachineRestorable, loaded.Snapshot.IsMachineRestorable);
            Assert.Equal(plan.Snapshot.RecoveryBoundary, loaded.Snapshot.RecoveryBoundary);
            Assert.Equal(plan.Snapshot.Entries, loaded.Snapshot.Entries);
            Assert.Equal(PlanClaimStatus.HashMismatch,
                await fixture.Plans.TryClaimAsync(
                    plan.ChangePlan.PlanId,
                    new string('F', 64),
                    default));
            var secondStore = new FileNetworkResetPlanStore(root);
            var claims = await Task.WhenAll(
                fixture.Plans.TryClaimAsync(
                    plan.ChangePlan.PlanId,
                    plan.ChangePlan.PlanHash,
                    default),
                secondStore.TryClaimAsync(
                    plan.ChangePlan.PlanId,
                    plan.ChangePlan.PlanHash,
                    default));

            Assert.Single(claims, status => status == PlanClaimStatus.Claimed);
            Assert.Single(claims, status => status == PlanClaimStatus.AlreadyClaimed);
            Assert.Equal(PlanClaimStatus.AlreadyClaimed,
                await new FileNetworkResetPlanStore(root).TryClaimAsync(
                    plan.ChangePlan.PlanId,
                    plan.ChangePlan.PlanHash,
                    default));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Durable_plan_store_rejects_payload_tampering()
    {
        var root = TempDirectory("plan-tamper");
        try
        {
            var store = new FileNetworkResetPlanStore(root);
            var fixture = new Fixture(plans: store);
            var plan = await fixture.Owner.PrepareAsync(NetworkResetProfile.Winsock);
            var path = Path.Combine(root, $"{plan.ChangePlan.PlanId:N}.json");
            var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            node["payload"]!["rebootRequired"] = false;
            await File.WriteAllTextAsync(path, node.ToJsonString());

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.LoadAsync(plan.ChangePlan.PlanId, default));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Receipt_store_supports_durable_checkpoints_and_rejects_tampering()
    {
        var root = TempDirectory("receipt");
        try
        {
            var store = new FileNetworkResetReceiptStore(root);
            var executing = CreateReceipt(
                NetworkResetReceiptState.Executing,
                NetworkResetReceiptPhase.StepExecuting,
                NetworkResetOutcome.InProgress,
                manual: false,
                reboot: false,
                completed: null);
            await store.SaveAsync(executing, default);
            var loadedExecuting = await store.LoadAsync(executing.ReceiptId);
            Assert.NotNull(loadedExecuting);
            Assert.Equal(executing.ReceiptId, loadedExecuting.ReceiptId);
            Assert.Equal(executing.PlanHash, loadedExecuting.PlanHash);
            Assert.Equal(executing.State, loadedExecuting.State);
            Assert.Equal(executing.Phase, loadedExecuting.Phase);

            var terminal = executing with
            {
                State = NetworkResetReceiptState.Completed,
                Phase = NetworkResetReceiptPhase.Completed,
                Outcome = NetworkResetOutcome.SucceededVerified,
                CurrentStep = null,
                CompletedAtUtc = executing.StartedAtUtc.AddSeconds(1)
            };
            await store.SaveAsync(terminal, default);
            var loadedTerminal = await store.LoadAsync(terminal.ReceiptId);
            Assert.NotNull(loadedTerminal);
            Assert.Equal(terminal.ReceiptId, loadedTerminal.ReceiptId);
            Assert.Equal(terminal.State, loadedTerminal.State);
            Assert.Equal(terminal.Phase, loadedTerminal.Phase);
            Assert.Equal(terminal.CurrentStep, loadedTerminal.CurrentStep);
            Assert.Equal(terminal.Steps, loadedTerminal.Steps);

            var path = Path.Combine(root, $"{terminal.ReceiptId:N}.json");
            var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            node["payload"]!["message"] = "tampered";
            await File.WriteAllTextAsync(path, node.ToJsonString());
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(terminal.ReceiptId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Durable_store_projects_an_interrupted_writer_as_unknown_manual_recovery()
    {
        var root = TempDirectory("receipt-crash");
        try
        {
            var store = new FileNetworkResetReceiptStore(root);
            var executing = CreateReceipt(
                NetworkResetReceiptState.Executing,
                NetworkResetReceiptPhase.StepExecuting,
                NetworkResetOutcome.InProgress,
                manual: true,
                reboot: true,
                completed: null) with
            {
                Profile = NetworkResetProfile.Winsock,
                RecoveryDecision = NetworkRecoveryDecision.RecoveryRequired
            };
            await store.SaveAsync(executing, default);

            var pending = Assert.Single(await store.LoadPendingAsync(default));

            Assert.Equal(NetworkResetReceiptState.Unknown, pending.State);
            Assert.Equal(NetworkResetOutcome.RecoveryRequired, pending.Outcome);
            Assert.Equal("receipt.incomplete-write-unknown", pending.ReasonCode);
            Assert.True(pending.ManualInterventionRequired);
            Assert.True(pending.RebootRequired);
            Assert.Contains("不要自动重试", pending.RecoveryInstructions, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Different_plans_across_owner_instances_share_one_global_machine_lease()
    {
        var root = TempDirectory("lease");
        try
        {
            var planRoot = Path.Combine(root, "plans");
            var lockRoot = Path.Combine(root, "locks");
            var firstPlatform = new FakePlatform { BlockFirstMutation = true };
            var secondPlatform = new FakePlatform();
            var first = new Fixture(
                new FileNetworkResetPlanStore(planRoot),
                firstPlatform,
                new FileChangeResourceLockProvider(lockRoot),
                TimeSpan.FromMilliseconds(150));
            var second = new Fixture(
                new FileNetworkResetPlanStore(planRoot),
                secondPlatform,
                new FileChangeResourceLockProvider(lockRoot),
                TimeSpan.FromMilliseconds(150));

            var firstTask = first.Owner.RunInteractiveAsync(NetworkResetProfile.DnsCache);
            await firstPlatform.MutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var blocked = await second.Owner.RunInteractiveAsync(NetworkResetProfile.Winsock);

            Assert.Equal(NetworkResetOutcome.Blocked, blocked.Outcome);
            Assert.Equal("resource-lock.timeout", blocked.ReasonCode);
            Assert.Empty(secondPlatform.Mutations);

            firstPlatform.ReleaseMutation.TrySetResult(true);
            Assert.Equal(NetworkResetOutcome.SucceededVerified, (await firstTask).Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Caller_cancel_after_dns_process_start_never_reports_success()
    {
        var fixture = new Fixture();
        fixture.Platform.BlockFirstMutation = true;
        using var cts = new CancellationTokenSource();

        var task = fixture.Owner.RunInteractiveAsync(NetworkResetProfile.DnsCache, cts.Token);
        await fixture.Platform.MutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        Assert.False(fixture.Platform.MutationTokenWasCancelled);
        fixture.Platform.ReleaseMutation.TrySetResult(true);

        var receipt = await task;
        Assert.Equal(NetworkResetOutcome.RecoveryRequired, receipt.Outcome);
        Assert.Equal("network.reset.completed-cancel-observed", receipt.ReasonCode);
        Assert.True(receipt.ManualInterventionRequired);
        Assert.Single(fixture.Platform.Mutations);
    }

    [Fact]
    public async Task Caller_cancel_during_final_step_is_recorded_without_detaching_writer()
    {
        var fixture = new Fixture();
        fixture.Platform.BlockFirstMutation = true;
        using var cts = new CancellationTokenSource();

        var task = fixture.Owner.RunInteractiveAsync(NetworkResetProfile.Winsock, cts.Token);
        await fixture.Platform.MutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        fixture.Platform.ReleaseMutation.TrySetResult(true);

        var receipt = await task;
        Assert.Equal(NetworkResetOutcome.RecoveryRequired, receipt.Outcome);
        Assert.Equal("network.reset.completed-cancel-observed", receipt.ReasonCode);
        Assert.True(receipt.ManualInterventionRequired);
        Assert.True(receipt.RebootRequired);
        Assert.False(fixture.Platform.MutationTokenWasCancelled);
    }

    [Fact]
    public async Task Receipt_failure_before_writer_blocks_with_zero_mutation()
    {
        var store = new FakeReceiptStore { FailOnSaveNumber = 1 };
        var fixture = new Fixture(receipts: store);

        var receipt = await fixture.Owner.RunInteractiveAsync(NetworkResetProfile.DnsCache);

        Assert.Equal(NetworkResetOutcome.FailedNoChange, receipt.Outcome);
        Assert.Equal("receipt.persist-failed-no-change", receipt.ReasonCode);
        Assert.False(receipt.MutationOccurred);
        Assert.Empty(fixture.Platform.Mutations);
    }

    [Fact]
    public async Task Step_executing_checkpoint_failure_before_first_writer_is_zero_change()
    {
        var store = new FakeReceiptStore
        {
            FailOnceOnPhase = NetworkResetReceiptPhase.StepExecuting
        };
        var fixture = new Fixture(receipts: store);

        var receipt = await fixture.Owner.RunInteractiveAsync(NetworkResetProfile.Winsock);

        Assert.Equal(NetworkResetOutcome.FailedNoChange, receipt.Outcome);
        Assert.Equal(NetworkResetReceiptState.Unknown, receipt.State);
        Assert.False(receipt.MutationOccurred);
        Assert.False(receipt.ManualInterventionRequired);
        Assert.False(receipt.RebootRequired);
        Assert.Empty(fixture.Platform.Mutations);
    }

    [Fact]
    public async Task Step_checkpoint_failure_after_mutation_stops_and_survives_new_owner_instance()
    {
        var store = new FakeReceiptStore
        {
            FailOnceOnPhase = NetworkResetReceiptPhase.StepCompleted
        };
        var plans = new FakePlanStore();
        var first = new Fixture(plans: plans, receipts: store);

        var receipt = await first.Owner.RunInteractiveAsync(NetworkResetProfile.Winsock);

        Assert.Equal(NetworkResetOutcome.RecoveryRequired, receipt.Outcome);
        Assert.True(receipt.ManualInterventionRequired);
        Assert.Single(first.Platform.Mutations);

        var restarted = new Fixture(plans: plans, receipts: store);
        var pending = await restarted.Owner.GetPendingRecoveryReceiptsAsync();
        var durable = Assert.Single(pending);
        Assert.Equal(NetworkResetReceiptState.Unknown, durable.State);
        Assert.Equal(NetworkResetOutcome.RecoveryRequired, durable.Outcome);
        Assert.Equal("receipt.incomplete-write-unknown", durable.ReasonCode);
        Assert.Equal(NetworkResetReceiptPhase.StepExecuting, durable.Phase);
        Assert.Equal(NetworkResetCommand.ResetWinsock, durable.CurrentStep);
        Assert.True(durable.MutationOccurred);
        Assert.True(durable.ManualInterventionRequired);
        Assert.True(durable.RebootRequired);
        Assert.Equal(NetworkRecoveryDecision.RecoveryRequired, durable.RecoveryDecision);
        Assert.Contains("不要自动重试", durable.RecoveryInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Terminal_receipt_failure_after_mutation_is_unknown_and_requires_manual_intervention()
    {
        var store = new FakeReceiptStore
        {
            FailOnceOnPhase = NetworkResetReceiptPhase.Completed
        };
        var fixture = new Fixture(receipts: store);

        var receipt = await fixture.Owner.RunInteractiveAsync(NetworkResetProfile.DnsCache);

        Assert.Equal(NetworkResetReceiptState.Unknown, receipt.State);
        Assert.Equal(NetworkResetOutcome.RecoveryRequired, receipt.Outcome);
        Assert.True(receipt.ManualInterventionRequired);
        Assert.Contains(receipt.ReceiptId.ToString("N"), receipt.Message, StringComparison.Ordinal);
        Assert.Contains(receipt.PlanId.ToString("N"), receipt.Message, StringComparison.Ordinal);
        Assert.NotEmpty(await fixture.Owner.GetPendingRecoveryReceiptsAsync());
    }

    [Fact]
    public async Task Full_reset_has_fixed_steps_no_firewall_and_honest_manual_recovery()
    {
        var fixture = new Fixture();

        var receipt = await fixture.Owner.RunInteractiveAsync(NetworkResetProfile.Full);

        Assert.Equal(NetworkResetOutcome.AppliedRequiresRestartManualRecovery, receipt.Outcome);
        Assert.Equal(NetworkResetReceiptState.ManualInterventionRequired, receipt.State);
        Assert.Equal(NetworkRecoveryDecision.ManualRecoveryOnly, receipt.RecoveryDecision);
        Assert.True(receipt.ManualInterventionRequired);
        Assert.True(receipt.RebootRequired);
        Assert.Equal(4, receipt.Steps.Count);
        Assert.All(receipt.Steps.Where(step => !IsDns(step.Command)), step =>
        {
            Assert.True(step.ReadBackCompleted);
            Assert.False(step.EffectVerified);
        });
        Assert.DoesNotContain(
            fixture.Platform.Mutations,
            command => command.ToString().Contains("Firewall", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(NetworkResetReceipt).GetProperties(),
            property => property.Name.Contains("RecoveryId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_step_has_a_write_unknown_checkpoint_and_a_durable_completed_checkpoint()
    {
        var fixture = new Fixture();

        var receipt = await fixture.Owner.RunInteractiveAsync(NetworkResetProfile.Full);

        Assert.Equal(4, fixture.Receipts.Saves.Count(item =>
            item.Phase == NetworkResetReceiptPhase.StepExecuting));
        Assert.Equal(4, fixture.Receipts.Saves.Count(item =>
            item.Phase == NetworkResetReceiptPhase.StepCompleted));
        Assert.All(
            fixture.Receipts.Saves.Where(item => item.Phase == NetworkResetReceiptPhase.StepExecuting),
            checkpoint =>
            {
                Assert.True(checkpoint.MutationOccurred);
                Assert.True(checkpoint.ManualInterventionRequired);
                Assert.True(checkpoint.RebootRequired);
                Assert.NotNull(checkpoint.CurrentStep);
            });
        Assert.Equal(receipt.Steps, fixture.Receipts.Saves
            .Last(item => item.Phase == NetworkResetReceiptPhase.StepCompleted)
            .Steps);
    }

    [Fact]
    public async Task Dns_success_means_command_completed_and_postflight_is_accessible_not_cache_contents_verified()
    {
        var fixture = new Fixture();

        var receipt = await fixture.Owner.RunInteractiveAsync(NetworkResetProfile.DnsCache);

        Assert.Equal(NetworkResetOutcome.SucceededVerified, receipt.Outcome);
        Assert.Equal(NetworkResetReceiptState.Completed, receipt.State);
        Assert.False(receipt.ManualInterventionRequired);
        Assert.False(receipt.RebootRequired);
        Assert.Equal(NetworkRecoveryDecision.NotRequired, receipt.RecoveryDecision);
        Assert.All(receipt.Steps, step => Assert.False(step.EffectVerified));
        Assert.Equal(
            [NetworkResetCommand.FlushDnsCache],
            fixture.Platform.Mutations);
        Assert.Equal("dns.postflight-accessible", receipt.Steps.Single().ReasonCode);
        Assert.Contains("不能据此证明缓存内容已清空", receipt.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Permission_and_snapshot_failures_block_before_confirmation_or_writer()
    {
        var permission = new Fixture();
        permission.Platform.Assessment = new(true, false, "permission.denied", "not admin");
        var blocked = await permission.Owner.RunInteractiveAsync(NetworkResetProfile.Full);
        Assert.Equal(NetworkResetOutcome.Blocked, blocked.Outcome);
        Assert.Empty(permission.Platform.Mutations);
        Assert.Equal(0, permission.Confirmation.RequestCount);

        var snapshot = new Fixture();
        snapshot.Platform.ReadFailure = NetworkResetCommand.ShowWinsockCatalog;
        var failed = await snapshot.Owner.RunInteractiveAsync(NetworkResetProfile.Winsock);
        Assert.Equal(NetworkResetOutcome.Blocked, failed.Outcome);
        Assert.Empty(snapshot.Platform.Mutations);
        Assert.Equal(0, snapshot.Confirmation.RequestCount);
    }

    [Fact]
    public async Task Baseline_failure_after_confirmation_causes_zero_mutation()
    {
        var fixture = new Fixture();
        fixture.Baseline.Decision = new(false, "baseline.failed", "no baseline", null);

        var receipt = await fixture.Owner.RunInteractiveAsync(NetworkResetProfile.Full);

        Assert.Equal(NetworkResetOutcome.Blocked, receipt.Outcome);
        Assert.Equal("baseline.failed", receipt.ReasonCode);
        Assert.Empty(fixture.Platform.Mutations);
    }

    [Fact]
    public async Task Confirmation_denial_forgery_replay_and_cross_instance_use_are_blocked()
    {
        var deniedFixture = new Fixture();
        deniedFixture.Confirmation.Deny = true;
        var denied = await deniedFixture.Owner.RunInteractiveAsync(NetworkResetProfile.Winsock);
        Assert.Equal(NetworkResetOutcome.Denied, denied.Outcome);
        Assert.Empty(deniedFixture.Platform.Mutations);

        var forgedFixture = new Fixture();
        var forgedPlan = await forgedFixture.Owner.PrepareAsync(NetworkResetProfile.DnsCache);
        var neverIssued = new ConfirmationToken(
            Guid.NewGuid(),
            forgedPlan.ChangePlan.PlanHash,
            forgedPlan.ChangePlan.Change.Scope,
            forgedPlan.ChangePlan.ExpiresAtUtc,
            true);
        var forged = await forgedFixture.Owner.ExecuteAsync(forgedPlan, neverIssued, default);
        Assert.Equal(NetworkResetOutcome.Denied, forged.Outcome);
        Assert.Equal("confirmation.invalid", forged.ReasonCode);
        Assert.Empty(forgedFixture.Platform.Mutations);

        var tamperedToken = new ConfirmationToken(
            Guid.NewGuid(),
            new string('B', 64),
            forgedPlan.ChangePlan.Change.Scope,
            forgedPlan.ChangePlan.ExpiresAtUtc,
            true);
        var tamperedConfirmation = await forgedFixture.Owner.ExecuteAsync(
            forgedPlan,
            tamperedToken,
            default);
        Assert.Equal(NetworkResetOutcome.Denied, tamperedConfirmation.Outcome);
        Assert.Equal("confirmation.invalid", tamperedConfirmation.ReasonCode);
        Assert.Empty(forgedFixture.Platform.Mutations);

        var replayFixture = new Fixture();
        var plan = await replayFixture.Owner.PrepareAsync(NetworkResetProfile.Winsock);
        var token = await replayFixture.Confirmation.RequestAsync(
            plan.ChangePlan,
            "title",
            "summary",
            default);
        var first = await replayFixture.Owner.ExecuteAsync(plan, token!, default);
        var replay = await replayFixture.Owner.ExecuteAsync(plan, token!, default);
        Assert.Equal(NetworkResetOutcome.AppliedRequiresRestartManualRecovery, first.Outcome);
        Assert.Equal(NetworkResetOutcome.Denied, replay.Outcome);

        var sharedPlans = new FakePlanStore();
        var sharedReceipts = new FakeReceiptStore();
        var issuer = new Fixture(plans: sharedPlans, receipts: sharedReceipts);
        var otherInstance = new Fixture(plans: sharedPlans, receipts: sharedReceipts);
        var crossPlan = await issuer.Owner.PrepareAsync(NetworkResetProfile.DnsCache);
        var crossToken = await issuer.Confirmation.RequestAsync(
            crossPlan.ChangePlan,
            "title",
            "summary",
            default);
        var cross = await otherInstance.Owner.ExecuteAsync(crossPlan, crossToken!, default);
        Assert.Equal(NetworkResetOutcome.Denied, cross.Outcome);
        Assert.Empty(otherInstance.Platform.Mutations);
    }

    [Fact]
    public async Task Snapshot_drift_and_forged_plan_content_block_before_first_write()
    {
        var drift = new Fixture();
        var driftPlan = await drift.Owner.PrepareAsync(NetworkResetProfile.TcpIp);
        var driftToken = await drift.Confirmation.RequestAsync(
            driftPlan.ChangePlan,
            "title",
            "summary",
            default);
        drift.Platform.DriftReads = true;
        var driftReceipt = await drift.Owner.ExecuteAsync(driftPlan, driftToken!, default);
        Assert.Equal(NetworkResetOutcome.Conflict, driftReceipt.Outcome);
        Assert.Equal("snapshot.drift", driftReceipt.ReasonCode);
        Assert.Empty(drift.Platform.Mutations);

        var forged = new Fixture();
        var forgedPlan = await forged.Owner.PrepareAsync(NetworkResetProfile.Winsock);
        var forgedToken = await forged.Confirmation.RequestAsync(
            forgedPlan.ChangePlan,
            "title",
            "summary",
            default);
        var tampered = forgedPlan with
        {
            RebootRequired = false,
            Steps = [NetworkResetCommand.FlushDnsCache]
        };
        var forgedReceipt = await forged.Owner.ExecuteAsync(tampered, forgedToken!, default);
        Assert.Equal(NetworkResetOutcome.Conflict, forgedReceipt.Outcome);
        Assert.Equal("plan.integrity-mismatch", forgedReceipt.ReasonCode);
        Assert.Empty(forged.Platform.Mutations);
    }

    [Fact]
    public async Task Expired_plan_is_denied_without_mutation()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 13, 6, 0, 0, TimeSpan.Zero));
        var fixture = new Fixture(clock: clock);
        var plan = await fixture.Owner.PrepareAsync(NetworkResetProfile.DnsCache);
        var token = await fixture.Confirmation.RequestAsync(plan.ChangePlan, "title", "summary", default);
        clock.Advance(TimeSpan.FromMinutes(3));

        var receipt = await fixture.Owner.ExecuteAsync(plan, token!, default);

        Assert.Equal(NetworkResetOutcome.Denied, receipt.Outcome);
        Assert.Equal("plan.expired", receipt.ReasonCode);
        Assert.Empty(fixture.Platform.Mutations);
    }

    [Fact]
    public async Task Partial_execution_timeout_and_readback_failure_never_report_success()
    {
        var partial = new Fixture();
        partial.Platform.FailMutation = NetworkResetCommand.ResetIpv4;
        var partialReceipt = await partial.Owner.RunInteractiveAsync(NetworkResetProfile.Full);
        Assert.Equal(NetworkResetOutcome.RecoveryRequired, partialReceipt.Outcome);
        Assert.Equal(
            [NetworkResetCommand.ResetWinsock, NetworkResetCommand.ResetIpv4],
            partial.Platform.Mutations);
        Assert.Equal(2, partialReceipt.Steps.Count);

        var timeout = new Fixture();
        timeout.Platform.TimeoutMutation = NetworkResetCommand.ResetWinsock;
        var timeoutReceipt = await timeout.Owner.RunInteractiveAsync(NetworkResetProfile.Winsock);
        Assert.Equal(NetworkResetOutcome.RecoveryRequired, timeoutReceipt.Outcome);
        Assert.Contains("timeout", timeoutReceipt.ReasonCode);

        var readBack = new Fixture();
        readBack.Platform.VerifyFailure = NetworkResetCommand.ResetWinsock;
        var readBackReceipt = await readBack.Owner.RunInteractiveAsync(NetworkResetProfile.Winsock);
        Assert.Equal(NetworkResetOutcome.RecoveryRequired, readBackReceipt.Outcome);
        Assert.Equal("readback.mismatch", readBackReceipt.ReasonCode);
    }

    [Fact]
    public async Task Cancellation_before_execution_is_zero_write_and_durable_terminal()
    {
        var fixture = new Fixture();
        var plan = await fixture.Owner.PrepareAsync(NetworkResetProfile.Full);
        var token = await fixture.Confirmation.RequestAsync(
            plan.ChangePlan,
            "title",
            "summary",
            default);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var receipt = await fixture.Owner.ExecuteAsync(plan, token!, cts.Token);

        Assert.Equal(NetworkResetOutcome.CancelledBeforeWrite, receipt.Outcome);
        Assert.False(receipt.MutationOccurred);
        Assert.Empty(fixture.Platform.Mutations);
    }

    private sealed class Fixture
    {
        public FakePlatform Platform { get; }
        public FakeBaseline Baseline { get; } = new();
        public FakeConfirmation Confirmation { get; }
        public INetworkResetPlanStore Plans { get; }
        public FakeReceiptStore Receipts { get; }
        public NetworkResetOwner Owner { get; }

        public Fixture(
            INetworkResetPlanStore? plans = null,
            FakePlatform? platform = null,
            IChangeResourceLockProvider? locks = null,
            TimeSpan? lockTimeout = null,
            FakeReceiptStore? receipts = null,
            FakeConfirmation? confirmation = null,
            TimeProvider? clock = null)
        {
            Platform = platform ?? new FakePlatform();
            Plans = plans ?? new FakePlanStore();
            Receipts = receipts ?? new FakeReceiptStore();
            Confirmation = confirmation ?? new FakeConfirmation(clock);
            Owner = new NetworkResetOwner(
                Platform,
                Baseline,
                Confirmation,
                Plans,
                Receipts,
                locks ?? new FakeResourceLockProvider(),
                clock ?? TimeProvider.System,
                lockTimeout);
        }
    }

    private sealed class FakePlatform : INetworkResetPlatform
    {
        private int _readGeneration;
        public NetworkResetPlatformAssessment Assessment { get; set; } =
            new(true, true, "ready", "ready");
        public NetworkResetSafetyAssessment Safety { get; set; } = new(
            true,
            "safety.clear",
            "safe",
            Array.Empty<NetworkResetSafetyFinding>());
        public NetworkResetCommand? ReadFailure { get; set; }
        public NetworkResetCommand? FailMutation { get; set; }
        public NetworkResetCommand? TimeoutMutation { get; set; }
        public NetworkResetCommand? VerifyFailure { get; set; }
        public bool DriftReads { get; set; }
        public List<NetworkResetCommand> Mutations { get; } = [];
        public bool BlockFirstMutation { get; set; }
        public bool MutationTokenWasCancelled { get; private set; }
        public TaskCompletionSource<bool> MutationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseMutation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NetworkResetPlatformAssessment> ProbeAsync(
            CancellationToken cancellationToken) => Task.FromResult(Assessment);

        public Task<NetworkResetSafetyAssessment> AssessResetSafetyAsync(
            NetworkResetProfile profile,
            CancellationToken cancellationToken) => Task.FromResult(Safety);

        public Task<NetworkResetCommandResult> RunReadOnlyAsync(
            NetworkResetCommand command,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadFailure == command)
            {
                return Task.FromResult(new NetworkResetCommandResult(
                    command,
                    5,
                    string.Empty,
                    "denied",
                    false,
                    true));
            }
            var generation = DriftReads ? ++_readGeneration : 0;
            return Task.FromResult(new NetworkResetCommandResult(
                command,
                0,
                $"{command}:state:{generation}",
                string.Empty,
                false,
                true));
        }

        public async Task<NetworkResetCommandResult> RunMutationAsync(
            NetworkResetCommand command,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Mutations.Add(command);
            MutationTokenWasCancelled |= cancellationToken.IsCancellationRequested;
            if (BlockFirstMutation && Mutations.Count == 1)
            {
                MutationStarted.TrySetResult(true);
                await ReleaseMutation.Task.ConfigureAwait(false);
                MutationTokenWasCancelled |= cancellationToken.IsCancellationRequested;
            }
            if (TimeoutMutation == command)
            {
                return new NetworkResetCommandResult(
                    command,
                    -1,
                    string.Empty,
                    "timeout",
                    true,
                    true);
            }
            if (FailMutation == command)
            {
                return new NetworkResetCommandResult(
                    command,
                    1,
                    string.Empty,
                    "failed",
                    false,
                    true);
            }
            return new NetworkResetCommandResult(
                command,
                0,
                "ok",
                string.Empty,
                false,
                true);
        }

        public Task<bool> VerifyAsync(
            NetworkResetCommand command,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(VerifyFailure != command);
    }

    private sealed class FakeBaseline : INetworkResetBaselineGate
    {
        public NetworkResetBaselineDecision Decision { get; set; } =
            new(true, "baseline.authorized", "ok", "evidence");

        public Task<NetworkResetBaselineDecision> AuthorizeAsync(
            NetworkResetPlan plan,
            CancellationToken cancellationToken) => Task.FromResult(Decision);
    }

    private sealed class FakeConfirmation : IChangeConfirmationAuthority
    {
        private readonly HashSet<Guid> _issued = [];
        private readonly TimeProvider _clock;

        public FakeConfirmation(TimeProvider? clock = null) =>
            _clock = clock ?? TimeProvider.System;

        public bool Deny { get; set; }
        public int RequestCount { get; private set; }

        public Task<ConfirmationToken?> RequestAsync(
            PreparedChangePlan plan,
            string title,
            string summary,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (Deny || _clock.GetUtcNow() > plan.ExpiresAtUtc)
            {
                return Task.FromResult<ConfirmationToken?>(null);
            }
            var token = new ConfirmationToken(
                Guid.NewGuid(),
                plan.PlanHash,
                plan.Change.Scope,
                plan.ExpiresAtUtc,
                true);
            _issued.Add(token.TokenId);
            return Task.FromResult<ConfirmationToken?>(token);
        }

        public bool TryConsume(PreparedChangePlan plan, ConfirmationToken token) =>
            _clock.GetUtcNow() <= token.ExpiresAtUtc
            && token.PlanHash == plan.PlanHash
            && token.Scope == plan.Change.Scope
            && _issued.Remove(token.TokenId);
    }

    private sealed class FakePlanStore : INetworkResetPlanStore
    {
        private readonly Dictionary<Guid, NetworkResetPlan> _plans = [];
        private readonly HashSet<Guid> _claimed = [];
        public int RegisterCount { get; private set; }

        public Task RegisterAsync(NetworkResetPlan plan, CancellationToken cancellationToken)
        {
            RegisterCount++;
            _plans.Add(plan.ChangePlan.PlanId, plan);
            return Task.CompletedTask;
        }

        public Task<NetworkResetPlan?> LoadAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(_plans.GetValueOrDefault(planId));

        public Task<PlanClaimStatus> TryClaimAsync(
            Guid planId,
            string planHash,
            CancellationToken cancellationToken)
        {
            if (!_plans.TryGetValue(planId, out var expected))
            {
                return Task.FromResult(PlanClaimStatus.Missing);
            }
            if (expected.ChangePlan.PlanHash != planHash)
            {
                return Task.FromResult(PlanClaimStatus.HashMismatch);
            }
            return Task.FromResult(
                _claimed.Add(planId)
                    ? PlanClaimStatus.Claimed
                    : PlanClaimStatus.AlreadyClaimed);
        }
    }

    private sealed class FakeReceiptStore : INetworkResetReceiptStore
    {
        private readonly Dictionary<Guid, NetworkResetReceipt> _latest = [];
        private bool _phaseFailureConsumed;

        public List<NetworkResetReceipt> Saves { get; } = [];
        public int? FailOnSaveNumber { get; set; }
        public NetworkResetReceiptPhase? FailOnceOnPhase { get; set; }

        public Task SaveAsync(NetworkResetReceipt receipt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var number = Saves.Count + 1;
            if (number == FailOnSaveNumber
                || (!_phaseFailureConsumed && receipt.Phase == FailOnceOnPhase))
            {
                _phaseFailureConsumed = true;
                throw new IOException("receipt volume unavailable");
            }
            Saves.Add(receipt);
            _latest[receipt.ReceiptId] = receipt;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NetworkResetReceipt>> LoadPendingAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<NetworkResetReceipt> pending = _latest.Values
                .Where(item => item.State != NetworkResetReceiptState.Completed
                    || item.ManualInterventionRequired
                    || item.RebootRequired)
                .Select(item => item.State == NetworkResetReceiptState.Executing
                    ? item with
                    {
                        State = NetworkResetReceiptState.Unknown,
                        Outcome = item.MutationOccurred
                            ? NetworkResetOutcome.RecoveryRequired
                            : NetworkResetOutcome.FailedNoChange,
                        ReasonCode = item.MutationOccurred
                            ? "receipt.incomplete-write-unknown"
                            : "receipt.incomplete-before-write",
                        ManualInterventionRequired = item.MutationOccurred,
                        RebootRequired = item.MutationOccurred
                            && item.Profile != NetworkResetProfile.DnsCache,
                        RecoveryDecision = item.MutationOccurred
                            ? NetworkRecoveryDecision.RecoveryRequired
                            : NetworkRecoveryDecision.NotRequired
                    }
                    : item)
                .OrderByDescending(item => item.StartedAtUtc)
                .ToArray();
            return Task.FromResult(pending);
        }
    }

    private sealed class FakeResourceLockProvider : IChangeResourceLockProvider
    {
        private readonly SemaphoreSlim _mutex = new(1, 1);

        public async Task<IChangeResourceLease?> TryAcquireAsync(
            string resourceKey,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (!await _mutex.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
            return new Lease(_mutex);
        }

        private sealed class Lease(SemaphoreSlim mutex) : IChangeResourceLease
        {
            private SemaphoreSlim? _mutex = mutex;

            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _mutex, null)?.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private static string TempDirectory(string suffix)
    {
        var path = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"yuqi-network-{suffix}-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static NetworkResetReceipt CreateReceipt(
        NetworkResetReceiptState state,
        NetworkResetReceiptPhase phase,
        NetworkResetOutcome outcome,
        bool manual,
        bool reboot,
        DateTimeOffset? completed)
    {
        var now = DateTimeOffset.UtcNow;
        return new NetworkResetReceipt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('A', 64),
            NetworkResetProfile.DnsCache,
            state,
            phase,
            outcome,
            "step.executing",
            "checkpoint",
            true,
            manual,
            reboot,
            manual ? NetworkRecoveryDecision.ManualRecoveryOnly : NetworkRecoveryDecision.NotRequired,
            "instructions",
            Array.Empty<NetworkResetStepReceipt>(),
            NetworkResetCommand.FlushDnsCache,
            now,
            completed);
    }

    private static bool IsDns(NetworkResetCommand command) =>
        command == NetworkResetCommand.FlushDnsCache;
}
