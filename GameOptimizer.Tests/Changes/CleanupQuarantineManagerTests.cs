using GameOptimizer.Core.Services.SystemCleanup;

namespace GameOptimizer.Tests.Changes;

public sealed class CleanupQuarantineManagerTests
{
    [Fact]
    public async Task ManifestedQuarantineRestoresExactContentWithoutOverwrite()
    {
        using var fixture = QuarantineFixture.Create();
        string original = await fixture.QuarantineAsync("restore.bin", "recover me");
        var manager = new CleanupQuarantineManager(fixture.QuarantineRoot);
        CleanupQuarantinePreview preview = await manager.CreateRestorePreviewAsync();
        CleanupQuarantineManifest manifest = Assert.Single(preview.Entries).Manifest;
        Assert.Equal(2, manifest.Version);
        Assert.False(string.IsNullOrWhiteSpace(manifest.ManifestSha256));
        Assert.False(string.IsNullOrWhiteSpace(manifest.ContentSha256));
        Assert.False(string.IsNullOrWhiteSpace(manifest.ResourceKey));
        Assert.NotEqual(Guid.Empty, manifest.PreviewId);
        Assert.Equal(CleanupQuarantineState.QuarantinedVerified, manifest.State);
        Assert.NotEmpty(manifest.Receipts);
        Assert.Equal(original, manifest.OriginalPath);
        Assert.Equal(fixture.AllowedRoot, manifest.OriginalAllowlistedRoot);
        Assert.False(string.IsNullOrWhiteSpace(manifest.OperationId));

        CleanupQuarantineResult result = await manager.RestoreAsync(preview, Confirm(preview));

        Assert.Equal(CleanupQuarantineOutcome.Restored, Assert.Single(result.Items).Outcome);
        Assert.Equal("recover me", File.ReadAllText(original));
        Assert.Empty((await manager.CreateRestorePreviewAsync()).Entries);
    }

    [Fact]
    public async Task ExistingRestoreTargetBlocksWithoutOverwrite()
    {
        using var fixture = QuarantineFixture.Create();
        string original = await fixture.QuarantineAsync("conflict.bin", "old");
        File.WriteAllText(original, "new");
        var manager = new CleanupQuarantineManager(fixture.QuarantineRoot);
        CleanupQuarantinePreview preview = await manager.CreateRestorePreviewAsync();

        CleanupQuarantineResult result = await manager.RestoreAsync(preview, Confirm(preview));

        Assert.Equal(CleanupQuarantineOutcome.TargetConflict, Assert.Single(result.Items).Outcome);
        Assert.Equal("new", File.ReadAllText(original));
        Assert.True(File.Exists(preview.Entries[0].Manifest.QuarantinePath));
    }

    [Fact]
    public async Task ManifestOrContentDriftFailsClosed()
    {
        using var manifestFixture = QuarantineFixture.Create();
        await manifestFixture.QuarantineAsync("manifest.bin", "data");
        var manifestManager = new CleanupQuarantineManager(manifestFixture.QuarantineRoot);
        CleanupQuarantinePreview manifestPreview = await manifestManager.CreateRestorePreviewAsync();
        string manifestJson = File.ReadAllText(manifestPreview.Entries[0].ManifestPath);
        File.WriteAllText(manifestPreview.Entries[0].ManifestPath, manifestJson.Replace("\"itemId\": \"test\"", "\"itemId\": \"tampered\"", StringComparison.Ordinal));

        CleanupQuarantineResult manifestResult = await manifestManager.RestoreAsync(manifestPreview, Confirm(manifestPreview));

        Assert.Equal(CleanupQuarantineOutcome.InvalidManifest, Assert.Single(manifestResult.Items).Outcome);

        using var contentFixture = QuarantineFixture.Create();
        await contentFixture.QuarantineAsync("content.bin", "original");
        var contentManager = new CleanupQuarantineManager(contentFixture.QuarantineRoot);
        CleanupQuarantinePreview contentPreview = await contentManager.CreateRestorePreviewAsync();
        File.AppendAllText(contentPreview.Entries[0].Manifest.QuarantinePath, "changed");

        CleanupQuarantineResult contentResult = await contentManager.RestoreAsync(contentPreview, Confirm(contentPreview));

        Assert.Equal(CleanupQuarantineOutcome.ContentMismatch, Assert.Single(contentResult.Items).Outcome);
    }

    [Fact]
    public async Task RetentionRequiresConfirmationAndOnlyMovesToSecondLevelHold()
    {
        using var fixture = QuarantineFixture.Create();
        await fixture.QuarantineAsync("retention.bin", "keep recoverable");
        var manager = new CleanupQuarantineManager(fixture.QuarantineRoot);
        CleanupQuarantinePreview preview = await manager.CreateRetentionPreviewAsync(TimeSpan.Zero);
        CleanupQuarantineConfirmation wrong = CleanupQuarantineConfirmation.Create(
            new CleanupQuarantinePreview("restore", preview.Entries, preview.CreatedAtUtc),
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(1));

        CleanupQuarantineResult blocked = await manager.StageRetentionAsync(preview, wrong);
        Assert.Equal(CleanupQuarantineOutcome.InvalidManifest, Assert.Single(blocked.Items).Outcome);
        Assert.True(File.Exists(preview.Entries[0].Manifest.QuarantinePath));

        CleanupQuarantinePreview retry = await manager.CreateRetentionPreviewAsync(TimeSpan.Zero);
        CleanupQuarantineResult staged = await manager.StageRetentionAsync(retry, Confirm(retry));

        CleanupQuarantineItemResult result = Assert.Single(staged.Items);
        Assert.Equal(CleanupQuarantineOutcome.StagedForRetention, result.Outcome);
        Assert.NotNull(result.CurrentPath);
        Assert.True(Directory.Exists(result.CurrentPath));
        Assert.True(File.Exists(Directory.EnumerateFiles(result.CurrentPath!, "*retention.bin", SearchOption.TopDirectoryOnly).Single()));
    }

    [Fact]
    public async Task ExpiredConfirmationAndCancellationMoveNothing()
    {
        using var fixture = QuarantineFixture.Create();
        await fixture.QuarantineAsync("blocked.bin", "data");
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var manager = new CleanupQuarantineManager(fixture.QuarantineRoot, new FixedTimeProvider(now));
        CleanupQuarantinePreview expiredPreview = await manager.CreateRestorePreviewAsync();
        CleanupQuarantineConfirmation expired = CleanupQuarantineConfirmation.Create(expiredPreview, now.AddMinutes(-2), TimeSpan.FromMinutes(1));

        CleanupQuarantineResult expiredResult = await manager.RestoreAsync(expiredPreview, expired);
        Assert.Equal(CleanupQuarantineOutcome.InvalidManifest, Assert.Single(expiredResult.Items).Outcome);

        CleanupQuarantinePreview cancelledPreview = await manager.CreateRestorePreviewAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        CleanupQuarantineConfirmation cancelledConfirmation = CleanupQuarantineConfirmation.Create(cancelledPreview, now, TimeSpan.FromMinutes(2));
        CleanupQuarantineResult cancelled = await manager.RestoreAsync(cancelledPreview, cancelledConfirmation, cts.Token);
        Assert.Equal(CleanupQuarantineOutcome.Cancelled, Assert.Single(cancelled.Items).Outcome);
        Assert.True(File.Exists(cancelledPreview.Entries[0].Manifest.QuarantinePath));
    }

    [Fact]
    public async Task RestorePermissionAndLockFailuresArePerItemAndNonDestructive()
    {
        using var permissionFixture = QuarantineFixture.Create();
        await permissionFixture.QuarantineAsync("permission.bin", "data");
        var permissionManager = new CleanupQuarantineManager(
            permissionFixture.QuarantineRoot,
            TimeProvider.System,
            (_, _) => Task.FromException<string>(new UnauthorizedAccessException("denied")));
        CleanupQuarantinePreview permissionPreview = await permissionManager.CreateRestorePreviewAsync();

        CleanupQuarantineResult permissionResult = await permissionManager.RestoreAsync(permissionPreview, Confirm(permissionPreview));

        Assert.Equal(CleanupQuarantineOutcome.PermissionDenied, Assert.Single(permissionResult.Items).Outcome);
        Assert.True(File.Exists(permissionPreview.Entries[0].Manifest.QuarantinePath));

        using var lockFixture = QuarantineFixture.Create();
        await lockFixture.QuarantineAsync("locked.bin", "data");
        var lockManager = new CleanupQuarantineManager(lockFixture.QuarantineRoot);
        CleanupQuarantinePreview lockPreview = await lockManager.CreateRestorePreviewAsync();
        await using FileStream lockStream = new(lockPreview.Entries[0].Manifest.QuarantinePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        CleanupQuarantineResult lockResult = await lockManager.RestoreAsync(lockPreview, Confirm(lockPreview));

        Assert.Equal(CleanupQuarantineOutcome.Locked, Assert.Single(lockResult.Items).Outcome);
        Assert.True(File.Exists(lockPreview.Entries[0].Manifest.QuarantinePath));
    }

    private static CleanupQuarantineConfirmation Confirm(CleanupQuarantinePreview preview) =>
        CleanupQuarantineConfirmation.Create(preview, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class QuarantineFixture : IDisposable
    {
        private static readonly string TestBase = Path.Combine(Path.GetTempPath(), "yuqi-quarantine-manager-tests");
        private QuarantineFixture(string parent, string allowedRoot, string quarantineRoot)
        {
            Parent = parent;
            AllowedRoot = allowedRoot;
            QuarantineRoot = quarantineRoot;
        }

        public string Parent { get; }
        public string AllowedRoot { get; }
        public string QuarantineRoot { get; }

        public static QuarantineFixture Create()
        {
            string parent = Path.GetFullPath(Path.Combine(TestBase, Guid.NewGuid().ToString("N")));
            EnsureContained(parent);
            string allowed = Path.Combine(parent, "allowed");
            string quarantine = Path.Combine(parent, "quarantine");
            Directory.CreateDirectory(allowed);
            Directory.CreateDirectory(quarantine);
            return new(parent, allowed, quarantine);
        }

        public async Task<string> QuarantineAsync(string name, string content)
        {
            string source = Path.Combine(AllowedRoot, name);
            File.WriteAllText(source, content);
            var info = new FileInfo(source);
            string hash = await CleanupQuarantineManifestStore.ComputeFileSha256Async(source, CancellationToken.None);
            var entry = new CleanupPreviewEntry(source, AllowedRoot, info.Length, info.LastWriteTimeUtc, hash, Guid.NewGuid());
            await new FileSystemCleanupQuarantine(QuarantineRoot).MoveAsync(entry, "test", CancellationToken.None);
            return source;
        }

        public void Dispose()
        {
            EnsureContained(Parent);
            if (Directory.Exists(Parent)) Directory.Delete(Parent, recursive: true);
        }

        private static void EnsureContained(string path)
        {
            string prefix = Path.GetFullPath(TestBase).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe quarantine test path.");
        }
    }
}
