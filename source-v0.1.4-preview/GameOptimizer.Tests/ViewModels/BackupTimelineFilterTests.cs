using GameOptimizer.Shell.ViewModels;
using GameOptimizer.Core.Services.Snapshot;

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
        Assert.Equal("备份记录", viewModel.TimelineTitle);

        viewModel.SelectTimelineFilterCommand.Execute("Packages");
        Assert.Equal(BackupTimelineFilter.ComprehensivePackages, viewModel.TimelineFilter);
        Assert.True(viewModel.IsPackageTimelineSelected);
        Assert.Equal("完整备份包", viewModel.TimelineTitle);
        Assert.Equal("暂无完整备份包", viewModel.TimelineEmptyTitle);
    }

    [Fact]
    public void UnifiedTimelineMergesBaselineSnapshotsAndVerifiedPackages()
    {
        var viewModel = new BackupManagerViewModel
        {
            IsLoading = true,
            HasFirstRunBaseline = true
        };
        viewModel.Snapshots.Add(new SnapshotInfo
        {
            Id = "snapshot-1",
            Name = "Before optimization",
            CreatedAt = DateTime.Now.AddHours(-2),
            SettingsCount = 9
        });
        var manifest = new ComprehensiveBackupManifest(
            FormatVersion: 1,
            PackageId: "package-1",
            CreatedUtc: DateTimeOffset.Now.AddHours(-1),
            UserSidSha256: "sid",
            MachineIdentitySha256: "machine",
            WindowsVersion: "Windows",
            WindowsBuild: 1,
            Architecture: "x64",
            ProcessorCount: 8,
            Artifacts: Array.Empty<ComprehensiveBackupArtifact>(),
            TypedCoverage: new SnapshotCoverageReport(0, 0, 0, 0, 0, Array.Empty<string>()),
            OperationStates: Array.Empty<ComprehensiveBackupOperationState>(),
            IsComplete: true);
        viewModel.ComprehensiveBackups.Add(new ComprehensiveBackupPackageInfo(
            "package-1",
            "C:\\backups\\package-1",
            manifest,
            IntegrityVerified: true));
        viewModel.IsLoading = false;

        Assert.Equal(3, viewModel.TimelineEntries.Count);
        Assert.Equal(BackupTimelineEntryKind.Baseline, viewModel.TimelineEntries[0].Kind);
        Assert.Equal(BackupTimelineEntryKind.Package, viewModel.TimelineEntries[1].Kind);
        Assert.Equal(BackupTimelineEntryKind.Snapshot, viewModel.TimelineEntries[2].Kind);
        Assert.Equal("今天", viewModel.TimelineEntries[1].GroupLabel);

        viewModel.SelectTimelineFilterCommand.Execute("Ordinary");
        Assert.Collection(
            viewModel.TimelineEntries,
            entry => Assert.Equal(BackupTimelineEntryKind.Snapshot, entry.Kind));

        viewModel.SelectTimelineFilterCommand.Execute("Packages");
        Assert.Collection(
            viewModel.TimelineEntries,
            entry => Assert.Equal(BackupTimelineEntryKind.Package, entry.Kind));
    }
}
