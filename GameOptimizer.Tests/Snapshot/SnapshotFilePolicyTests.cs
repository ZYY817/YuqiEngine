using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Tests.Snapshot;

public sealed class SnapshotFilePolicyTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("nested/id")]
    [InlineData("nested\\id")]
    [InlineData("C:\\Windows\\win")]
    [InlineData("%2e%2e%2fescape")]
    [InlineData("")]
    [InlineData(" ")]
    public void EverySnapshotIdPathApiRejectsTraversalAndNonCanonicalIds(string hostileId)
    {
        var policy = new SnapshotFilePolicy(Path.Combine(Path.GetTempPath(), $"yuqi-policy-{Guid.NewGuid():N}"));

        Assert.Throws<InvalidDataException>(() => policy.GetSnapshotPath(hostileId));
        Assert.Throws<InvalidDataException>(() => policy.GetLegacyRegistryDirectory(hostileId));
    }

    [Fact]
    public void CanonicalContainmentRejectsSiblingPrefixConfusion()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"yuqi-policy-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "snapshots");
        var sibling = Path.Combine(parent, "snapshots-evil", "deadbeef.json");
        var policy = new SnapshotFilePolicy(root);

        Assert.Throws<InvalidOperationException>(() => policy.EnsureContained(sibling));
    }

    [Fact]
    public void CatalogFilenameParsingRejectsInvalidOrNestedSnapshotNames()
    {
        var root = Path.Combine(Path.GetTempPath(), $"yuqi-policy-{Guid.NewGuid():N}");
        var policy = new SnapshotFilePolicy(root);

        Assert.False(policy.TryGetIdFromSnapshotFile(Path.Combine(root, "not an id.json"), out _, out var invalid));
        Assert.Equal("snapshot.path.invalid", invalid);
        Assert.False(policy.TryGetIdFromSnapshotFile(Path.Combine(root, "nested", "deadbeef.json"), out _, out var nested));
        Assert.Equal("snapshot.path.filename-mismatch", nested);
    }

    [Theory]
    [InlineData("not-hex")]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void ChecksumComparisonFailsClosedForMalformedOrWrongLengthValues(string supplied)
    {
        const string valid = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        Assert.False(SnapshotFilePolicy.FixedChecksumEquals(supplied, valid));
        Assert.False(SnapshotFilePolicy.FixedChecksumEquals(valid, supplied));
    }

    [Fact]
    public void ConcurrentPathResolutionIsDeterministicAndContained()
    {
        var root = Path.Combine(Path.GetTempPath(), $"yuqi-policy-{Guid.NewGuid():N}");
        var policy = new SnapshotFilePolicy(root);

        var paths = Enumerable.Range(0, 64)
            .AsParallel()
            .Select(_ => policy.GetSnapshotPath("deadbeef"))
            .ToArray();

        Assert.All(paths, path => Assert.Equal(Path.Combine(root, "deadbeef.json"), path));
        Assert.Single(paths.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void LegacyDirectoryLinkEscapingRootIsRejectedWhenLinksAreAvailable()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"yuqi-policy-{Guid.NewGuid():N}");
        var root = Path.Combine(parent, "snapshots");
        var outside = Path.Combine(parent, "outside");
        var linkedSnapshot = Path.Combine(root, "deadbeef");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        try
        {
            Directory.CreateSymbolicLink(linkedSnapshot, outside);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var policy = new SnapshotFilePolicy(root);
        Assert.Throws<InvalidOperationException>(() => policy.GetLegacyRegistryDirectory("deadbeef"));
    }
}
