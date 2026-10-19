using GameOptimizer.Core.SystemRestore;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Tests.ViewModels;

public sealed class RestoreTimelineFilterTests
{
    [Fact]
    public void TimelineFilterKeepsSystemAndSoftwareRecoveryBoundariesExplicit()
    {
        var viewModel = new RestorePointViewModel(
            new NoOpRestorePointOwner(),
            new NoOpRestorePointConfirmationPrompt());

        viewModel.SelectTimelineFilterCommand.Execute("System");
        Assert.Equal(RestoreTimelineFilter.SystemRestorePoints, viewModel.TimelineFilter);
        Assert.True(viewModel.IsSystemTimelineSelected);
        Assert.Equal("系统还原点时间线", viewModel.TimelineTitle);

        viewModel.SelectTimelineFilterCommand.Execute("Software");
        Assert.Equal(RestoreTimelineFilter.SoftwareRecovery, viewModel.TimelineFilter);
        Assert.True(viewModel.IsSoftwareTimelineSelected);
        Assert.Equal("软件恢复记录", viewModel.TimelineTitle);
        Assert.Equal("暂无软件恢复记录", viewModel.TimelineEmptyTitle);

        viewModel.SelectTimelineFilterCommand.Execute("All");
        Assert.Equal(RestoreTimelineFilter.All, viewModel.TimelineFilter);
        Assert.True(viewModel.IsAllTimelineSelected);
        Assert.Equal("恢复时间线", viewModel.TimelineTitle);
    }

    private sealed class NoOpRestorePointOwner : ISystemRestorePointOwner
    {
        private static SystemRestorePointWorkflowResult Unsupported(SystemRestorePointPhase phase) =>
            new(false, false, SystemRestorePointWorkflowStatus.Unsupported, phase, "测试替身不执行系统操作");

        public Task<SystemRestorePointWorkflowResult> InspectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Unsupported(SystemRestorePointPhase.Inspect));

        public Task<SystemRestorePointWorkflowResult> PrepareCreateAsync(
            SystemRestorePointCreateRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unsupported(SystemRestorePointPhase.Prepare));

        public SystemRestorePointConfirmationToken? ConfirmCreate(
            SystemRestorePointCreatePlan plan,
            SystemRestorePointConfirmationEvidence evidence) => null;

        public Task<SystemRestorePointWorkflowResult> ExecuteCreateAsync(
            SystemRestorePointCreatePlan plan,
            SystemRestorePointConfirmationToken confirmation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unsupported(SystemRestorePointPhase.Execute));

        public Task<IReadOnlyList<SystemRestorePointWorkflowResult>> ReconcileInterruptedCreatesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SystemRestorePointWorkflowResult>>([]);

        public Task<SystemRestorePointWorkflowResult> ReconcileInterruptedCreateAsync(
            Guid planId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unsupported(SystemRestorePointPhase.Reconcile));

        public Task<SystemRestorePointWorkflowResult> ResolveInterruptedCreateAsync(
            SystemRestorePointManualResolution resolution,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unsupported(SystemRestorePointPhase.Reconcile));

        public Task<SystemRestorePointWorkflowResult> PrepareSystemRestoreAsync(
            SystemRestorePointIdentity target,
            string source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unsupported(SystemRestorePointPhase.Prepare));

        public Task<SystemRestorePointWorkflowResult> ExecuteSystemRestoreAsync(
            SystemRestoreExecutionPlan plan,
            SystemRestorePointConfirmationToken? confirmation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unsupported(SystemRestorePointPhase.Execute));
    }

    private sealed class NoOpRestorePointConfirmationPrompt : ISystemRestorePointConfirmationPrompt
    {
        public Task<bool> ConfirmCreateAsync(SystemRestorePointCreatePlan plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
