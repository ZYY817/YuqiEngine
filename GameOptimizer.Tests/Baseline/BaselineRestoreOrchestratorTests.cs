using System.IO.Compression;
using System.Text.Json;
using GameOptimizer.Core.Services.Snapshot;
using GameOptimizer.Shell.Services.Baseline;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Baseline;

public sealed class BaselineRestoreOrchestratorTests
{
    [Theory]
    [InlineData("ImportRegistry", true)]
    [InlineData("RunCommand", true)]
    [InlineData("ManualAction", true)]
    [InlineData("UnknownLegacyStep", true)]
    public async Task RestoreAsync_BlockedOrManualStep_DoesNotReportFullRestoreSuccess(
        string stepType,
        bool optional)
    {
        string packageDirectory = CreatePackageDirectory(stepType, optional);
        var manifest = new BaselinePackageManifest
        {
            PackageId = "baseline-package",
            EnvironmentBinding = BaselineEnvironmentBindingPolicy.CaptureCurrent(),
            Domains =
            {
                new BaselineDomainArtifact
                {
                    DomainId = "D03",
                    DomainName = "恢复真实性测试域",
                    Required = false,
                    RelativePath = "D03.bundle.zip"
                }
            }
        };
        var orchestrator = new BaselineRestoreOrchestrator(
            new FakeCatalog(manifest, packageDirectory),
            new EmergencySnapshotService(),
            new BaselineRestoreScoringService(),
            NullLogger<BaselineRestoreOrchestrator>.Instance);

        BaselineRestoreResult result = await orchestrator.RestoreAsync("baseline-package");

        Assert.False(result.Success);
        Assert.Equal(BaselineDomainStatus.ManualRequired, Assert.Single(result.Report.DomainResults).Status);
        Assert.Equal(0, result.Report.SuccessCount);
        Assert.Equal(1, result.Report.SkippedCount);
        Assert.Contains("不能宣称全部域恢复成功", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("全部选中域已执行并通过恢复结果校验", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRecoverabilitySelfCheckAsync_FileReadableButNoTypedRestoreOwner_FailsAdmission()
    {
        string packageDirectory = CreatePackageDirectory("CopyFile", optional: false, domainId: "D01");
        var manifest = new BaselinePackageManifest
        {
            PackageId = "baseline-package",
            Domains =
            {
                new BaselineDomainArtifact
                {
                    DomainId = "D01",
                    DomainName = "回滚仓快照",
                    Required = true,
                    RelativePath = "D03.bundle.zip",
                    SizeBytes = new FileInfo(Path.Combine(packageDirectory, "D03.bundle.zip")).Length
                }
            }
        };
        manifest.Domains[0].Sha256 = BaselineHashHelper.ComputeSha256(Path.Combine(packageDirectory, "D03.bundle.zip"));
        var orchestrator = new BaselineRestoreOrchestrator(
            new FakeCatalog(manifest, packageDirectory),
            new EmergencySnapshotService(),
            new BaselineRestoreScoringService(),
            NullLogger<BaselineRestoreOrchestrator>.Instance);

        BaselineRecoverabilityCheckResult result = await orchestrator.RunRecoverabilitySelfCheckAsync("baseline-package", "test");

        Assert.False(result.Success);
        Assert.Equal(5, result.DomainFailedCount);
        Assert.Equal(1, result.DomainManualRequiredCount);
        Assert.Contains("已阻断优化入口", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreAsync_TypedSnapshot_UsesPrepareExecuteAndKeepsVerifiedImportedSnapshot()
    {
        string packageDirectory = CreatePackageDirectory("TypedSnapshot", optional: false, domainId: "D03");
        var manifest = new BaselinePackageManifest
        {
            PackageId = "baseline-package",
            EnvironmentBinding = BaselineEnvironmentBindingPolicy.CaptureCurrent(),
            Domains =
            {
                new BaselineDomainArtifact
                {
                    DomainId = "D03",
                    DomainName = "受控设置快照",
                    Required = true,
                    RelativePath = "D03.bundle.zip"
                }
            }
        };
        var snapshots = new EmergencySnapshotService { EnableTypedRestore = true };
        var orchestrator = new BaselineRestoreOrchestrator(
            new FakeCatalog(manifest, packageDirectory),
            snapshots,
            new BaselineRestoreScoringService(),
            NullLogger<BaselineRestoreOrchestrator>.Instance);

        BaselineRestoreResult result = await orchestrator.RestoreAsync("baseline-package");

        Assert.True(result.Success, result.Message);
        Assert.Equal(BaselineDomainStatus.Succeeded, Assert.Single(result.Report.DomainResults).Status);
        Assert.Equal(1, snapshots.ImportCount);
        Assert.Equal(1, snapshots.PrepareCount);
        Assert.Equal(1, snapshots.ExecuteCount);
        Assert.Empty(snapshots.DeletedSnapshotIds);
    }

    [Fact]
    public async Task RestoreAsync_TypedSnapshotFailure_RemovesTemporaryImportedSnapshot()
    {
        string packageDirectory = CreatePackageDirectory("TypedSnapshot", optional: false, domainId: "D03");
        var manifest = new BaselinePackageManifest
        {
            PackageId = "baseline-package",
            EnvironmentBinding = BaselineEnvironmentBindingPolicy.CaptureCurrent(),
            Domains =
            {
                new BaselineDomainArtifact
                {
                    DomainId = "D03",
                    DomainName = "受控设置快照",
                    Required = true,
                    RelativePath = "D03.bundle.zip"
                }
            }
        };
        var snapshots = new EmergencySnapshotService { EnableTypedRestore = true, FailTypedExecution = true };
        var orchestrator = new BaselineRestoreOrchestrator(
            new FakeCatalog(manifest, packageDirectory),
            snapshots,
            new BaselineRestoreScoringService(),
            NullLogger<BaselineRestoreOrchestrator>.Instance);

        BaselineRestoreResult result = await orchestrator.RestoreAsync("baseline-package");

        Assert.False(result.Success);
        Assert.Equal(BaselineDomainStatus.Failed, Assert.Single(result.Report.DomainResults).Status);
        Assert.Contains("imported-typed-snapshot", snapshots.DeletedSnapshotIds);
    }

    [Fact]
    public async Task RunRecoverabilitySelfCheckAsync_TypedSnapshotImportsThenRemovesValidationCopy()
    {
        string packageDirectory = CreatePackageDirectory("TypedSnapshot", optional: false, domainId: "D03");
        string bundlePath = Path.Combine(packageDirectory, "D03.bundle.zip");
        var manifest = new BaselinePackageManifest
        {
            PackageId = "baseline-package",
            Domains =
            {
                new BaselineDomainArtifact
                {
                    DomainId = "D03",
                    DomainName = "受控设置快照",
                    Required = true,
                    Status = BaselineDomainStatus.Succeeded,
                    RelativePath = "D03.bundle.zip",
                    SizeBytes = new FileInfo(bundlePath).Length,
                    Sha256 = BaselineHashHelper.ComputeSha256(bundlePath)
                }
            }
        };
        var snapshots = new EmergencySnapshotService { EnableTypedRestore = true };
        var orchestrator = new BaselineRestoreOrchestrator(
            new FakeCatalog(manifest, packageDirectory),
            snapshots,
            new BaselineRestoreScoringService(),
            NullLogger<BaselineRestoreOrchestrator>.Instance);

        BaselineRecoverabilityCheckResult result = await orchestrator.RunRecoverabilitySelfCheckAsync("baseline-package", "test");

        Assert.False(result.Success);
        Assert.Equal(4, result.DomainFailedCount);
        Assert.Contains("imported-typed-snapshot", snapshots.DeletedSnapshotIds);
    }

    [Fact]
    public async Task CollectDomainAsync_TypedSettingsDomain_ExportsTheTypedSnapshotInsteadOfRegFiles()
    {
        string packageDirectory = Path.Combine(@"F:\YuqiEngine\Verification\BaselineTypedSnapshotCollection", Guid.NewGuid().ToString("N"));
        var snapshots = new EmergencySnapshotService { EnableTypedRestore = true };
        var collector = new BaselineDomainCollector(NullLogger<BaselineDomainCollector>.Instance, snapshots);
        BaselineDomainDefinition domain = collector.GetDefinitions().Single(item => item.DomainId == "D03");

        BaselineDomainArtifact artifact = await collector.CollectDomainAsync(domain, packageDirectory);

        Assert.Equal(BaselineDomainStatus.Succeeded, artifact.Status);
        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(packageDirectory, artifact.RelativePath));
        ZipArchiveEntry manifestEntry = Assert.Single(archive.Entries, entry => entry.Name == "domain-manifest.json");
        using StreamReader reader = new(manifestEntry.Open());
        using JsonDocument manifest = JsonDocument.Parse(reader.ReadToEnd());
        string[] steps = manifest.RootElement.GetProperty("restoreSteps")
            .EnumerateArray()
            .Select(item => item.GetProperty("stepType").GetString() ?? string.Empty)
            .ToArray();

        Assert.Equal(["TypedSnapshot"], steps);
        Assert.Contains(archive.Entries, entry => entry.FullName == "payload/typed-settings-snapshot.json");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".reg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DomainDefinitions_OnlySoftwareCoreRecoveryDomainsBlockFirstBaselineAdmission()
    {
        var collector = new BaselineDomainCollector(
            NullLogger<BaselineDomainCollector>.Instance,
            new EmergencySnapshotService());
        var definitions = collector.GetDefinitions().ToDictionary(item => item.DomainId, StringComparer.OrdinalIgnoreCase);

        foreach (string domainId in new[] { "D03", "D04", "D05", "D06" })
        {
            Assert.True(definitions[domainId].Required, domainId + " must be a core baseline domain.");
        }

        foreach (string domainId in new[] { "D01", "D02", "D08", "D09", "D10", "D13", "D15", "D16", "D18", "D22" })
        {
            Assert.False(definitions[domainId].Required, domainId + " must remain an advisory/audit domain.");
        }
    }

    private static string CreatePackageDirectory(string stepType, bool optional, string domainId = "D03")
    {
        string directory = Path.Combine(@"F:\YuqiEngine\Verification\BaselineRestoreTruthfulness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string bundlePath = Path.Combine(directory, "D03.bundle.zip");
        using (ZipArchive archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "domain-manifest.json", JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                domainId,
                domainName = "恢复真实性测试域",
                required = false,
                items = new[] { new { itemType = "fixture" } },
                restoreSteps = new[]
                {
                    new
                    {
                        stepType,
                        sourceRelativePath = "payload/legacy.reg",
                        command = stepType == "RunCommand" ? "powercfg" : string.Empty,
                        optional,
                        description = "此步骤需要受控恢复器或人工完成。"
                    }
                }
            }));
            WriteEntry(archive, "payload/legacy.reg", "Windows Registry Editor Version 5.00");
        }
        return directory;
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        using StreamWriter writer = new(archive.CreateEntry(entryName).Open());
        writer.Write(content);
    }

    private sealed class FakeCatalog : IBaselineCatalogService
    {
        private readonly BaselinePackageManifest _manifest;
        private readonly string _packageDirectory;

        public FakeCatalog(BaselinePackageManifest manifest, string packageDirectory)
        {
            _manifest = manifest;
            _packageDirectory = packageDirectory;
        }

        public Task<IReadOnlyList<BaselineCatalogEntry>> GetEntriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BaselineCatalogEntry>>(Array.Empty<BaselineCatalogEntry>());

        public Task UpsertAsync(BaselinePackageManifest manifest, CancellationToken ct = default) => Task.CompletedTask;

        public Task<BaselinePackageManifest?> GetManifestAsync(string packageId, CancellationToken ct = default) =>
            Task.FromResult<BaselinePackageManifest?>(_manifest);

        public Task<(bool Success, string ErrorMessage)> VerifyPackageAsync(string packageId, CancellationToken ct = default) =>
            Task.FromResult((true, string.Empty));

        public Task<(bool Success, string ErrorMessage, string ExportPath)> ExportPackageAsync(string packageId, string outputDirectory, CancellationToken ct = default) =>
            Task.FromResult((false, "not used", string.Empty));

        public Task<string?> GetPackageDirectoryAsync(string packageId, CancellationToken ct = default) =>
            Task.FromResult<string?>(_packageDirectory);

        public Task<(bool Success, string ErrorMessage)> DeletePackageAsync(string packageId, CancellationToken ct = default) =>
            Task.FromResult((false, "not used"));
    }

    private sealed class EmergencySnapshotService : ISnapshotService
    {
        public bool EnableTypedRestore { get; init; }

        public bool FailTypedExecution { get; init; }

        public int ImportCount { get; private set; }

        public int PrepareCount { get; private set; }

        public int ExecuteCount { get; private set; }

        public List<string> DeletedSnapshotIds { get; } = new();

        public Task<OptimizationSnapshot> CreateSnapshotAsync(string name, string? description = null, CancellationToken ct = default) =>
            Task.FromResult(new OptimizationSnapshot { Id = "emergency-snapshot", Name = name, Description = description ?? string.Empty });

        public Task<SnapshotCoverageReport> GetCoverageReportAsync(CancellationToken ct = default) =>
            Task.FromResult(new SnapshotCoverageReport(0, 0, 0, 0, 0, Array.Empty<string>()));

        public Task<SnapshotRestorePlan> PrepareRestoreAsync(string snapshotId, SnapshotRestoreKind kind, CancellationToken ct = default)
        {
            if (!EnableTypedRestore)
            {
                return Task.FromException<SnapshotRestorePlan>(new NotSupportedException());
            }

            PrepareCount++;
            return Task.FromResult(new SnapshotRestorePlan(
                2,
                Guid.NewGuid(),
                snapshotId,
                kind,
                "identity",
                Array.Empty<SnapshotRestoreStepPlan>(),
                "plan",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1)));
        }

        public Task<SnapshotRestoreResult> ExecuteRestoreAsync(SnapshotRestorePlan plan, CancellationToken ct = default)
        {
            if (!EnableTypedRestore)
            {
                return Task.FromException<SnapshotRestoreResult>(new NotSupportedException());
            }

            ExecuteCount++;
            return Task.FromResult(new SnapshotRestoreResult
            {
                Success = !FailTypedExecution,
                Message = FailTypedExecution ? "typed restore readback failed" : "typed restore verified"
            });
        }

        public Task<RegistryBackupRestoreResult> ExecuteRegistryRestoreAsync(SnapshotRestorePlan plan, CancellationToken ct = default) =>
            Task.FromException<RegistryBackupRestoreResult>(new NotSupportedException());

        public Task<SnapshotRestoreRecoveryReport> ReconcileRestoreAsync(Guid planId, string planSha256, CancellationToken ct = default) =>
            Task.FromException<SnapshotRestoreRecoveryReport>(new NotSupportedException());

        public Task<SnapshotRestoreRecoveryReport> ResolveInterruptedRestoreAsync(Guid planId, string planSha256, SnapshotRestoreManualDecision decision, CancellationToken ct = default) =>
            Task.FromException<SnapshotRestoreRecoveryReport>(new NotSupportedException());

        public Task<SnapshotRestoreResult> RestoreSnapshotAsync(string snapshotId, CancellationToken ct = default) =>
            Task.FromException<SnapshotRestoreResult>(new NotSupportedException());

        public Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync() =>
            Task.FromResult<IReadOnlyList<SnapshotInfo>>(Array.Empty<SnapshotInfo>());

        public Task<SnapshotCatalogResult> GetSnapshotCatalogAsync(CancellationToken ct = default) =>
            Task.FromResult(new SnapshotCatalogResult(Array.Empty<SnapshotInfo>(), Array.Empty<SnapshotCatalogDiagnostic>()));

        public Task<IReadOnlyList<SnapshotRestoreBatchIdentity>> GetPendingRestoreBatchesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SnapshotRestoreBatchIdentity>>(Array.Empty<SnapshotRestoreBatchIdentity>());

        public Task<OptimizationSnapshot?> GetSnapshotAsync(string snapshotId) =>
            Task.FromResult<OptimizationSnapshot?>(null);

        public Task DeleteSnapshotAsync(string snapshotId)
        {
            DeletedSnapshotIds.Add(snapshotId);
            return Task.CompletedTask;
        }

        public Task ExportSnapshotAsync(string snapshotId, string filePath)
        {
            if (!EnableTypedRestore)
            {
                return Task.CompletedTask;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "{\"schemaVersion\":5,\"typed\":true}");
            return Task.CompletedTask;
        }

        public Task<OptimizationSnapshot?> ImportSnapshotAsync(string filePath)
        {
            if (!EnableTypedRestore)
            {
                return Task.FromResult<OptimizationSnapshot?>(null);
            }

            ImportCount++;
            return Task.FromResult<OptimizationSnapshot?>(new OptimizationSnapshot { Id = "imported-typed-snapshot" });
        }

        public string GetSnapshotDirectory() => Path.GetTempPath();

        public Task<RegistryBackupRestoreResult> RestoreRegistryBackupAsync(string snapshotId, CancellationToken ct = default) =>
            Task.FromException<RegistryBackupRestoreResult>(new NotSupportedException());

        public bool HasRegistryBackup(string snapshotId) => false;
    }
}
