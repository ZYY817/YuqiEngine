using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Tests.Snapshot;

public sealed class SnapshotServiceIsolationTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("nested/id")]
    [InlineData("nested\\id")]
    [InlineData("C:\\Windows\\win")]
    [InlineData(" ")]
    public async Task EveryPublicSnapshotIdApiRejectsNonCanonicalIds(string hostileId)
    {
        using var scope = new TempScope();
        var service = scope.Service();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetSnapshotAsync(hostileId));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DeleteSnapshotAsync(hostileId));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ExportSnapshotAsync(hostileId, Path.Combine(scope.Root, "export.json")));
        Assert.Throws<InvalidDataException>(() => service.HasRegistryBackup(hostileId));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RestoreRegistryBackupAsync(hostileId));
    }

    [Fact]
    public async Task CatalogReturnsOnlyValidTypedSnapshotsAndDiagnosesCorruption()
    {
        using var scope = new TempScope();
        var valid = ValidV4("deadbeef", "valid");
        await WriteAsync(Path.Combine(scope.Root, "deadbeef.json"), valid);

        var checksumMismatch = ValidV4("cafebabe", "tampered");
        checksumMismatch.Name = "changed-after-checksum";
        await WriteAsync(Path.Combine(scope.Root, "cafebabe.json"), checksumMismatch, recomputeChecksum: false);

        await WriteAsync(Path.Combine(scope.Root, "0123abcd.json"), ValidV4("7654abcd", "id mismatch"));
        await File.WriteAllTextAsync(Path.Combine(scope.Root, "not-an-id!.json"), "{}");

        var catalog = await scope.Service().GetSnapshotCatalogAsync();

        var item = Assert.Single(catalog.Snapshots);
        Assert.Equal("deadbeef", item.Id);
        Assert.Equal(4, item.SettingsCount);
        Assert.Equal(4, item.OperationsCount);
        Assert.Equal(3, catalog.Diagnostics.Count);
        Assert.Contains(catalog.Diagnostics, item => item.FileName == "cafebabe.json");
        Assert.Contains(catalog.Diagnostics, item => item.FileName == "0123abcd.json");
        Assert.Contains(catalog.Diagnostics, item => item.ReasonCode == "snapshot.path.invalid");
    }

    [Fact]
    public async Task ImportRequiresStrictManifestAndChecksumAndWritesOnlyToInjectedRoot()
    {
        using var scope = new TempScope();
        var importRoot = Path.Combine(scope.Root, "incoming");
        Directory.CreateDirectory(importRoot);
        var service = scope.Service();

        var validPath = Path.Combine(importRoot, "valid.json");
        await WriteAsync(validPath, ValidV4("a1b2c3d4", "portable"));
        var imported = await service.ImportSnapshotAsync(validPath);
        Assert.NotNull(imported);
        Assert.True(File.Exists(Path.Combine(scope.Root, $"{imported.Id}.json")));

        var tamperedPath = Path.Combine(importRoot, "tampered.json");
        var tampered = ValidV4("11223344", "before");
        tampered.Name = "after";
        await WriteAsync(tamperedPath, tampered, recomputeChecksum: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportSnapshotAsync(tamperedPath));

        var incompletePath = Path.Combine(importRoot, "incomplete.json");
        var incomplete = ValidV4("55667788", "incomplete");
        incomplete.TypedRegistryDwordStates.RemoveAt(0);
        await WriteAsync(incompletePath, incomplete);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportSnapshotAsync(incompletePath));

        var unknownMemberPath = Path.Combine(importRoot, "unknown.json");
        var json = JsonSerializer.Serialize(ValidV4("99aabbcc", "unknown"), JsonOptions);
        await File.WriteAllTextAsync(unknownMemberPath, json[..^1] + ",\"unexpected\":true}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportSnapshotAsync(unknownMemberPath));
    }

    [Fact]
    public async Task ImportRejectsPayloadBeyondCapacityBoundaryBeforeJsonParsing()
    {
        using var scope = new TempScope();
        var oversized = Path.Combine(scope.Root, "oversized.json");
        await using (var stream = new FileStream(oversized, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(SnapshotFilePolicy.MaximumSnapshotBytes + 1);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Service().ImportSnapshotAsync(oversized));
        Assert.DoesNotContain(Directory.GetFiles(scope.Root, "*.json"), path => path != oversized);
    }

    [Fact]
    public async Task PendingEnumerationUsesInjectedDurableStoreAndHonorsCancellation()
    {
        using var scope = new TempScope();
        var batchRoot = Path.Combine(scope.Root, "restore-batches");
        var store = new FileSnapshotRestoreBatchStore(batchRoot);
        var plan = Plan("deadbeef");
        await store.RegisterAsync(plan, default);
        await store.SetClaimingAsync(plan, default);

        var pending = await scope.Service().GetPendingRestoreBatchesAsync();
        var item = Assert.Single(pending);
        Assert.Equal(plan.PlanId, item.PlanId);
        Assert.Equal(plan.PlanSha256, item.PlanSha256);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.Service().GetPendingRestoreBatchesAsync(canceled.Token));
    }

    [Fact]
    public async Task ConcurrentCatalogReadsAreDeterministicAndReadOnly()
    {
        using var scope = new TempScope();
        await WriteAsync(Path.Combine(scope.Root, "deadbeef.json"), ValidV4("deadbeef", "valid"));
        var service = scope.Service();

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => service.GetSnapshotCatalogAsync()));

        Assert.All(results, result => Assert.Equal("deadbeef", Assert.Single(result.Snapshots).Id));
        Assert.All(results, result => Assert.Empty(result.Diagnostics));
        Assert.Single(Directory.GetFiles(scope.Root, "*.json"));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task WriteAsync(string path, OptimizationSnapshot snapshot, bool recomputeChecksum = true)
    {
        if (recomputeChecksum)
            snapshot.Checksum = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private static OptimizationSnapshot ValidV4(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Version = 4,
        TypedRegistryDwordStates =
        [
            new(RepairChangeCapabilityIds.GameDvrAppCapture, @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR|AppCaptureEnabled|游戏录制", true, 1),
            new(RepairChangeCapabilityIds.GameDvrEnabled, @"HKEY_CURRENT_USER\System\GameConfigStore|GameDVR_Enabled|游戏模式", true, 0),
            new(RepairChangeCapabilityIds.GameModeAutoEnable, @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\GameBar|AutoGameModeEnabled|Game Bar", false, null),
            new("basic.disable_game_bar", @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\GameBar|ShowStartupPanel|Game Bar", true, 1)
        ]
    };

    private static SnapshotRestorePlan Plan(string snapshotId)
    {
        var now = DateTimeOffset.UtcNow;
        return new SnapshotRestorePlan(
            SnapshotRestoreCoordinator.CurrentSchemaVersion,
            Guid.NewGuid(),
            snapshotId,
            SnapshotRestoreKind.Normal,
            new string('A', 64),
            [],
            new string('B', 64),
            now,
            now.AddMinutes(2));
    }

    private sealed class TempScope : IDisposable
    {
        public TempScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "yuqi-snapshot-service-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public SnapshotService Service() => new(new DenyGate(), null, null, null, null, null, Root);

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class DenyGate : ISnapshotRestoreAuthorizationGate
    {
        public Task<SnapshotRestoreAuthorizationDecision> AuthorizeAsync(
            SnapshotRestorePlan plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SnapshotRestoreAuthorizationDecision(false, "test-denied", "test-denied"));
    }
}
