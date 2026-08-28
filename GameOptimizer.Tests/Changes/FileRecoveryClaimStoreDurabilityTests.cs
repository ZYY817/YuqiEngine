using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;

namespace GameOptimizer.Tests.Changes;

public sealed class FileRecoveryClaimStoreDurabilityTests
{
    [Fact]
    public async Task AvailableState_TamperBadJsonAndUnsupportedVersion_AreRejected()
    {
        var root = EvidenceDirectory();
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        var store = new FileRecoveryClaimStore(root);
        await store.RegisterAvailableAsync(recoveryId, hash, CancellationToken.None);
        var path = StatePath(root, recoveryId, "available");
        var original = await File.ReadAllTextAsync(path);
        var document = JsonNode.Parse(original)!.AsObject();
        document["integrityHash"] = new string('B', 64);
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None));

        document = JsonNode.Parse(original)!.AsObject();
        document["schemaVersion"] = FileRecoveryClaimStore.CurrentSchemaVersion + 1;
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));
        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None));

        await File.WriteAllTextAsync(path, "{bad-json", new UTF8Encoding(false, true));
        await Assert.ThrowsAnyAsync<JsonException>(
            () => store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None));
    }

    [Fact]
    public async Task AvailableState_UnknownMemberAndOversize_AreRejected()
    {
        var root = EvidenceDirectory();
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        var store = new FileRecoveryClaimStore(root);
        await store.RegisterAvailableAsync(recoveryId, hash, CancellationToken.None);
        var path = StatePath(root, recoveryId, "available");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        document["unexpected"] = true;
        await File.WriteAllTextAsync(path, document.ToJsonString(), new UTF8Encoding(false, true));
        await Assert.ThrowsAnyAsync<JsonException>(
            () => store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None));

        await File.WriteAllBytesAsync(path, new byte[FileRecoveryClaimStore.MaximumStateBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentRegistration_IsIdempotentAndPublishesOneCompleteState()
    {
        var root = EvidenceDirectory();
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        var stores = Enumerable.Range(0, 12).Select(_ => new FileRecoveryClaimStore(root)).ToArray();
        await Task.WhenAll(stores.Select(store =>
            store.RegisterAvailableAsync(recoveryId, hash, CancellationToken.None)));

        var claim = await stores[0].TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None);
        Assert.Equal(RecoveryClaimStatus.Claimed, claim.Status);
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp-*"));
    }

    [Fact]
    public async Task CancelledRegistration_DoesNotPublishPartialStateOrLeaveOwnTemporaryFile()
    {
        var root = EvidenceDirectory();
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        var store = new FileRecoveryClaimStore(root);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.RegisterAvailableAsync(recoveryId, hash, source.Token));

        Assert.False(File.Exists(StatePath(root, recoveryId, "available")));
        Assert.True(!Directory.Exists(root) || !Directory.EnumerateFiles(root, "*.tmp-*").Any());
    }

    [Fact]
    public async Task StaleAvailableTemporaryFile_IsRemovedBeforeACompleteCommit()
    {
        var root = EvidenceDirectory();
        var recoveryId = Guid.NewGuid();
        var hash = new string('A', 64);
        Directory.CreateDirectory(root);
        var stalePath = Path.Combine(root, $".{recoveryId:N}.available.tmp-crashed");
        await File.WriteAllTextAsync(stalePath, "partial", new UTF8Encoding(false, true));
        File.SetLastWriteTimeUtc(stalePath, DateTime.UtcNow.AddMinutes(-10));

        var store = new FileRecoveryClaimStore(root);
        await store.RegisterAvailableAsync(recoveryId, hash, CancellationToken.None);

        Assert.False(File.Exists(stalePath));
        Assert.Equal(
            RecoveryClaimStatus.Claimed,
            (await store.TryBeginRestoreAsync(recoveryId, hash, CancellationToken.None)).Status);
    }

    private static string StatePath(string root, Guid recoveryId, string state) =>
        Path.Combine(root, $"{recoveryId:N}.{state}");

    private static string EvidenceDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "recovery-store-evidence", Guid.NewGuid().ToString("N"));
}
