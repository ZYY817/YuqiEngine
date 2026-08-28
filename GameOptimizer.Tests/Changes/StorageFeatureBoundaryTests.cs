using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Services.Repair.RepairOperations;
using GameOptimizer.Core.Services.StorageSafety;
using GameOptimizer.Infrastructure.Windows.StorageSafety;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class StorageFeatureBoundaryTests
{
    [Fact]
    public async Task RepairEntryUsesSingleOwnerAndNeverMutatesStorage()
    {
        var platform = new FakePlatform(new StorageFeatureSnapshot(
            StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
            StorageFeatureDiagnosticOutcome.Degraded,
            false,
            "volume:C:",
            "NTFS",
            true,
            1,
            "Stopped",
            "Disabled",
            "fake degraded"));
        var owner = new StorageFeatureRepairOwner(platform);
        var registry = new RepairItemRegistry(storageFeatureRepairOwner: owner);
        registry.Initialize();

        var item = Assert.IsType<RepairItem>(registry.GetById("repair.ntfs_encryption"));
        Assert.False(await item.CheckHealthAsync());
        var result = await item.RepairAsync(null);

        Assert.False(result.Success);
        Assert.Contains("不会修改", result.Message, StringComparison.Ordinal);
        Assert.Equal(2, platform.Reads);
        Assert.Equal(0, platform.Writes);
    }

    [Theory]
    [InlineData(StorageFeatureDiagnosticOutcome.NotApplicable)]
    [InlineData(StorageFeatureDiagnosticOutcome.PermissionDenied)]
    [InlineData(StorageFeatureDiagnosticOutcome.Unknown)]
    public async Task FailuresRemainStructuredAndFailClosed(StorageFeatureDiagnosticOutcome outcome)
    {
        var owner = new StorageFeatureRepairOwner(new FakePlatform(
            StorageFeatureSnapshot.Failed("repair.ntfs_encryption", outcome, outcome.ToString())));

        var snapshot = await owner.DiagnoseAsync("repair.ntfs_encryption", default);
        var repair = await owner.RepairAsync("repair.ntfs_encryption", null, default);

        Assert.Equal(outcome, snapshot.Outcome);
        Assert.Null(snapshot.IsHealthy);
        Assert.False(repair.Success);
    }

    [Fact]
    public async Task LegacyStorageOperationsAndRevertAreFailClosed()
    {
        var operations = new IOptimizationOperation[]
        {
            new DisableNtfs8Dot3Operation(),
            new OptimizeIoPageLockLimitOperation()
        };
        foreach (var operation in operations)
        {
            var apply = await operation.ApplyAsync(null!, default);
            var restore = await operation.RestoreAsync(null!, default);
            Assert.False(apply.Success);
            Assert.False(apply.Changed);
            Assert.False(restore.Success);
            Assert.False(restore.Changed);
        }

        var revert = OptimizationRevertRepairs.GetAll()
            .Single(value => value.Id == "repair.revert.ntfs_encryption");
        var result = await revert.RepairAsync(null);
        Assert.False(result.Success);
        Assert.Contains("旧快照", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositionRegistersOnePlatformAndOwner()
    {
        var services = new ServiceCollection();
        services.AddStorageFeatureWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, value => value.ServiceType == typeof(IStorageFeatureDiagnosticPlatform));
        Assert.Single(services, value => value.ServiceType == typeof(IStorageFeatureRepairOwner));
        Assert.IsType<WindowsStorageFeatureDiagnosticPlatform>(provider.GetRequiredService<IStorageFeatureDiagnosticPlatform>());
        Assert.Same(provider.GetRequiredService<IStorageFeatureRepairOwner>(), provider.GetRequiredService<IStorageFeatureRepairOwner>());
    }

    [Fact]
    public void ProductionStorageScopeContainsNoLegacyWriter()
    {
        var root = FindRoot();
        var files = new[]
        {
            @"GameOptimizer.Core\Services\Repair\RepairOperations\StorageRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\OptimizationRevertRepairs.cs",
            @"GameOptimizer.Core\Optimization\Operations\StorageOptimizationOperation.cs"
        };
        var forbidden = new[]
        {
            "CreateSubKey(@\"SYSTEM\\CurrentControlSet\\Control\\FileSystem\")",
            "SetValue(\"NtfsDisableEncryption\"",
            "SetValue(\"NtfsDisable8dot3NameCreation\"",
            "SetValue(\"IoPageLockLimit\""
        };

        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task LocalCertificateVisibilityNeverUnlocksNtfsEfsMutation()
    {
        var visibleLocalMaterial = new EfsRecoveryMaterialPreflight(
            true, 2, 2, 2, 1, "aggregate only");
        var owner = new StorageFeatureRepairOwner(new FakePlatform(new StorageFeatureSnapshot(
            StorageFeatureRepairCatalog.NtfsEncryptionRepairId,
            StorageFeatureDiagnosticOutcome.Healthy,
            true,
            "volume:C:",
            "NTFS",
            false,
            null,
            "Running",
            "Auto",
            "fake healthy",
            visibleLocalMaterial)));

        var result = await owner.RepairAsync(StorageFeatureRepairCatalog.NtfsEncryptionRepairId, null, default);

        Assert.False(result.Success);
        Assert.Contains("不能解除门禁", result.Message, StringComparison.Ordinal);
        Assert.Contains("PrivateKeyAvailable=2", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsPreflightSourceDoesNotReadOrPersistSensitiveCertificateMaterial()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Infrastructure.Windows\StorageSafety\WindowsStorageFeatureDiagnosticPlatform.cs"));
        var forbidden = new[] { ".Export(", "GetPrivateKey(", "certutil", "cipher", ".Subject", ".Thumbprint", ".SerialNumber", "File.Read", "File.Write" };

        foreach (var token in forbidden)
        {
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("StoreLocation.CurrentUser", source, StringComparison.Ordinal);
        Assert.Contains("OpenFlags.ReadOnly", source, StringComparison.Ordinal);
    }

    private sealed class FakePlatform(StorageFeatureSnapshot snapshot) : IStorageFeatureDiagnosticPlatform
    {
        public int Reads { get; private set; }
        public int Writes => 0;

        public Task<StorageFeatureSnapshot> InspectNtfsEncryptionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult(snapshot);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "GameOptimizer.Core")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
