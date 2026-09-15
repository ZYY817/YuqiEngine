using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services.Snapshot;
using GameOptimizer.Core.Optimization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameOptimizer.Tests.Snapshot;

public sealed class ComprehensiveBackupServiceTests
{
    [Fact]
    public async Task CreatePackage_CapturesRegistryAndTypedSnapshotWithoutImportPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "YuqiEngine-comprehensive-test", Guid.NewGuid().ToString("N"));
        var backend = new FakeRegistryBackend();
        var snapshot = new FakeSnapshotService();
        try
        {
            var service = new ComprehensiveBackupService(snapshot, backend, root);
            var package = await service.CreatePackageAsync("测试完整备份");

            Assert.False(package.Manifest.IsComplete);
            Assert.Contains("typed 恢复契约", package.Manifest.IncompleteReason);
            Assert.Contains(package.Manifest.Artifacts, item => item.Kind == ComprehensiveBackupArtifactKind.UserRegistry
                && item.Status == ComprehensiveBackupArtifactStatus.ManualOnly
                && !item.AutomaticRestoreSupported);
            Assert.Contains(package.Manifest.Artifacts, item => item.Kind == ComprehensiveBackupArtifactKind.TypedSnapshot
                && item.AutomaticRestoreSupported);
            Assert.True(File.Exists(Path.Combine(package.DirectoryPath, "manifest.json")));
            Assert.Equal(1, snapshot.CreateCount);
            Assert.Equal(0, backend.ImportCount);

            var listed = await service.GetPackagesAsync();
            Assert.Single(listed);
            Assert.True(listed[0].IntegrityVerified);
            var registryPath = Path.Combine(package.DirectoryPath, "payload", "user-hkcu.reg");
            File.AppendAllText(registryPath, "tampered");
            var tampered = await service.GetPackagesAsync();
            Assert.False(tampered[0].IntegrityVerified);
            Assert.Contains("校验失败", tampered[0].IntegrityError);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareTypedRestoreAsync(package.PackageId));
            Assert.Equal(0, snapshot.ImportCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreatePackage_RegistryFailurePublishesPartialPackageWithReason()
    {
        var root = Path.Combine(Path.GetTempPath(), "YuqiEngine-comprehensive-test", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ComprehensiveBackupService(
                new FakeSnapshotService(),
                new FakeRegistryBackend { FailExport = true },
                root);

            var package = await service.CreatePackageAsync("注册表失败备份");

            Assert.False(package.Manifest.IsComplete);
            var registry = Assert.Single(package.Manifest.Artifacts, item => item.Kind == ComprehensiveBackupArtifactKind.UserRegistry);
            Assert.Equal(ComprehensiveBackupArtifactStatus.Failed, registry.Status);
            Assert.Contains("测试导出失败", registry.Reason);
            Assert.True(File.Exists(Path.Combine(package.DirectoryPath, "manifest.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreatePackage_CancellationRemovesStagingAndDoesNotPublishPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "YuqiEngine-comprehensive-test", Guid.NewGuid().ToString("N"));
        using var cts = new CancellationTokenSource();
        try
        {
            var service = new ComprehensiveBackupService(
                new FakeSnapshotService(),
                new FakeRegistryBackend { BlockExport = true },
                root);

            var task = service.CreatePackageAsync("取消备份", ct: cts.Token);
            await Task.Delay(20);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            Assert.False(Directory.Exists(root) && Directory.EnumerateDirectories(root).Any());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreatePackage_WithOperationProviderCapturesEveryProbeResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "YuqiEngine-comprehensive-test", Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FakeOperationProvider();
            var restoreStore = new FakeRestoreStore(new Dictionary<string, string>
            {
                ["test.operation"] = "{\"before\":true}"
            });
            var service = new ComprehensiveBackupService(
                new FakeSnapshotService(),
                new FakeRegistryBackend(),
                root,
                provider,
                restoreStore);

            var package = await service.CreatePackageAsync("状态探测备份");

            var state = Assert.Single(package.Manifest.OperationStates);
            Assert.Equal("test.module", state.ModuleKey);
            Assert.Equal("test.operation", state.OperationId);
            Assert.True(state.IsCompliant);
            Assert.Null(state.Error);
            Assert.Equal("HKCU", state.RegistryHive);
            Assert.Equal("Software\\YuqiEngine\\__backup_probe_absent__", state.RegistryPath);
            Assert.Equal("Value", state.RegistryValueName);
            Assert.Equal("Missing", state.CaptureStatus);
            Assert.Contains("keyPresent", state.CapturedValueJson);
            var material = Assert.Single(package.Manifest.Artifacts, item => item.Kind == ComprehensiveBackupArtifactKind.OperationRecoveryMaterial);
            Assert.Equal(ComprehensiveBackupArtifactStatus.Captured, material.Status);
            Assert.Contains("test.operation", File.ReadAllText(Path.Combine(package.DirectoryPath, material.RelativePath)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareTypedRestore_VerifiesPackageThenReturnsExistingRestorePlan()
    {
        var root = Path.Combine(Path.GetTempPath(), "YuqiEngine-comprehensive-test", Guid.NewGuid().ToString("N"));
        try
        {
            var snapshot = new FakeSnapshotService();
            var service = new ComprehensiveBackupService(snapshot, new FakeRegistryBackend(), root);
            var package = await service.CreatePackageAsync("类型化恢复准备");

            var preparation = await service.PrepareTypedRestoreAsync(package.PackageId);

            Assert.Equal(package.PackageId, preparation.PackageId);
            Assert.Equal("imported-typed", preparation.ImportedSnapshotId);
            Assert.Equal(SnapshotRestoreKind.Normal, preparation.Plan.Kind);
            Assert.Contains(preparation.ManualOnlyArtifacts, item => item.Kind == ComprehensiveBackupArtifactKind.UserRegistry);
            Assert.Equal(1, snapshot.ImportCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareTypedRestore_BlocksDifferentUserEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), "YuqiEngine-comprehensive-test", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ComprehensiveBackupService(new FakeSnapshotService(), new FakeRegistryBackend(), root);
            var package = await service.CreatePackageAsync("环境指纹校验");
            var manifestPath = Path.Combine(package.DirectoryPath, "manifest.json");
            var manifest = await File.ReadAllTextAsync(manifestPath);
            manifest = manifest.Replace(package.Manifest.UserSidSha256, new string('0', 64), StringComparison.Ordinal);
            await File.WriteAllTextAsync(manifestPath, manifest);

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => service.PrepareTypedRestoreAsync(package.PackageId));

            Assert.Contains("其他 Windows 用户", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeRegistryBackend : IUserRegistryExportBackend
    {
        public bool FailExport { get; init; }
        public bool BlockExport { get; init; }
        public int ImportCount { get; private set; }

        public async Task<UserRegistryExportResult> ExportCurrentUserAsync(string destinationPath, CancellationToken ct = default)
        {
            if (BlockExport)
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            if (FailExport)
                return new UserRegistryExportResult(false, 0, "测试导出失败");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, "Windows Registry Editor Version 5.00\n");
            return new UserRegistryExportResult(true, new FileInfo(destinationPath).Length);
        }
    }

    private sealed class FakeSnapshotService : ISnapshotService
    {
        public int CreateCount { get; private set; }
        public int ImportCount { get; private set; }

        public Task<OptimizationSnapshot> CreateSnapshotAsync(string name, string? description = null, CancellationToken ct = default)
        {
            CreateCount++;
            return Task.FromResult(new OptimizationSnapshot { Id = "typed-test", Name = name });
        }

        public Task<SnapshotCoverageReport> GetCoverageReportAsync(CancellationToken ct = default) =>
            Task.FromResult(new SnapshotCoverageReport(2, 2, 1, 1, 0, new[] { "uncaptured" }));

        public Task<SnapshotRestorePlan> PrepareRestoreAsync(string snapshotId, SnapshotRestoreKind kind, CancellationToken ct = default) =>
            Task.FromResult(new SnapshotRestorePlan(
                2,
                Guid.NewGuid(),
                snapshotId,
                kind,
                "identity",
                Array.Empty<SnapshotRestoreStepPlan>(),
                "plan",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1)));
        public Task<SnapshotRestoreResult> ExecuteRestoreAsync(SnapshotRestorePlan plan, CancellationToken ct = default) => NotSupported<SnapshotRestoreResult>();
        public Task<RegistryBackupRestoreResult> ExecuteRegistryRestoreAsync(SnapshotRestorePlan plan, CancellationToken ct = default) => NotSupported<RegistryBackupRestoreResult>();
        public Task<SnapshotRestoreRecoveryReport> ReconcileRestoreAsync(Guid planId, string planSha256, CancellationToken ct = default) => NotSupported<SnapshotRestoreRecoveryReport>();
        public Task<SnapshotRestoreRecoveryReport> ResolveInterruptedRestoreAsync(Guid planId, string planSha256, SnapshotRestoreManualDecision decision, CancellationToken ct = default) => NotSupported<SnapshotRestoreRecoveryReport>();
        public Task<SnapshotRestoreResult> RestoreSnapshotAsync(string snapshotId, CancellationToken ct = default) => NotSupported<SnapshotRestoreResult>();
        public Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync() => Task.FromResult<IReadOnlyList<SnapshotInfo>>(Array.Empty<SnapshotInfo>());
        public Task<SnapshotCatalogResult> GetSnapshotCatalogAsync(CancellationToken ct = default) => NotSupported<SnapshotCatalogResult>();
        public Task<IReadOnlyList<SnapshotRestoreBatchIdentity>> GetPendingRestoreBatchesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SnapshotRestoreBatchIdentity>>(Array.Empty<SnapshotRestoreBatchIdentity>());
        public Task<OptimizationSnapshot?> GetSnapshotAsync(string snapshotId) => Task.FromResult<OptimizationSnapshot?>(null);
        public Task<OptimizationSnapshot?> ImportSnapshotAsync(string filePath)
        {
            ImportCount++;
            return Task.FromResult<OptimizationSnapshot?>(new OptimizationSnapshot
            {
                Id = "imported-typed",
                Name = "imported"
            });
        }
        public Task DeleteSnapshotAsync(string snapshotId) => Task.CompletedTask;
        public Task ExportSnapshotAsync(string snapshotId, string filePath) => Task.CompletedTask;
        public string GetSnapshotDirectory() => Path.GetTempPath();
        public Task<RegistryBackupRestoreResult> RestoreRegistryBackupAsync(string snapshotId, CancellationToken ct = default) => NotSupported<RegistryBackupRestoreResult>();
        public bool HasRegistryBackup(string snapshotId) => false;

        private static Task<T> NotSupported<T>() => Task.FromException<T>(new NotSupportedException());
    }

    private sealed class FakeOperationProvider : IOptimizationOperationProvider
    {
        public IEnumerable<OptimizationModuleDescriptor> GetModules() => new[]
        {
            new OptimizationModuleDescriptor("test.module", "测试", "测试", new[] { new FakeOperation() })
        };
    }

    private sealed class FakeRestoreStore(IReadOnlyDictionary<string, string> values) : IOperationRestoreStore
    {
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(values.TryGetValue(operationId, out var payload) ? payload : null);
        }

        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(values.ContainsKey(operationId));
        }
    }

    private sealed class FakeOperation : IOptimizationOperation
    {
        public string Id => "test.operation";
        public string Title => "测试操作";
        public string Description => "测试";
        public string Category => "测试";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public string RegistryHiveText => "HKCU";
        public string RegistryPath => @"Software\YuqiEngine\__backup_probe_absent__";
        public string RegistryValueName => "Value";
        public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new OperationStatus(true, "已符合"));
        public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationInvokeResult(true, false, "测试"));
        public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new OperationInvokeResult(true, false, "测试"));
    }
}
