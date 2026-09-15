using GameOptimizer.Core.FileShellSafety;

namespace GameOptimizer.Tests.FileShellSafety;

public sealed class FileShellSafetyOwnerTests
{
    [Theory]
    [InlineData(FileShellMutationKind.PermanentDelete)]
    [InlineData(FileShellMutationKind.ExplorerRestart)]
    [InlineData(FileShellMutationKind.ShellStateReset)]
    [InlineData(FileShellMutationKind.AccessControlMutation)]
    public async Task HighRiskNonQuarantineMutationsAreDeniedBeforeInspection(FileShellMutationKind kind)
    {
        var inspector = new FakeInspector(ValidInspection("C:\\owned\\entry.tmp"));
        var owner = new FileShellSafetyOwner(inspector);

        var decision = await owner.EvaluateAsync(new FileShellSafetyRequest("test", kind), default);

        Assert.Equal(FileShellSafetyStatus.PolicyDenied, decision.Status);
        Assert.False(decision.Allowed);
        Assert.Equal(0, inspector.CallCount);
    }

    [Theory]
    [InlineData("invalid", FileShellSafetyStatus.InvalidPath)]
    [InlineData("wildcard", FileShellSafetyStatus.WildcardDenied)]
    [InlineData("root", FileShellSafetyStatus.RootPathDenied)]
    [InlineData("outside", FileShellSafetyStatus.OutsideAllowlist)]
    [InlineData("reparse", FileShellSafetyStatus.ReparsePointDenied)]
    [InlineData("hash", FileShellSafetyStatus.PreviewHashRequired)]
    [InlineData("baseline", FileShellSafetyStatus.BaselineRequired)]
    [InlineData("drift", FileShellSafetyStatus.DriftBlocked)]
    [InlineData("manifest", FileShellSafetyStatus.ManifestRequired)]
    [InlineData("restore", FileShellSafetyStatus.RestoreUnproven)]
    public async Task QuarantinePreparationFailsAtFirstMissingGate(string variant, FileShellSafetyStatus expected)
    {
        var inspection = variant switch
        {
            "invalid" => ValidInspection("") with { IsPathSyntaxValid = false },
            "wildcard" => ValidInspection("C:\\owned\\*.tmp") with { ContainsWildcard = true },
            "root" => ValidInspection("C:\\") with { IsRootPath = true },
            "outside" => ValidInspection("C:\\outside\\item.tmp") with { IsWithinAllowlistedRoot = false },
            "reparse" => ValidInspection("C:\\owned\\link\\item.tmp") with { IsReparsePoint = true },
            "hash" => ValidInspection("C:\\owned\\item.tmp") with { PreviewSha256 = null },
            "baseline" => ValidInspection("C:\\owned\\item.tmp") with { BaselineId = null },
            "drift" => ValidInspection("C:\\owned\\item.tmp") with { IsStateCurrent = false },
            "manifest" => ValidInspection("C:\\owned\\item.tmp") with { HasDurableQuarantineManifest = false },
            "restore" => ValidInspection("C:\\owned\\item.tmp") with { HasRestoreProof = false },
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        var owner = new FileShellSafetyOwner(new FakeInspector(inspection));

        var decision = await owner.EvaluateAsync(
            new FileShellSafetyRequest("test", FileShellMutationKind.QuarantineFiles, inspection.CanonicalPath, "confirmed"),
            default);

        Assert.Equal(expected, decision.Status);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task QuarantineRequiresExplicitConfirmation()
    {
        var inspection = ValidInspection("C:\\owned\\item.tmp");
        var owner = new FileShellSafetyOwner(new FakeInspector(inspection));

        var decision = await owner.EvaluateAsync(
            new FileShellSafetyRequest("test", FileShellMutationKind.QuarantineFiles, inspection.CanonicalPath), default);

        Assert.Equal(FileShellSafetyStatus.ConfirmationRequired, decision.Status);
    }

    [Fact]
    public async Task FullyPreparedLegacyRequestStillFailsWhenExecutorIsUnavailable()
    {
        var inspection = ValidInspection("C:\\owned\\item.tmp");
        var owner = new FileShellSafetyOwner(new FakeInspector(inspection));

        var decision = await owner.EvaluateAsync(
            new FileShellSafetyRequest("test", FileShellMutationKind.QuarantineFiles, inspection.CanonicalPath, "confirmed"),
            default);

        Assert.Equal(FileShellSafetyStatus.QuarantineOwnerUnavailable, decision.Status);
        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task CancellationTimeoutAndInspectionFailureAreDistinct()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var normal = new FileShellSafetyOwner(new FakeInspector(ValidInspection("C:\\owned\\item.tmp")));
        Assert.Equal(FileShellSafetyStatus.Cancelled,
            (await normal.EvaluateAsync(Request(), cancelled.Token)).Status);

        var timeout = new FileShellSafetyOwner(new BlockingInspector(), timing: new(TimeSpan.FromMilliseconds(20)));
        Assert.Equal(FileShellSafetyStatus.TimedOut,
            (await timeout.EvaluateAsync(Request(), default)).Status);

        var failing = new FileShellSafetyOwner(new ThrowingInspector());
        Assert.Equal(FileShellSafetyStatus.InspectionFailed,
            (await failing.EvaluateAsync(Request(), default)).Status);
    }

    [Fact]
    public async Task EvaluationDoesNotMutateSelfOwnedTemporaryFile()
    {
        var parent = Path.Combine(Path.GetTempPath(), "yuqi-file-shell-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, "owned.tmp");
        await File.WriteAllTextAsync(path, "owned-test-content");
        try
        {
            var inspection = ValidInspection(Path.GetFullPath(path));
            var owner = new FileShellSafetyOwner(new FakeInspector(inspection));
            var decision = await owner.EvaluateAsync(
                new FileShellSafetyRequest("temp-owned", FileShellMutationKind.QuarantineFiles, path, "confirmed"), default);

            Assert.Equal(FileShellSafetyStatus.QuarantineOwnerUnavailable, decision.Status);
            Assert.True(File.Exists(path));
            Assert.Equal("owned-test-content", await File.ReadAllTextAsync(path));
        }
        finally
        {
            var canonicalParent = Path.GetFullPath(parent);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), canonicalParent, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(canonicalParent)) Directory.Delete(canonicalParent, recursive: true);
        }
    }

    private static FileShellSafetyRequest Request() =>
        new("test", FileShellMutationKind.QuarantineFiles, "C:\\owned\\item.tmp", "confirmed");

    private static FileShellSafetyInspection ValidInspection(string path) =>
        new(path, true, false, false, true, false, "ABCDEF", "baseline-1", true, true, true);

    private sealed class FakeInspector(FileShellSafetyInspection inspection) : IFileShellSafetyInspector
    {
        public int CallCount { get; private set; }
        public Task<FileShellSafetyInspection> InspectAsync(FileShellSafetyRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(inspection);
        }
    }

    private sealed class BlockingInspector : IFileShellSafetyInspector
    {
        public async Task<FileShellSafetyInspection> InspectAsync(FileShellSafetyRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }

    private sealed class ThrowingInspector : IFileShellSafetyInspector
    {
        public Task<FileShellSafetyInspection> InspectAsync(FileShellSafetyRequest request, CancellationToken cancellationToken) =>
            throw new IOException("simulated");
    }
}
