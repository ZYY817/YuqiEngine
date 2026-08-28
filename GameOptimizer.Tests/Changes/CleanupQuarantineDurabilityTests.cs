// File: GameOptimizer.Tests/Changes/CleanupQuarantineDurabilityTests.cs
using GameOptimizer.Core.Services.SystemCleanup;

namespace GameOptimizer.Tests.Changes;

public sealed class CleanupQuarantineDurabilityTests
{
    [Fact]
    public async Task PreviewContentHashBlocksSameSizeAndTimestampDriftAfterAuthorization()
    {
        using var fixture = Fixture.Create();
        string source = fixture.Write("drift.bin", "AAAA");
        var item = fixture.Item();
        var service = SystemCleanupService.CreateForTesting(new MutatingGate(() =>
        {
            DateTime timestamp = File.GetLastWriteTimeUtc(source);
            File.WriteAllText(source, "BBBB");
            File.SetLastWriteTimeUtc(source, timestamp);
        }), new FileSystemCleanupQuarantine(fixture.Quarantine), [item]);

        CleanupPreview preview = await service.CreatePreviewAsync(item);
        CleanupPreviewConfirmation confirmation = CleanupPreviewConfirmation.Create(preview, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        CleanupResult result = await service.CleanPreviewAsync(item, preview, confirmation);

        CleanupFileResult blocked = Assert.Single(result.FileResults);
        Assert.Equal(CleanupFileOutcome.ChangedSincePreview, blocked.Outcome);
        Assert.Equal("cleanup.preview-content-drift", blocked.ReasonCode);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ResourceLeaseSerializesDifferentInstancesAndReleasesAfterDispose()
    {
        using var fixture = Fixture.Create();
        await using CleanupQuarantineResourceLease first = Assert.IsType<CleanupQuarantineResourceLease>(
            await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(fixture.Quarantine, "cleanup.resource.test", TimeSpan.FromSeconds(1), default));

        CleanupQuarantineResourceLease? blocked = await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(
            fixture.Quarantine,
            "cleanup.resource.test",
            TimeSpan.FromMilliseconds(30),
            default);
        Assert.Null(blocked);

        await first.DisposeAsync();
        CleanupQuarantineResourceLease? second = await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(
            fixture.Quarantine,
            "cleanup.resource.test",
            TimeSpan.FromSeconds(1),
            default);
        Assert.NotNull(second);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task ResourceLeaseCancellationDoesNotLeakAndReplayCanAcquire()
    {
        using var fixture = Fixture.Create();
        await using CleanupQuarantineResourceLease first = Assert.IsType<CleanupQuarantineResourceLease>(
            await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(fixture.Quarantine, "cleanup.resource.cancel", TimeSpan.FromSeconds(1), default));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(
                fixture.Quarantine,
                "cleanup.resource.cancel",
                TimeSpan.FromSeconds(1),
                cancellation.Token));

        await first.DisposeAsync();
        await using CleanupQuarantineResourceLease replay = Assert.IsType<CleanupQuarantineResourceLease>(
            await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(fixture.Quarantine, "cleanup.resource.cancel", TimeSpan.FromSeconds(1), default));
    }

    [Fact]
    public async Task ResourceLeaseIsReleasedWhenOwnerExitsThroughException()
    {
        using var fixture = Fixture.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using CleanupQuarantineResourceLease lease = Assert.IsType<CleanupQuarantineResourceLease>(
                await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(fixture.Quarantine, "cleanup.resource.exception", TimeSpan.FromSeconds(1), default));
            throw new InvalidOperationException("test owner failure");
        });

        await using CleanupQuarantineResourceLease replay = Assert.IsType<CleanupQuarantineResourceLease>(
            await CleanupQuarantineManifestStore.TryAcquireResourceLeaseAsync(fixture.Quarantine, "cleanup.resource.exception", TimeSpan.FromSeconds(1), default));
    }

    [Fact]
    public async Task CrossInstanceCleanupAllowsOnlyOneVerifiedQuarantineForTheSameResource()
    {
        using var fixture = Fixture.Create();
        fixture.Write("race.bin", "recoverable");
        var firstItem = fixture.Item();
        var secondItem = fixture.Item();
        var first = SystemCleanupService.CreateForTesting(new AllowGate(), new FileSystemCleanupQuarantine(fixture.Quarantine), [firstItem]);
        var second = SystemCleanupService.CreateForTesting(new AllowGate(), new FileSystemCleanupQuarantine(fixture.Quarantine), [secondItem]);
        CleanupPreview firstPreview = await first.CreatePreviewAsync(firstItem);
        CleanupPreview secondPreview = await second.CreatePreviewAsync(secondItem);
        CleanupPreviewConfirmation firstConfirmation = CleanupPreviewConfirmation.Create(firstPreview, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        CleanupPreviewConfirmation secondConfirmation = CleanupPreviewConfirmation.Create(secondPreview, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        CleanupResult[] results = await Task.WhenAll(
            first.CleanPreviewAsync(firstItem, firstPreview, firstConfirmation),
            second.CleanPreviewAsync(secondItem, secondPreview, secondConfirmation));

        CleanupFileResult[] items = results.SelectMany(result => result.FileResults).ToArray();
        Assert.Single(items, item => item.Outcome == CleanupFileOutcome.Quarantined);
        Assert.Single(items, item => item.Outcome is CleanupFileOutcome.ChangedSincePreview or CleanupFileOutcome.Locked);
    }

    [Fact]
    public async Task ReconcileAfterInterruptedQuarantineReceiptVerifiesMovedContent()
    {
        using var fixture = Fixture.Create();
        string source = fixture.Write("move.bin", "recoverable");
        CleanupQuarantineEntry entry = await fixture.QuarantineAsync(source);
        CleanupQuarantineManifest moving = await CleanupQuarantineManifestStore.TransitionAsync(
            entry.ManifestPath,
            entry.Manifest,
            CleanupQuarantineState.MovePrepared,
            "test.interrupted",
            "simulated receipt interruption",
            true,
            Guid.NewGuid(),
            default);
        Assert.Equal(CleanupQuarantineState.MovePrepared, moving.State);

        CleanupQuarantineReconciliationResult reconciled = await new CleanupQuarantineManager(fixture.Quarantine).ReconcileAsync();

        Assert.Contains(reconciled.Items, item => item.Outcome == CleanupQuarantineOutcome.ReconciledQuarantined);
        CleanupQuarantineEntry current = Assert.IsType<CleanupQuarantineEntry>(await CleanupQuarantineManifestStore.TryReadVerifiedAsync(fixture.Quarantine, entry.ManifestPath, default));
        Assert.Equal(CleanupQuarantineState.QuarantinedVerified, current.Manifest.State);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(current.Manifest.QuarantinePath));
    }

    [Fact]
    public async Task ReconcileAfterInterruptedRestoreVerifiesOriginalTarget()
    {
        using var fixture = Fixture.Create();
        string source = fixture.Write("restore.bin", "recoverable");
        CleanupQuarantineEntry entry = await fixture.QuarantineAsync(source);
        CleanupQuarantineManifest claimed = await CleanupQuarantineManifestStore.TransitionAsync(
            entry.ManifestPath,
            entry.Manifest,
            CleanupQuarantineState.RestoreClaimed,
            "test.restore-claimed",
            "simulated process exit after restore move",
            false,
            Guid.NewGuid(),
            default);
        File.Move(claimed.QuarantinePath, source, overwrite: false);

        CleanupQuarantineReconciliationResult reconciled = await new CleanupQuarantineManager(fixture.Quarantine).ReconcileAsync();

        Assert.Contains(reconciled.Items, item => item.Outcome == CleanupQuarantineOutcome.ReconciledRestored);
        CleanupQuarantineEntry current = Assert.IsType<CleanupQuarantineEntry>(await CleanupQuarantineManifestStore.TryReadVerifiedAsync(fixture.Quarantine, entry.ManifestPath, default));
        Assert.Equal(CleanupQuarantineState.RestoredVerified, current.Manifest.State);
        Assert.Equal("recoverable", File.ReadAllText(source));
    }

    [Fact]
    public async Task ReconcileAfterInterruptedRetentionRetainsRestoreCapability()
    {
        using var fixture = Fixture.Create();
        string source = fixture.Write("retention.bin", "recoverable");
        CleanupQuarantineEntry entry = await fixture.QuarantineAsync(source);
        CleanupQuarantineManifest claimed = await CleanupQuarantineManifestStore.TransitionAsync(
            entry.ManifestPath,
            entry.Manifest,
            CleanupQuarantineState.RetentionClaimed,
            "test.retention-claimed",
            "simulated process exit after retention move",
            false,
            Guid.NewGuid(),
            default);
        string batch = Path.GetDirectoryName(entry.ManifestPath)!;
        string held = Path.Combine(fixture.Quarantine, "RetentionHold", claimed.OperationId);
        Directory.CreateDirectory(Path.GetDirectoryName(held)!);
        Directory.Move(batch, held);
        string heldManifest = Path.Combine(held, Path.GetFileName(entry.ManifestPath));

        CleanupQuarantineReconciliationResult reconciled = await new CleanupQuarantineManager(fixture.Quarantine).ReconcileAsync();

        Assert.Contains(reconciled.Items, item => item.Outcome == CleanupQuarantineOutcome.ReconciledRetention);
        CleanupQuarantineEntry current = Assert.IsType<CleanupQuarantineEntry>(await CleanupQuarantineManifestStore.TryReadVerifiedAsync(fixture.Quarantine, heldManifest, default));
        Assert.Equal(CleanupQuarantineState.RetentionStaged, current.Manifest.State);
        Assert.True(File.Exists(current.Manifest.QuarantinePath));
        CleanupQuarantinePreview restore = await new CleanupQuarantineManager(fixture.Quarantine).CreateRestorePreviewAsync();
        Assert.Single(restore.Entries);
    }

    [Fact]
    public void CrossVolumeMovesAreExplicitlyBlockedByVolumeIdentity()
    {
        Assert.False(CleanupQuarantineManifestStore.AreOnSameVolume(@"C:\source.bin", @"D:\quarantine\source.bin"));
    }

    private sealed class MutatingGate(Action action) : ICleanupAuthorizationGate
    {
        public Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken)
        {
            action();
            return Task.FromResult(new CleanupAuthorizationDecision(true, "test.allowed", "allowed"));
        }
    }

    private sealed class AllowGate : ICleanupAuthorizationGate
    {
        public Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken) =>
            Task.FromResult(new CleanupAuthorizationDecision(true, "test.allowed", "allowed"));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string TestBase = Path.Combine(Path.GetTempPath(), "yuqi-cleanup-durable-tests");

        private Fixture(string parent, string root, string quarantine)
        {
            Parent = parent;
            Root = root;
            Quarantine = quarantine;
        }

        public string Parent { get; }
        public string Root { get; }
        public string Quarantine { get; }

        public static Fixture Create()
        {
            string parent = Path.GetFullPath(Path.Combine(TestBase, Guid.NewGuid().ToString("N")));
            EnsureContained(parent);
            string root = Path.Combine(parent, "allowed");
            string quarantine = Path.Combine(parent, "quarantine");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(quarantine);
            return new Fixture(parent, root, quarantine);
        }

        public CleanupItem Item() => new() { Id = "durable", TargetPaths = [Root], Patterns = ["*"], IncludeSubdirectories = true, IsSelected = true };

        public string Write(string name, string content)
        {
            string path = Path.GetFullPath(Path.Combine(Root, name));
            EnsureUnder(Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public async Task<CleanupQuarantineEntry> QuarantineAsync(string source)
        {
            FileInfo info = new(source);
            string hash = await CleanupQuarantineManifestStore.ComputeFileSha256Async(source, default);
            var entry = new CleanupPreviewEntry(source, Root, info.Length, info.LastWriteTimeUtc, hash, Guid.NewGuid());
            CleanupQuarantineMove move = await new FileSystemCleanupQuarantine(Quarantine).MoveWithReceiptAsync(entry, "durable", Guid.NewGuid().ToString("N"), default);
            return Assert.IsType<CleanupQuarantineEntry>(await CleanupQuarantineManifestStore.TryReadVerifiedAsync(Quarantine, move.ManifestPath!, default));
        }

        public void Dispose()
        {
            EnsureContained(Parent);
            if (Directory.Exists(Parent)) Directory.Delete(Parent, recursive: true);
        }

        private static void EnsureContained(string path) => EnsureUnder(TestBase, path);

        private static void EnsureUnder(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe test path.");
        }
    }
}
