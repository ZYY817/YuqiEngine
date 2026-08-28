using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Tests.ViewModels;

public sealed class RestorePointViewModelSafetyTests
{
    [Fact]
    public async Task ProductionBoundaryKeepsCreateDisabledEvenWhenApiAndProtectionAreAvailable()
    {
        var owner = new FakeOwner { Probe = CreateProbe(SystemRestorePointCreationBoundary.InProcessNonCancellable) };
        var viewModel = new RestorePointViewModel(owner, new RejectingPrompt());

        await viewModel.InitializeAsync();

        Assert.False(viewModel.CanCreateRestorePoint);
        Assert.Contains("独立创建 writer", viewModel.CreateAvailabilityText, StringComparison.Ordinal);
        Assert.Equal(0, owner.PrepareCalls);

        var facade = new SystemRestoreService(owner, new RejectingPrompt());
        var capability = facade.CanCreateRestorePoint();
        Assert.False(capability.CanCreate);
        Assert.Contains("isolated, terminable writer", capability.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IsolatedWriterEnablesFirstRestorePointCreationWhenAllConditionsPass()
    {
        var owner = new FakeOwner { Probe = CreateProbe(SystemRestorePointCreationBoundary.IsolatedAndTerminable) };
        var viewModel = new RestorePointViewModel(owner, new RejectingPrompt());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.CanCreateRestorePoint);
        Assert.Contains("第一个还原点", viewModel.CreateAvailabilityText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CooldownShowsRemainingTimeAndKeepsCreationDisabled()
    {
        var probe = CreateProbe(SystemRestorePointCreationBoundary.IsolatedAndTerminable) with
        {
            NextCreationAllowedAtUtc = DateTimeOffset.UtcNow.AddMinutes(35)
        };
        var viewModel = new RestorePointViewModel(new FakeOwner { Probe = probe }, new RejectingPrompt());

        await viewModel.InitializeAsync();

        Assert.False(viewModel.CanCreateRestorePoint);
        Assert.Contains("冷却中", viewModel.CreateAvailabilityText, StringComparison.Ordinal);
        Assert.Contains("分钟", viewModel.CreateAvailabilityText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshCanBeCancelledAndDoesNotStartConcurrentInspection()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new FakeOwner
        {
            Inspect = async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("unreachable");
            }
        };
        var viewModel = new RestorePointViewModel(owner, new RejectingPrompt());

        var refresh = viewModel.RefreshStatusCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(viewModel.IsOperationBusy);

        viewModel.CancelCurrentOperationFromUiCommand.Execute(null);
        await refresh.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.IsOperationBusy);
        Assert.Equal(RestorePointPageState.Partial, viewModel.PageState);
        Assert.Contains("取消", viewModel.RestorePointStatus, StringComparison.Ordinal);
        Assert.Equal(1, owner.InspectCalls);
    }

    [Fact]
    public async Task StartupUnknownReceiptIsVisibleAndManualAcknowledgementStaysUnknown()
    {
        var receipt = new SystemRestorePointReceipt(
            Guid.NewGuid(), Guid.NewGuid(), "HASH", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "volume", "test", "test", null, SystemRestorePointReceiptState.Unknown,
            SystemRestorePointMutationOutcome.Unknown, null, false, false,
            MutationState: SystemRestorePointMutationState.Unknown, ClaimId: Guid.NewGuid());
        var attention = new SystemRestorePointWorkflowResult(
            false, null, SystemRestorePointWorkflowStatus.ManualInterventionRequired,
            SystemRestorePointPhase.Reconcile, "identity could not be proved",
            ManualInterventionRequired: true, Receipt: receipt);
        var startup = new FakeStartupReconciliation(new(
            SystemRestorePointStartupReconciliationStatus.Completed,
            [attention], "classified", ManualInterventionRequired: true));
        var owner = new FakeOwner
        {
            Resolve = resolution => Task.FromResult(attention with
            {
                Message = "acknowledged but remains unknown"
            })
        };
        var viewModel = new RestorePointViewModel(
            owner, new RejectingPrompt(), startupReconciliation: startup);

        await viewModel.InitializeAsync();
        Assert.True(viewModel.HasRecoveryAttention);
        Assert.Contains("identity could not be proved", viewModel.RecoveryAttentionText, StringComparison.Ordinal);

        await viewModel.AcknowledgeUnknownRecoveryCommand.ExecuteAsync(null);
        Assert.Contains("Unknown", viewModel.RecoveryAttentionText, StringComparison.Ordinal);
        Assert.Equal(SystemRestorePointManualResolutionDisposition.AcknowledgeUnknown, owner.LastResolution?.Disposition);
    }

    [Fact]
    public void ManualLaunchFeedbackReflectsSuccessAndFailureWithoutPerformingSystemMutation()
    {
        var launcher = new FakeLauncher();
        var viewModel = new RestorePointViewModel(
            new FakeOwner(), new RejectingPrompt(), manualLauncher: launcher);

        viewModel.OpenSystemRestoreSettingsCommand.Execute(null);
        Assert.Equal(RestorePointPageState.Partial, viewModel.PageState);
        Assert.Equal(1, launcher.SettingsCalls);

        viewModel.OpenSystemRestoreWizardCommand.Execute(null);
        Assert.Equal(RestorePointPageState.Error, viewModel.PageState);
        Assert.Equal(1, launcher.WizardCalls);
    }

    private static SystemRestorePointProbe CreateProbe(SystemRestorePointCreationBoundary boundary) => new(
        true, 26100, true, SystemProtectionState.Enabled, "volume", [], TimeSpan.Zero,
        null, true, false, false, CreationBoundary: boundary);

    private sealed class FakeOwner : ISystemRestorePointOwner
    {
        public SystemRestorePointProbe Probe { get; init; } = CreateProbe(SystemRestorePointCreationBoundary.InProcessNonCancellable);
        public Func<CancellationToken, Task<SystemRestorePointWorkflowResult>>? Inspect { get; init; }
        public Func<SystemRestorePointManualResolution, Task<SystemRestorePointWorkflowResult>>? Resolve { get; init; }
        public int InspectCalls { get; private set; }
        public int PrepareCalls { get; private set; }
        public SystemRestorePointManualResolution? LastResolution { get; private set; }

        public Task<SystemRestorePointWorkflowResult> InspectAsync(CancellationToken cancellationToken = default)
        {
            InspectCalls++;
            return Inspect?.Invoke(cancellationToken) ?? Task.FromResult(new SystemRestorePointWorkflowResult(
                true, false, SystemRestorePointWorkflowStatus.Ready, SystemRestorePointPhase.Inspect,
                "read", Probe: Probe));
        }

        public Task<SystemRestorePointWorkflowResult> PrepareCreateAsync(SystemRestorePointCreateRequest request, CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            return Task.FromResult(Unsupported(SystemRestorePointPhase.Prepare));
        }

        public SystemRestorePointConfirmationToken? ConfirmCreate(SystemRestorePointCreatePlan plan, SystemRestorePointConfirmationEvidence evidence) => null;
        public Task<SystemRestorePointWorkflowResult> ExecuteCreateAsync(SystemRestorePointCreatePlan plan, SystemRestorePointConfirmationToken confirmation, CancellationToken cancellationToken = default) => Task.FromResult(Unsupported(SystemRestorePointPhase.Execute));
        public Task<IReadOnlyList<SystemRestorePointWorkflowResult>> ReconcileInterruptedCreatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SystemRestorePointWorkflowResult>>([]);
        public Task<SystemRestorePointWorkflowResult> ReconcileInterruptedCreateAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(Unsupported(SystemRestorePointPhase.Reconcile));
        public Task<SystemRestorePointWorkflowResult> ResolveInterruptedCreateAsync(SystemRestorePointManualResolution resolution, CancellationToken cancellationToken = default)
        {
            LastResolution = resolution;
            return Resolve?.Invoke(resolution) ?? Task.FromResult(Unsupported(SystemRestorePointPhase.Reconcile));
        }
        public Task<SystemRestorePointWorkflowResult> PrepareSystemRestoreAsync(SystemRestorePointIdentity target, string source, CancellationToken cancellationToken = default) => Task.FromResult(Unsupported(SystemRestorePointPhase.Prepare));
        public Task<SystemRestorePointWorkflowResult> ExecuteSystemRestoreAsync(SystemRestoreExecutionPlan plan, SystemRestorePointConfirmationToken? confirmation, CancellationToken cancellationToken = default) => Task.FromResult(Unsupported(SystemRestorePointPhase.Execute));

        private static SystemRestorePointWorkflowResult Unsupported(SystemRestorePointPhase phase) =>
            new(false, false, SystemRestorePointWorkflowStatus.Unsupported, phase, "fake only");
    }

    private sealed class RejectingPrompt : ISystemRestorePointConfirmationPrompt
    {
        public Task<bool> ConfirmCreateAsync(SystemRestorePointCreatePlan plan, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FakeStartupReconciliation(SystemRestorePointStartupReconciliationResult result)
        : ISystemRestorePointStartupReconciliation
    {
        public SystemRestorePointStartupReconciliationResult? LastResult => result;
        public Task<SystemRestorePointStartupReconciliationResult> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FakeLauncher : ISystemRestoreManualLauncher
    {
        public int SettingsCalls { get; private set; }
        public int WizardCalls { get; private set; }
        public SystemRestoreManualLaunchResult OpenProtectionSettings()
        {
            SettingsCalls++;
            return new(true, "opened settings only");
        }
        public SystemRestoreManualLaunchResult OpenRecoveryWizard()
        {
            WizardCalls++;
            return new(false, "launch failed safely");
        }
    }
}
