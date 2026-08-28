using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Tests.ViewModels;

public sealed class BackupTimelineFilterTests
{
    [Fact]
    public void TimelineFilterChangesOnlyTheOwnedBackupTimeline()
    {
        var viewModel = new BackupManagerViewModel();

        viewModel.SelectTimelineFilterCommand.Execute("Ordinary");
        Assert.Equal(BackupTimelineFilter.OrdinarySnapshots, viewModel.TimelineFilter);
        Assert.True(viewModel.IsOrdinaryTimelineSelected);
        Assert.Equal("普通快照时间线", viewModel.TimelineTitle);
        Assert.Equal("暂无普通快照", viewModel.TimelineEmptyTitle);

        viewModel.SelectTimelineFilterCommand.Execute("Baseline");
        Assert.Equal(BackupTimelineFilter.FirstRunBaseline, viewModel.TimelineFilter);
        Assert.True(viewModel.IsBaselineTimelineSelected);
        Assert.Equal("首次安全基线", viewModel.TimelineTitle);
        Assert.Equal("暂无首次安全基线", viewModel.TimelineEmptyTitle);

        viewModel.SelectTimelineFilterCommand.Execute("All");
        Assert.Equal(BackupTimelineFilter.All, viewModel.TimelineFilter);
        Assert.True(viewModel.IsAllTimelineSelected);
        Assert.Equal("恢复时间线", viewModel.TimelineTitle);
    }
}
