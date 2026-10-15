using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Snapshot;
using GameOptimizer.Core.Services.Startup;
using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Tests.Safety;

public sealed class BaselineAdapterRiskAcknowledgementTests
{
    [Fact]
    public async Task ChangeAndRepairApply_AcceptExplicitUnprotectedDecision()
    {
        var baseline = new FakeBaselineGate(AcknowledgedDecision());
        var changeGate = new BaselineChangeExecutionGate(baseline);
        var repairGate = new BaselineRepairExecutionGate(baseline);

        var change = await changeGate.EnsureCanExecuteAsync("change.test", ChangeExecutionIntent.Apply);
        var repair = await repairGate.EnsureCanExecuteAsync("repair.test", RepairExecutionIntent.Apply);

        Assert.True(change.IsAllowed);
        Assert.True(repair.IsAllowed);
        Assert.Equal(2, baseline.EnsureCalls);
    }

    [Fact]
    public async Task ChangeAndRepairRestore_BypassApplyBaselinePolicy()
    {
        var baseline = new FakeBaselineGate(BlockedDecision());
        var changeGate = new BaselineChangeExecutionGate(baseline);
        var repairGate = new BaselineRepairExecutionGate(baseline);

        var change = await changeGate.EnsureCanExecuteAsync("change.test", ChangeExecutionIntent.Restore);
        var repair = await repairGate.EnsureCanExecuteAsync("repair.test", RepairExecutionIntent.Restore);

        Assert.True(change.IsAllowed);
        Assert.True(repair.IsAllowed);
        Assert.Equal(0, baseline.EnsureCalls);
    }

    [Fact]
    public async Task ManagedAutostart_AcceptsRiskAcknowledgementWithoutVerifiedPackage()
    {
        var baseline = new FakeBaselineGate(AcknowledgedDecision());
        var gate = new BaselineManagedAutostartGate(baseline);
        var plan = new ManagedAutostartPlan(
            Guid.NewGuid(),
            new ManagedAutostartIdentity(ManagedAutostartKind.RegistryRun, "CurrentUser", "test"),
            ManagedAutostartAction.Disable,
            "snapshot",
            "plan",
            DateTimeOffset.UtcNow.AddMinutes(1),
            "risk",
            true,
            false);

        var allowed = await gate.AuthorizeBaselineAsync(plan, CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task SnapshotRestore_RemainsAvailableWithoutFirstBaseline()
    {
        var gate = new BaselineSnapshotRestoreAuthorizationGate();
        var plan = new SnapshotRestorePlan(
            1,
            Guid.NewGuid(),
            "snapshot-1",
            SnapshotRestoreKind.Normal,
            "identity",
            Array.Empty<SnapshotRestoreStepPlan>(),
            "plan",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1));

        var decision = await gate.AuthorizeAsync(plan, CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal("snapshot.recovery-intent-authorized", decision.ReasonCode);
    }

    [Fact]
    public void NetworkAndPowerAdapters_UseSharedProceedDecisionInsteadOfCompletedStateLock()
    {
        var root = FindRepositoryRoot();
        var network = File.ReadAllText(Path.Combine(root,
            "GameOptimizer.Shell", "Services", "Baseline", "BaselineNetworkResetAuthorizationGate.cs"));
        var power = File.ReadAllText(Path.Combine(root,
            "GameOptimizer.Shell", "Services", "PowerManagement", "BaselinePowerGate.cs"));

        Assert.Contains("decision.CanProceed", network, StringComparison.Ordinal);
        Assert.Contains("decision.AuthorizationEvidenceId", network, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstRunBaselineState.CompletedVerified", network, StringComparison.Ordinal);
        Assert.Contains("decision.CanProceed", power, StringComparison.Ordinal);
        Assert.Contains("decision.AuthorizationEvidenceId", power, StringComparison.Ordinal);
        Assert.DoesNotContain("FirstRunBaselineState.CompletedVerified", power, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupAndPromptContract_AreNonBlockingAndExplainRecoveryLimits()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "App.xaml.cs"));
        var dialog = File.ReadAllText(Path.Combine(root,
            "GameOptimizer.Shell", "Views", "FirstRunBaselineDialog.xaml.cs"));
        var prompt = File.ReadAllText(Path.Combine(root,
            "GameOptimizer.Shell", "Services", "WpfBaselineRiskAcknowledgementPrompt.cs"));
        var onboardingStart = app.IndexOf(
            "private async Task ShowFirstProtectionOnboardingIfNeededAsync",
            StringComparison.Ordinal);
        var onboardingEnd = app.IndexOf("private async Task ShowReleaseNoticeIfNeededAsync", onboardingStart, StringComparison.Ordinal);
        var onboarding = app[onboardingStart..onboardingEnd];

        Assert.Contains("IBaselineRiskAcknowledgementPrompt, WpfBaselineRiskAcknowledgementPrompt", app, StringComparison.Ordinal);
        Assert.Contains("dialog.Show();", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogResult =", dialog, StringComparison.Ordinal);
        Assert.Contains("继续只授权本次操作", prompt, StringComparison.Ordinal);
        Assert.Contains("不会跳过管理员权限", prompt, StringComparison.Ordinal);
        Assert.Contains("恢复材料、隔离区、撤销入口或现有系统还原点", File.ReadAllText(Path.Combine(root,
            "GameOptimizer.Shell", "Services", "Baseline", "BaselineGateService.cs")), StringComparison.Ordinal);
    }

    private static BaselineGateDecision AcknowledgedDecision() => new(
        BaselineGateDecisionKind.AllowedWithRiskAcknowledgement,
        "risk acknowledged",
        new BaselineSafetyState { FirstRunState = FirstRunBaselineState.NotStarted },
        new string('E', 64));

    private static BaselineGateDecision BlockedDecision() => new(
        BaselineGateDecisionKind.BlockedRiskNotAcknowledged,
        "risk declined",
        new BaselineSafetyState { FirstRunState = FirstRunBaselineState.NotStarted });

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GameOptimizer.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class FakeBaselineGate : IBaselineGateService
    {
        private readonly BaselineGateDecision _decision;
        public FakeBaselineGate(BaselineGateDecision decision) => _decision = decision;
        public int EnsureCalls { get; private set; }

        public Task<BaselineGateDecision> EnsureCanOptimizeAsync(string entryPoint, CancellationToken ct = default)
        {
            EnsureCalls++;
            return Task.FromResult(_decision);
        }

        public Task<BaselineSafetyState> GetStateAsync(CancellationToken ct = default) => Task.FromResult(_decision.State);
        public Task SetReadonlyModeAsync(bool isReadonly, string entryPoint, string? reason = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkBaselineCompletedVerifiedAsync(string entryPoint, string packageId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkBaselineFailureAsync(string entryPoint, FirstRunBaselineState state, string? reason = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
