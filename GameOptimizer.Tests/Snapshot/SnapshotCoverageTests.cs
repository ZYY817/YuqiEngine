using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Tests.Snapshot;

public sealed class SnapshotCoverageTests
{
    [Fact]
    public async Task Coverage_report_is_explicitly_partial_for_the_current_catalog()
    {
        using var scope = new TempScope();
        var service = new SnapshotService(
            new DenyGate(), null, null,
            new BuiltInOptimizationOperationProvider(),
            null, null, scope.Root);

        var report = await service.GetCoverageReportAsync();

        Assert.Equal(212, report.CatalogOperationCount);
        Assert.Equal(207, report.TypedExecutableOperationCount);
        Assert.Equal(9, report.CapturedOperationCount);
        Assert.Equal(15, report.CapturedScalarValueCount);
        Assert.Equal(5, report.ManagedFailClosedOperationCount);
        Assert.Equal(203, report.UncapturedOperationIds.Count);
        Assert.False(report.IsComplete);
    }

    private sealed class TempScope : IDisposable
    {
        public TempScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "yuqi-snapshot-coverage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        public string Root { get; }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }

    private sealed class DenyGate : ISnapshotRestoreAuthorizationGate
    {
        public Task<SnapshotRestoreAuthorizationDecision> AuthorizeAsync(
            SnapshotRestorePlan plan, CancellationToken cancellationToken) =>
            Task.FromResult(new SnapshotRestoreAuthorizationDecision(false, "test-denied", "test-denied"));
    }
}
