using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Services.SystemCleanup;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class OrchestratedFileCleanupOperationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Apply_EmptyPreviewSucceedsWithoutMutation()
    {
        var service = new FakeCleanupService(previewEntryCount: 0);
        var operation = CreateOperation(service);

        var result = await operation.ApplyAsync(NewContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Contains("没有需要清理的文件", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_HappyPathIssuesConfirmationAndQuarantinesEveryFile()
    {
        var service = new FakeCleanupService(previewEntryCount: 3)
        {
            Result = SuccessfulCleanResult(deleted: 3, bytes: 4096)
        };
        var gate = new FakeAuthorizationGate(allowed: true);
        var operation = CreateOperation(service, authorizationGate: gate);

        var result = await operation.ApplyAsync(NewContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(1, service.CleanCalls);
        Assert.NotNull(service.LastConfirmation);
        Assert.True(
            service.LastConfirmation!.Matches(service.LastPreview!, Now.AddSeconds(5)),
            "操作签发的确认凭据必须与预览绑定。");
        Assert.True(gate.AuthorizeCalls >= 1);
        Assert.Contains("隔离区", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_AuthorizationDenialBlocksBeforeAnyFileOperation()
    {
        var service = new FakeCleanupService(previewEntryCount: 2);
        var gate = new FakeAuthorizationGate(allowed: false, reasonCode: "baseline.denied");
        var operation = CreateOperation(service, authorizationGate: gate);

        var result = await operation.ApplyAsync(NewContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
        Assert.Equal(0, service.CleanCalls);
        Assert.Contains("baseline.denied", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_PartialFailureReportsMutationAndKeepsRecoveryPath()
    {
        var service = new FakeCleanupService(previewEntryCount: 4)
        {
            Result = PartialFailureCleanResult(deleted: 2, failed: 2)
        };
        var operation = CreateOperation(service);

        var result = await operation.ApplyAsync(NewContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Changed);
        Assert.Contains("2 个失败", result.Message, StringComparison.Ordinal);
        Assert.Contains("恢复流程找回", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_MissingServiceOrItemFailsClosed()
    {
        var withoutService = CreateOperation(cleanupService: null);
        var withoutServiceResult = await withoutService.ApplyAsync(NewContext(), CancellationToken.None);
        Assert.False(withoutServiceResult.Success);
        Assert.False(withoutServiceResult.Changed);

        var service = new FakeCleanupService(previewEntryCount: 1);
        var unknownItem = CreateOperation(service, cleanupItemId: "not_in_catalog");
        var unknownResult = await unknownItem.ApplyAsync(NewContext(), CancellationToken.None);
        Assert.False(unknownResult.Success);
    }

    [Fact]
    public async Task GetStatus_ReportsPendingFilesAsNotCompliant()
    {
        var pending = CreateOperation(new FakeCleanupService(previewEntryCount: 5));
        var pendingStatus = await pending.GetStatusAsync(CancellationToken.None);
        Assert.False(pendingStatus.IsCompliant);
        Assert.Contains("5 个可清理文件", pendingStatus.Details, StringComparison.Ordinal);

        var clean = CreateOperation(new FakeCleanupService(previewEntryCount: 0));
        var cleanStatus = await clean.GetStatusAsync(CancellationToken.None);
        Assert.True(cleanStatus.IsCompliant);
    }

    [Fact]
    public async Task Restore_DelegatesToCentralQuarantineAndReportsHonestOutcome()
    {
        var quarantine = new FakeQuarantineManager { RestoredCount = 3, FailedCount = 0 };
        var operation = CreateOperation(new FakeCleanupService(0), quarantineManager: quarantine);

        var restored = await operation.RestoreAsync(NewContext(), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.True(restored.Changed);
        Assert.Equal(1, quarantine.RestoreCalls);
        Assert.NotNull(quarantine.LastConfirmation);

        var failing = new FakeQuarantineManager { RestoredCount = 1, FailedCount = 2 };
        var failingOperation = CreateOperation(new FakeCleanupService(0), quarantineManager: failing);
        var failingResult = await failingOperation.RestoreAsync(NewContext(), CancellationToken.None);

        Assert.False(failingResult.Success);
        Assert.True(failingResult.Changed);
    }

    [Fact]
    public void Descriptor_ExposesExplicitConfirmationAndAdministratorMetadata()
    {
        var operation = CreateOperation(new FakeCleanupService(0));

        Assert.True(operation.RequiresExplicitConfirmation);
        Assert.True(operation.RequiresAdmin);
        Assert.Equal(OperationRiskLevel.Safe, operation.RiskLevel);
        Assert.Equal("cleanup.gpu_cache", operation.Id);
    }

    private static OrchestratedFileCleanupOperation CreateOperation(
        ISystemCleanupService? cleanupService,
        ICleanupQuarantineManager? quarantineManager = null,
        ICleanupAuthorizationGate? authorizationGate = null,
        string cleanupItemId = "gpu_cache") =>
        new(
            new StubChangeOrchestrator(),
            "cleanup.gpu_cache",
            "清理 GPU 着色器缓存",
            "清理 NVIDIA/AMD/D3DSCache 着色器缓存目录。",
            "系统清理",
            OperationRiskLevel.Safe,
            cleanupItemId,
            "释放磁盘空间，修复着色器异常",
            "下次游戏启动需重新编译着色器，首次加载变慢",
            cleanupService,
            quarantineManager,
            authorizationGate,
            TimeProvider.System);

    private static OperationExecutionContext NewContext() =>
        new(new CountingRestoreStore(), NullLogger.Instance);

    private static CleanupResult SuccessfulCleanResult(int deleted, long bytes) => new()
    {
        TotalCleaned = bytes,
        FilesDeleted = deleted,
        FilesFailed = 0,
        Errors = [],
        FileResults = []
    };

    private static CleanupResult PartialFailureCleanResult(int deleted, int failed) => new()
    {
        TotalCleaned = deleted * 100L,
        FilesDeleted = deleted,
        FilesFailed = failed,
        Errors = [$"{failed} 个文件被占用"],
        FileResults = []
    };

    private sealed class StubChangeOrchestrator : IChangeOrchestrator
    {
        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<StateObservation?>(null);

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken, bool registerPlan = true) =>
            Task.FromResult(new PreparationResult(ChangePreparationStatus.Blocked, null, "test.blocked", "Blocked in unit test.", null));

        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CountingRestoreStore : IOperationRestoreStore
    {
        public int Calls { get; private set; }
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.FromResult<string?>(null); }
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.FromResult(false); }
    }

    private sealed class FakeCleanupService(int previewEntryCount) : ISystemCleanupService
    {
        public CleanupResult Result { get; set; } = SuccessfulStaticResult();
        public int CleanCalls { get; private set; }
        public CleanupPreview? LastPreview { get; private set; }
        public CleanupPreviewConfirmation? LastConfirmation { get; private set; }

        public IReadOnlyList<CleanupItem> GetCleanupItems() =>
        [
            new CleanupItem
            {
                Id = "gpu_cache",
                Name = "GPU 缓存",
                Description = "测试项",
                TargetPaths = [Path.Combine(Path.GetTempPath(), "dxcache")]
            }
        ];

        public Task<CleanupPreview> CreatePreviewAsync(CleanupItem item, CancellationToken ct = default)
        {
            var entries = Enumerable.Range(0, previewEntryCount)
                .Select(index => new CleanupPreviewEntry(
                    $@"C:\fake\dxcache\file{index}.bin",
                    @"C:\fake\dxcache",
                    128,
                    DateTime.UtcNow.AddHours(-index)))
                .ToList();
            var preview = new CleanupPreview(item.Id, entries, Now);
            LastPreview = preview;
            return Task.FromResult(preview);
        }

        public Task<CleanupResult> CleanPreviewAsync(
            CleanupItem item,
            CleanupPreview preview,
            CleanupPreviewConfirmation confirmation,
            CancellationToken ct = default)
        {
            CleanCalls++;
            LastConfirmation = confirmation;
            return Task.FromResult(Result);
        }

        public void InvalidatePreview(CleanupPreview preview) { }

        public Task<long> ScanItemAsync(CleanupItem item, CancellationToken ct = default) => Task.FromResult(0L);

        public Task<CleanupResult> CleanItemAsync(CleanupItem item, CancellationToken ct = default) =>
            throw new NotSupportedException("编排操作必须走预览+确认路径。");

        private static CleanupResult SuccessfulStaticResult() => new()
        {
            TotalCleaned = 0,
            FilesDeleted = 0,
            FilesFailed = 0,
            Errors = [],
            FileResults = []
        };
    }

    private sealed class FakeAuthorizationGate(bool allowed, string reasonCode = "allowed") : ICleanupAuthorizationGate
    {
        public int AuthorizeCalls { get; private set; }

        public Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            return Task.FromResult(new CleanupAuthorizationDecision(
                allowed,
                reasonCode,
                allowed ? "授权通过。" : "基线门禁拒绝。"));
        }
    }

    private sealed class FakeQuarantineManager : ICleanupQuarantineManager
    {
        public int RestoredCount { get; set; }
        public int FailedCount { get; set; }
        public int RestoreCalls { get; private set; }
        public CleanupQuarantineConfirmation? LastConfirmation { get; private set; }

        public Task<CleanupQuarantinePreview> CreateRestorePreviewAsync(CancellationToken cancellationToken = default) =>
            CreateRestorePreviewAsync(_ => true, cancellationToken);

        public Task<CleanupQuarantinePreview> CreateRestorePreviewAsync(Func<CleanupQuarantineEntry, bool> scope, CancellationToken cancellationToken = default)
        {
            var entries = new List<CleanupQuarantineEntry>();
            for (var index = 0; index < RestoredCount + FailedCount; index++)
            {
                entries.Add(new CleanupQuarantineEntry(
                    $@"C:\fake\quarantine\batch{index}\entry{index}.manifest.json",
                    MakeManifest(index)));
            }

            return Task.FromResult(new CleanupQuarantinePreview("restore", entries.Where(scope), Now));
        }

        public Task<CleanupQuarantineResult> RestoreAsync(
            CleanupQuarantinePreview preview,
            CleanupQuarantineConfirmation confirmation,
            CancellationToken cancellationToken = default)
        {
            RestoreCalls++;
            LastConfirmation = confirmation;
            var result = new CleanupQuarantineResult();
            foreach (var entry in preview.Entries)
            {
                var outcome = result.Items.Count < RestoredCount
                    ? CleanupQuarantineOutcome.Restored
                    : CleanupQuarantineOutcome.ContentMismatch;
                result.Items.Add(new CleanupQuarantineItemResult(
                    entry.Manifest.OperationId,
                    entry.Manifest.OriginalPath,
                    outcome,
                    outcome == CleanupQuarantineOutcome.Restored ? "restored" : "content-mismatch",
                    "fake"));
            }

            return Task.FromResult(result);
        }

        public Task<CleanupQuarantinePreview> CreateRetentionPreviewAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CleanupQuarantinePreview("retention-stage", [], Now));

        public Task<CleanupQuarantineResult> StageRetentionAsync(CleanupQuarantinePreview preview, CleanupQuarantineConfirmation confirmation, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CleanupQuarantineResult());

        public Task<CleanupQuarantineReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CleanupQuarantineReconciliationResult());

        // Why: 恢复按所选清理项的 TargetPaths 收窄范围，清单的原始路径
        // 必须位于该项的目标根之内才会被恢复。
        private static CleanupQuarantineManifest MakeManifest(int index) => new(
            2,
            $"op-{index}",
            Guid.NewGuid(),
            "gpu_cache",
            Path.Combine(Path.GetTempPath(), "dxcache", $"file{index}.bin"),
            Path.Combine(Path.GetTempPath(), "dxcache"),
            $@"C:\fake\quarantine\q{index}.bin",
            256,
            DateTime.UtcNow.AddDays(-1),
            new string('A', 64),
            "cleanup.resource.fake",
            CleanupQuarantineState.QuarantinedVerified,
            null,
            Now,
            [],
            new string('B', 64));
    }
}
