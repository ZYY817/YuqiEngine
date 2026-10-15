using GameOptimizer.Core.Services.SystemCleanup;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class SystemCleanupSafetyTests
{
    [Fact]
    public void DependencyInjectionUsesTheBuiltInImmutableCatalog()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICleanupAuthorizationGate, AllowGate>();
        services.AddSingleton<ICleanupQuarantine>(new FileSystemCleanupQuarantine());
        services.AddSingleton<ISystemCleanupService, SystemCleanupService>();
        using ServiceProvider provider = services.BuildServiceProvider();

        IReadOnlyList<CleanupItem> catalog = provider.GetRequiredService<ISystemCleanupService>().GetCleanupItems();

        Assert.Equal(13, catalog.Count);
        Assert.Contains(catalog, item => item.Id == "system_temp");
    }

    [Fact]
    public void PathTraversalOutsideAllowlistIsRejected()
    {
        using var fixture = TempFixture.Create();
        string outside = Path.Combine(fixture.Parent, "outside.bin");

        bool allowed = CleanupPathPolicy.TryValidateCandidate(fixture.Root, outside, out _, out string reason);

        Assert.False(allowed);
        Assert.Equal("cleanup.path-outside-allowlist", reason);
    }

    [Fact]
    public void VolumeRootIsNeverAnAllowlistedRoot()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.False(CleanupPathPolicy.TryValidateRoot(root, out _, out string reason));
        Assert.Equal("cleanup.root-forbidden", reason);
    }

    [Fact]
    public void ReparsePointInCandidateChainIsRejected()
    {
        using var fixture = TempFixture.Create();
        string child = Path.Combine(fixture.Root, "link");
        string file = Path.Combine(child, "file.bin");
        Directory.CreateDirectory(child);
        File.WriteAllText(file, "x");

        bool allowed = CleanupPathPolicy.TryValidateCandidate(
            fixture.Root,
            file,
            out _,
            out string reason,
            path => string.Equals(path, child, StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : File.GetAttributes(path));

        Assert.False(allowed);
        Assert.Equal("cleanup.path-reparse-point", reason);
    }

    [Fact]
    public void LongCanonicalCandidateRemainsContainedWithoutPathTruncation()
    {
        using var fixture = TempFixture.Create();
        string relative = string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("segment-0123456789", 20)) + Path.DirectorySeparatorChar + "cache.bin";
        string candidate = Path.Combine(fixture.Root, relative);

        bool allowed = CleanupPathPolicy.TryValidateCandidate(
            fixture.Root,
            candidate,
            out string canonical,
            out string reason,
            _ => FileAttributes.Normal);

        Assert.True(allowed || reason == "cleanup.path-invalid", reason);
        if (allowed)
        {
            Assert.StartsWith(fixture.Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, canonical, StringComparison.OrdinalIgnoreCase);
            Assert.True(canonical.Length > 260);
        }
    }

    [Fact]
    public async Task PreviewIsImmutableDeduplicatedAndTotalsExactBytes()
    {
        using var fixture = TempFixture.Create();
        string file = fixture.Write("cache/a.bin", "12345");
        var item = fixture.Item([fixture.Root, fixture.Root]);
        var service = fixture.Service(new AllowGate(), item);

        CleanupPreview preview = await service.CreatePreviewAsync(item);

        Assert.Single(preview.Entries);
        Assert.Equal(5, preview.TotalBytes);
        Assert.Equal(Path.GetFullPath(file), preview.Entries[0].CanonicalPath);
        Assert.True(CleanupResourceIdentity.IsSha256(preview.Entries[0].ContentSha256), "预览应包含内容哈希以支持授权前漂移检测。");
        Assert.Throws<NotSupportedException>(() => ((IList<CleanupPreviewEntry>)preview.Entries).Add(preview.Entries[0]));
    }

    [Fact]
    public void PreviewRetainsSkippedAccessMetricsWithoutChangingCleanupIdentity()
    {
        var entry = new CleanupPreviewEntry(@"C:\allowed\cache.bin", @"C:\allowed", 12, DateTime.UnixEpoch, new string('A', 64));
        var complete = new CleanupPreview("temp", [entry], DateTimeOffset.UnixEpoch);
        var partial = new CleanupPreview(
            "temp",
            [entry],
            DateTimeOffset.UnixEpoch,
            skippedDirectoryCount: 3,
            skippedFileCount: 2);

        Assert.Equal(3, partial.SkippedDirectoryCount);
        Assert.Equal(2, partial.SkippedFileCount);
        Assert.Equal(complete.Fingerprint, partial.Fingerprint);
    }

    [Fact]
    public async Task CallerCannotReplaceAllowlistedRootAfterServiceConstruction()
    {
        using var fixture = TempFixture.Create();
        string allowed = fixture.Write("allowed.bin", "ok");
        string outsideRoot = Path.Combine(fixture.Parent, "outside");
        Directory.CreateDirectory(outsideRoot);
        string outside = Path.Combine(outsideRoot, "outside.bin");
        File.WriteAllText(outside, "must stay");
        var item = fixture.Item([fixture.Root]);
        var service = fixture.Service(new AllowGate(), item);
        item.TargetPaths = [outsideRoot];

        CleanupPreview preview = await service.CreatePreviewAsync(item);

        Assert.Single(preview.Entries);
        Assert.Equal(allowed, preview.Entries[0].CanonicalPath);
        Assert.DoesNotContain(preview.Entries, entry => entry.CanonicalPath == outside);
    }

    [Fact]
    public async Task DefaultServiceBlocksWritesUntilBaselineGateIsConnected()
    {
        using var fixture = TempFixture.Create();
        string file = fixture.Write("blocked.bin", "data");
        var item = fixture.Item([fixture.Root]);
        var service = SystemCleanupService.CreateForTesting(new DenyCleanupAuthorizationGate(), new FileSystemCleanupQuarantine(fixture.Quarantine), [item]);

        CleanupResult result = await ConfirmAndCleanAsync(service, item);

        Assert.True(File.Exists(file));
        Assert.Contains(result.Errors, error => error.Contains("cleanup.baseline-gate-not-connected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AllowedCleanupMovesToRecoverableQuarantineInsteadOfDeleting()
    {
        using var fixture = TempFixture.Create();
        string source = fixture.Write("cache.bin", "recoverable");
        var item = fixture.Item([fixture.Root]);
        var service = fixture.Service(new AllowGate(), item);

        CleanupResult result = await ConfirmAndCleanAsync(service, item);

        CleanupFileResult moved = Assert.Single(result.FileResults);
        Assert.Equal(CleanupFileOutcome.Quarantined, moved.Outcome);
        Assert.False(File.Exists(source));
        Assert.NotNull(moved.QuarantinePath);
        Assert.True(File.Exists(moved.QuarantinePath));
    }

    [Fact]
    public async Task FileChangedAfterPreviewIsBlockedAsRace()
    {
        using var fixture = TempFixture.Create();
        string file = fixture.Write("race.bin", "before");
        var item = fixture.Item([fixture.Root]);
        var service = fixture.Service(new MutatingGate(() => File.AppendAllText(file, "-changed")), item);

        CleanupResult result = await ConfirmAndCleanAsync(service, item);

        Assert.Equal(CleanupFileOutcome.ChangedSincePreview, Assert.Single(result.FileResults).Outcome);
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData(typeof(IOException), CleanupFileOutcome.Locked)]
    [InlineData(typeof(UnauthorizedAccessException), CleanupFileOutcome.PermissionDenied)]
    public async Task LockedAndPermissionFailuresProducePerFileResults(Type exceptionType, CleanupFileOutcome expected)
    {
        using var fixture = TempFixture.Create();
        string file = fixture.Write("failure.bin", "data");
        Exception exception = (Exception)Activator.CreateInstance(exceptionType, "failure")!;
        var item = fixture.Item([fixture.Root]);
        var service = SystemCleanupService.CreateForTesting(new AllowGate(), new ThrowingQuarantine(exception), [item]);

        CleanupResult result = await ConfirmAndCleanAsync(service, item);

        Assert.Equal(expected, Assert.Single(result.FileResults).Outcome);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task CancellationDuringAuthorizationReturnsPerItemCancelledAndDoesNotWrite()
    {
        using var fixture = TempFixture.Create();
        string file = fixture.Write("cancel.bin", "data");
        var item = fixture.Item([fixture.Root]);
        var service = fixture.Service(new CancellingGate(), item);

        CleanupResult result = await ConfirmAndCleanAsync(service, item);

        Assert.Equal(CleanupFileOutcome.Cancelled, Assert.Single(result.FileResults).Outcome);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task PartialFailurePreservesSuccessfulAndFailedItemResults()
    {
        using var fixture = TempFixture.Create();
        fixture.Write("a.bin", "a");
        fixture.Write("b.bin", "bb");
        var item = fixture.Item([fixture.Root]);
        var service = SystemCleanupService.CreateForTesting(new AllowGate(), new FirstSucceedsThenFailsQuarantine(fixture.Quarantine), [item]);

        CleanupResult result = await ConfirmAndCleanAsync(service, item);

        Assert.Equal(2, result.FileResults.Count);
        Assert.Contains(result.FileResults, entry => entry.Outcome == CleanupFileOutcome.Quarantined);
        Assert.Contains(result.FileResults, entry => entry.Outcome == CleanupFileOutcome.Locked);
        Assert.Equal(1, result.FilesDeleted);
        Assert.Equal(1, result.FilesFailed);
    }

    [Fact]
    public async Task ReceiptPersistenceFailureNeverReportsARecoverableMoveAsSuccess()
    {
        using var fixture = TempFixture.Create();
        string source = fixture.Write("receipt.bin", "data");
        var item = fixture.Item([fixture.Root]);
        var service = SystemCleanupService.CreateForTesting(new AllowGate(), new ReceiptFailingAfterMoveQuarantine(fixture.Quarantine), [item]);

        CleanupResult result = await ConfirmAndCleanAsync(service, item);

        CleanupFileResult failed = Assert.Single(result.FileResults);
        Assert.Equal(CleanupFileOutcome.Failed, failed.Outcome);
        Assert.Equal("cleanup.test-receipt-failed", failed.ReasonCode);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(0, result.TotalCleaned);
        Assert.False(File.Exists(source));
        CleanupQuarantinePreview restore = await new CleanupQuarantineManager(fixture.Quarantine).CreateRestorePreviewAsync();
        Assert.Single(restore.Entries);
    }

    [Fact]
    public async Task CancellationAfterMoveIsPartialAndRequiresRecoveryInsteadOfSuccess()
    {
        using var fixture = TempFixture.Create();
        string source = fixture.Write("cancel-after-move.bin", "data");
        var item = fixture.Item([fixture.Root]);
        var service = SystemCleanupService.CreateForTesting(new AllowGate(), new CancelAfterMoveQuarantine(fixture.Quarantine), [item]);

        CleanupResult result = await ConfirmAndCleanAsync(service, item);

        CleanupFileResult partial = Assert.Single(result.FileResults);
        Assert.Equal(CleanupFileOutcome.CancelledAfterPartialQuarantine, partial.Outcome);
        Assert.Equal("cleanup.quarantine-cancelled-after-move", partial.ReasonCode);
        Assert.Equal(0, result.FilesDeleted);
        Assert.False(File.Exists(source));
        Assert.Single((await new CleanupQuarantineManager(fixture.Quarantine).CreateRestorePreviewAsync()).Entries);
    }

    private sealed class AllowGate : ICleanupAuthorizationGate
    {
        public Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken) =>
            Task.FromResult(new CleanupAuthorizationDecision(true, "test.allowed", "allowed"));
    }

    [Fact]
    public async Task LegacyCleanupWithoutConfirmedPreviewIsFailClosed()
    {
        using var fixture = TempFixture.Create();
        string file = fixture.Write("legacy.bin", "data");
        CleanupItem item = fixture.Item([fixture.Root]);
        SystemCleanupService service = fixture.Service(new AllowGate(), item);

        CleanupResult result = await service.CleanItemAsync(item);

        Assert.True(File.Exists(file));
        Assert.Contains(result.Errors, error => error.StartsWith("cleanup.confirmation-required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExpiredOrDifferentPreviewConfirmationMovesNothingBeforeBaseline()
    {
        using var fixture = TempFixture.Create();
        string file = fixture.Write("expired.bin", "data");
        CleanupItem item = fixture.Item([fixture.Root]);
        var gate = new CountingGate();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero));
        var service = SystemCleanupService.CreateForTesting(gate, new FileSystemCleanupQuarantine(fixture.Quarantine), [item], clock);
        CleanupPreview preview = await service.CreatePreviewAsync(item);
        CleanupPreview other = new(preview.ItemId, preview.Entries, preview.CreatedAtUtc);
        CleanupPreviewConfirmation expired = CleanupPreviewConfirmation.Create(preview, clock.GetUtcNow().AddMinutes(-3), TimeSpan.FromMinutes(2));
        CleanupPreviewConfirmation different = CleanupPreviewConfirmation.Create(other, clock.GetUtcNow(), TimeSpan.FromMinutes(2));

        CleanupResult expiredResult = await service.CleanPreviewAsync(item, preview, expired);
        CleanupResult differentResult = await service.CleanPreviewAsync(item, preview, different);

        Assert.True(File.Exists(file));
        Assert.Equal(0, gate.CallCount);
        Assert.All(new[] { expiredResult, differentResult }, result =>
            Assert.Contains(result.Errors, error => error.StartsWith("cleanup.confirmation-invalid-or-expired", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ForgedOrReplayedPreviewMovesNothing()
    {
        using var fixture = TempFixture.Create();
        string file = fixture.Write("identity.bin", "data");
        CleanupItem item = fixture.Item([fixture.Root]);
        var gate = new CountingGate();
        var service = SystemCleanupService.CreateForTesting(gate, new FileSystemCleanupQuarantine(fixture.Quarantine), [item]);
        CleanupPreview issued = await service.CreatePreviewAsync(item);
        CleanupPreview forged = new(issued.ItemId, issued.Entries, issued.CreatedAtUtc);
        CleanupPreviewConfirmation forgedConfirmation = CleanupPreviewConfirmation.Create(forged, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

        CleanupResult forgedResult = await service.CleanPreviewAsync(item, forged, forgedConfirmation);
        CleanupPreviewConfirmation issuedConfirmation = CleanupPreviewConfirmation.Create(issued, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        CleanupResult first = await service.CleanPreviewAsync(item, issued, issuedConfirmation);
        CleanupResult replay = await service.CleanPreviewAsync(item, issued, issuedConfirmation);

        Assert.Contains(forgedResult.Errors, error => error.StartsWith("cleanup.preview-not-issued-or-replayed", StringComparison.Ordinal));
        Assert.Single(first.FileResults, result => result.Outcome == CleanupFileOutcome.Quarantined);
        Assert.Contains(replay.Errors, error => error.StartsWith("cleanup.preview-not-issued-or-replayed", StringComparison.Ordinal));
        Assert.Equal(1, gate.CallCount);
        Assert.False(File.Exists(file));
    }

    private static async Task<CleanupResult> ConfirmAndCleanAsync(SystemCleanupService service, CleanupItem item)
    {
        CleanupPreview preview = await service.CreatePreviewAsync(item);
        CleanupPreviewConfirmation confirmation = CleanupPreviewConfirmation.Create(preview, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        return await service.CleanPreviewAsync(item, preview, confirmation);
    }

    private sealed class CountingGate : ICleanupAuthorizationGate
    {
        public int CallCount { get; private set; }
        public Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CleanupAuthorizationDecision(true, "test.allowed", "allowed"));
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MutatingGate : ICleanupAuthorizationGate
    {
        private readonly Action _mutation;
        public MutatingGate(Action mutation) => _mutation = mutation;
        public Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken)
        {
            _mutation();
            return Task.FromResult(new CleanupAuthorizationDecision(true, "test.allowed", "allowed"));
        }
    }

    private sealed class CancellingGate : ICleanupAuthorizationGate
    {
        public Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken) =>
            Task.FromException<CleanupAuthorizationDecision>(new OperationCanceledException());
    }

    private sealed class ThrowingQuarantine : ICleanupQuarantine
    {
        private readonly Exception _exception;
        public ThrowingQuarantine(Exception exception) => _exception = exception;
        public Task<string> MoveAsync(CleanupPreviewEntry entry, string itemId, CancellationToken cancellationToken) => Task.FromException<string>(_exception);
    }

    private sealed class FirstSucceedsThenFailsQuarantine : ICleanupQuarantine
    {
        private readonly FileSystemCleanupQuarantine _inner;
        private int _count;
        public FirstSucceedsThenFailsQuarantine(string root) => _inner = new(root);
        public Task<string> MoveAsync(CleanupPreviewEntry entry, string itemId, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _count) == 1
                ? _inner.MoveAsync(entry, itemId, cancellationToken)
                : Task.FromException<string>(new IOException("locked"));

        public Task<CleanupQuarantineMove> MoveWithReceiptAsync(
            CleanupPreviewEntry entry,
            string itemId,
            string operationId,
            CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _count) == 1
                ? _inner.MoveWithReceiptAsync(entry, itemId, operationId, cancellationToken)
                : Task.FromException<CleanupQuarantineMove>(new IOException("locked"));
    }

    private sealed class ReceiptFailingAfterMoveQuarantine : ICleanupQuarantine
    {
        private readonly FileSystemCleanupQuarantine _inner;
        public ReceiptFailingAfterMoveQuarantine(string root) => _inner = new(root);
        public Task<string> MoveAsync(CleanupPreviewEntry entry, string itemId, CancellationToken cancellationToken) =>
            _inner.MoveAsync(entry, itemId, cancellationToken);

        public async Task<CleanupQuarantineMove> MoveWithReceiptAsync(
            CleanupPreviewEntry entry,
            string itemId,
            string operationId,
            CancellationToken cancellationToken)
        {
            _ = await _inner.MoveWithReceiptAsync(entry, itemId, operationId, cancellationToken);
            throw new CleanupQuarantinePersistenceException("cleanup.test-receipt-failed", "fault injected after quarantine move", mutationOccurred: true);
        }
    }

    private sealed class CancelAfterMoveQuarantine : ICleanupQuarantine
    {
        private readonly FileSystemCleanupQuarantine _inner;
        public CancelAfterMoveQuarantine(string root) => _inner = new(root);
        public Task<string> MoveAsync(CleanupPreviewEntry entry, string itemId, CancellationToken cancellationToken) =>
            _inner.MoveAsync(entry, itemId, cancellationToken);

        public async Task<CleanupQuarantineMove> MoveWithReceiptAsync(
            CleanupPreviewEntry entry,
            string itemId,
            string operationId,
            CancellationToken cancellationToken)
        {
            _ = await _inner.MoveWithReceiptAsync(entry, itemId, operationId, cancellationToken);
            throw new CleanupQuarantinePersistenceException("cleanup.quarantine-cancelled-after-move", "fault injected after quarantine move", mutationOccurred: true);
        }
    }

    private sealed class TempFixture : IDisposable
    {
        private static readonly string TestBase = Path.Combine(Path.GetTempPath(), "yuqi-system-cleanup-tests");
        private TempFixture(string parent, string root, string quarantine) { Parent = parent; Root = root; Quarantine = quarantine; }
        public string Parent { get; }
        public string Root { get; }
        public string Quarantine { get; }

        public static TempFixture Create()
        {
            string parent = Path.GetFullPath(Path.Combine(TestBase, Guid.NewGuid().ToString("N")));
            string prefix = Path.GetFullPath(TestBase).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!parent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test path.");
            string root = Path.Combine(parent, "allowlisted");
            string quarantine = Path.Combine(parent, "quarantine");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(quarantine);
            return new(parent, root, quarantine);
        }

        public string Write(string relativePath, string content)
        {
            string path = Path.GetFullPath(Path.Combine(Root, relativePath));
            string prefix = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test file path.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public CleanupItem Item(string[] roots) => new() { Id = "test_cleanup", TargetPaths = roots, Patterns = ["*"], IncludeSubdirectories = true, IsSelected = true };
        public SystemCleanupService Service(ICleanupAuthorizationGate gate, CleanupItem item) =>
            SystemCleanupService.CreateForTesting(gate, new FileSystemCleanupQuarantine(Quarantine), [item]);

        public void Dispose()
        {
            string parent = Path.GetFullPath(Parent);
            string prefix = Path.GetFullPath(TestBase).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (parent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }
}
