// File: GameOptimizer.Tests/Shell/CommandResultPresentationTests.cs
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using CoreOperationStatus = GameOptimizer.Core.Optimization.OperationStatus;
using IOperationProgress = GameOptimizer.Core.Services.IOperationProgress;

namespace GameOptimizer.Tests.Shell;

public sealed class CommandResultPresentationTests
{
    [Fact]
    public void CommandExecutionGate_RejectsDuplicateAndReopensAfterCompletion()
    {
        var gate = new CommandExecutionGate();

        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
        Assert.True(gate.IsBusy);

        gate.Exit();

        Assert.False(gate.IsBusy);
        Assert.True(gate.TryEnter());
        gate.Exit();
    }

    [Fact]
    public async Task CommandExecutionGate_FakeCommandReleasesAfterCancellationAndException()
    {
        var gate = new CommandExecutionGate();

        async Task RunAndReleaseAsync(Task command)
        {
            Assert.True(gate.TryEnter());
            try
            {
                await command;
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                gate.Exit();
            }
        }

        await RunAndReleaseAsync(Task.FromCanceled(new CancellationToken(canceled: true)));
        Assert.False(gate.IsBusy);

        await RunAndReleaseAsync(Task.FromException(new InvalidOperationException("fake failure")));
        Assert.False(gate.IsBusy);
        Assert.True(gate.TryEnter());
        gate.Exit();
    }

    [Fact]
    public async Task RepairCommand_WhenAlreadyBusy_DoesNotInvokeCoreRepairAgain()
    {
        var repairService = new FakeRepairService();
        var viewModel = new GameOptimizer.Shell.ViewModels.RepairCenterViewModel(repairService, null!);
        var item = new GameOptimizer.Shell.ViewModels.RepairItemViewModel(CreateRepairItem());

        viewModel.IsRepairing = true;
        await viewModel.RepairItemCommand.ExecuteAsync(item);

        Assert.Equal(0, repairService.RepairCalls);
    }

    [Fact]
    public void OptimizationSuccessWithReboot_IsNotReportedAsPlainSuccess()
    {
        var summary = OptimizationSummary(
            success: true,
            changed: true,
            message: "Applied and verified.",
            after: new CoreOperationStatus(true, "Target reached.", RequiresReboot: true));

        var presentation = CommandResultPresentationMapper.FromOptimization(summary);

        Assert.Equal(CommandResultStatus.RequiresReboot, presentation.Status);
        Assert.True(presentation.IsSuccess);
        Assert.True(presentation.RequiresReboot);
    }

    [Fact]
    public void OptimizationReadbackRequiringRecovery_IsNotReportedAsSuccess()
    {
        var presentation = CommandResultPresentationMapper.FromOptimization(
            OptimizationSummary(
                success: true,
                changed: true,
                message: "The final state is unknown.",
                after: new CoreOperationStatus(
                    false,
                    "Rollback failed; manual recovery is required.",
                    RequiresRecovery: true)));

        Assert.Equal(CommandResultStatus.RequiresRecovery, presentation.Status);
        Assert.False(presentation.IsSuccess);
    }

    [Fact]
    public void MissingProfileReceipt_IsFailClosedAndNeverReportsSuccess()
    {
        var presentation = CommandResultPresentationMapper.FromMissingReceipt("基线恢复");

        Assert.Equal(CommandResultStatus.RequiresRecovery, presentation.Status);
        Assert.False(presentation.IsSuccess);
        Assert.Contains("未返回逐项执行回执", presentation.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("User cancelled. Final state was not verified.", CommandResultStatus.Cancelled)]
    [InlineData("Administrator permission is required.", CommandResultStatus.PermissionDenied)]
    [InlineData("This operation is not supported on this OS.", CommandResultStatus.Unsupported)]
    [InlineData("Rollback failed; manual recovery is required.", CommandResultStatus.RequiresRecovery)]
    [InlineData("Blocked by global safety policy.", CommandResultStatus.Blocked)]
    public void OptimizationFailure_UsesAuthoritativeFailureReason(
        string message,
        CommandResultStatus expectedStatus)
    {
        var presentation = CommandResultPresentationMapper.FromOptimization(
            OptimizationSummary(
                success: false,
                changed: false,
                message: message,
                after: new CoreOperationStatus(false, message)));

        Assert.Equal(expectedStatus, presentation.Status);
        Assert.False(presentation.IsSuccess);
        Assert.DoesNotContain(message, presentation.DisplayText, StringComparison.Ordinal);
        Assert.DoesNotContain(expectedStatus.ToString(), presentation.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairResultCodes_PreserveSuccessFailureCancellationPermissionAndUnsupportedStates()
    {
        var results = new[]
        {
            (RepairResult.Succeeded("Repair applied."), CommandResultStatus.Succeeded),
            (RepairResult.NoChange(), CommandResultStatus.NoChange),
            (RepairResult.Cancelled("Repair was cancelled."), CommandResultStatus.Cancelled),
            (RepairResult.PermissionDenied("Administrator permission is required."), CommandResultStatus.PermissionDenied),
            (RepairResult.VersionUnsupported("This OS is unsupported."), CommandResultStatus.Unsupported),
            (RepairResult.Blocked("Repair requires recovery material."), CommandResultStatus.RequiresRecovery),
            (RepairResult.Blocked("Repair is blocked by policy."), CommandResultStatus.Blocked)
        };

        foreach (var (result, expectedStatus) in results)
        {
            var presentation = CommandResultPresentationMapper.FromRepair(result);

            Assert.Equal(expectedStatus, presentation.Status);
            Assert.Equal(result.Success, presentation.IsSuccess);
            Assert.DoesNotContain(result.Message, presentation.DisplayText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RepairSuccessWithReboot_IsShownAsRebootRequired()
    {
        var presentation = CommandResultPresentationMapper.FromRepair(
            RepairResult.Succeeded("Repair applied.", requiresReboot: true));

        Assert.Equal(CommandResultStatus.RequiresReboot, presentation.Status);
        Assert.True(presentation.IsSuccess);
        Assert.DoesNotContain("RequiresReboot", presentation.DisplayText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Administrator permission is required.", CommandResultStatus.PermissionDenied)]
    [InlineData("This operation is not supported on this OS.", CommandResultStatus.Unsupported)]
    [InlineData("Rollback failed; manual recovery is required.", CommandResultStatus.RequiresRecovery)]
    public async Task MasterBatchEntry_PreservesCoreFailureStatusInErrorDetails(
        string message,
        CommandResultStatus expectedStatus)
    {
        var item = new OptimizationToggleModel(
            "Test operation",
            isEnabled: true,
            operationId: "test.operation");
        var summary = OptimizationSummary(
            success: false,
            changed: false,
            message,
            after: new CoreOperationStatus(false, message));

        var result = await new ThreadSafeBatchOperationService().ExecuteApplyAsync(
            new FakeOptimizationExecutor(summary),
            "test.module",
            new[] { item },
            progress: null,
            CancellationToken.None);

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Single(result.Errors);
        Assert.DoesNotContain(expectedStatus.ToString(), result.Errors[0].ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(message, result.Errors[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThreadSafeBatchService_ExecutesSelectedApplyItemsInOneExecutorBatch()
    {
        var items = new[]
        {
            new OptimizationToggleModel("First operation", isEnabled: true, operationId: "test.first"),
            new OptimizationToggleModel("Second operation", isEnabled: true, operationId: "test.second")
        };
        var executor = new CapturingBatchExecutor();

        var result = await new ThreadSafeBatchOperationService().ExecuteApplyAsync(
            executor,
            "test.module",
            items,
            progress: null,
            CancellationToken.None);

        Assert.Equal(1, executor.ApplyCallCount);
        Assert.Equal(new[] { "test.first", "test.second" }, executor.LastApplyOperationIds);
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(new[] { "test.first", "test.second" }, result.OperationSummaries.Select(summary => summary.OperationId));
    }

    [Fact]
    public async Task BatchChangeConfirmationScope_FlowsOnlyThroughTheConfirmedBatch()
    {
        Assert.False(BatchChangeConfirmationScope.IsActive);

        using (BatchChangeConfirmationScope.Begin())
        {
            Assert.True(BatchChangeConfirmationScope.IsActive);
            Assert.True(await Task.Run(() => BatchChangeConfirmationScope.IsActive));
        }

        Assert.False(BatchChangeConfirmationScope.IsActive);
    }

    [Fact]
    public void MasterModuleTemplate_DirectEntriesKeepUnifiedGateAndLifecycleHooks()
    {
        var sourcePath = FindRepositoryFile("GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("private readonly CommandExecutionGate _operationGate", source, StringComparison.Ordinal);
        Assert.Contains("private CancellationTokenSource? _operationCts", source, StringComparison.Ordinal);
        Assert.Contains("CancelActiveOperation();", source, StringComparison.Ordinal);
        Assert.Contains("TryBeginOperation(loc.Get(\"Module.Applying\"), out var operationToken)", source, StringComparison.Ordinal);
        Assert.Contains("TryBeginOperation(loc.Get(\"Module.Restoring\"), out var operationToken)", source, StringComparison.Ordinal);
        Assert.Contains("_threadSafeBatchService.ExecuteApplyAsync", source, StringComparison.Ordinal);
        Assert.Contains("_threadSafeBatchService.ExecuteRestoreAsync", source, StringComparison.Ordinal);
        Assert.Contains("EndOperation();", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardRestoreEntriesUseGateCancellationAndAuthoritativePresentation()
    {
        var source = File.ReadAllText(FindRepositoryFile("GameOptimizer.Shell", "ViewModels", "DashboardViewModel.cs"));

        var globalRestore = SliceMethod(source, "private async Task RestoreWindowsDefaultAsync()", "private Task RestoreUserBackupAsync()");
        Assert.Contains("TryBeginModuleOperation(out CancellationToken operationToken)", globalRestore, StringComparison.Ordinal);
        Assert.Contains("RestoreOperationsAsync(module.Key, operationIds, operationToken)", globalRestore, StringComparison.Ordinal);
        Assert.Contains("CommandResultPresentationMapper.FromOptimization(op)", globalRestore, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException", globalRestore, StringComparison.Ordinal);
        Assert.Contains("finally", globalRestore, StringComparison.Ordinal);
        Assert.Contains("EndModuleOperation();", globalRestore, StringComparison.Ordinal);
        Assert.DoesNotContain("_optimizationEngine.RestoreAsync", globalRestore, StringComparison.Ordinal);

        var baseline = SliceMethod(source, "private async Task RestoreModuleBaselineFromProfileAsync()", "private async Task RestoreModuleWindowsDefaultsAsync()");
        Assert.Contains("TryBeginModuleOperation(out CancellationToken operationToken)", baseline, StringComparison.Ordinal);
        Assert.Contains("operationToken", baseline, StringComparison.Ordinal);
        Assert.Contains("FromMissingReceipt", baseline, StringComparison.Ordinal);
        Assert.Contains("FromException", baseline, StringComparison.Ordinal);
        Assert.Contains("finally", baseline, StringComparison.Ordinal);
        Assert.DoesNotContain("BaselineRestoredFor", baseline, StringComparison.Ordinal);

        var defaults = SliceMethod(source, "private async Task RestoreModuleWindowsDefaultsAsync()", "private void SaveModuleSnapshot()");
        Assert.Contains("TryBeginModuleOperation(out CancellationToken operationToken)", defaults, StringComparison.Ordinal);
        Assert.Contains("operationToken", defaults, StringComparison.Ordinal);
        Assert.Contains("FromMissingReceipt", defaults, StringComparison.Ordinal);
        Assert.Contains("FromException", defaults, StringComparison.Ordinal);
        Assert.Contains("finally", defaults, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultsRestoredFor", defaults, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericRecoveryEntries_RouteToVerifiedBackupsAndNeverGuessWindowsDefaults()
    {
        var master = File.ReadAllText(FindRepositoryFile("GameOptimizer.Shell", "Views", "MasterModuleTemplate.xaml.cs"));
        var masterEntry = SliceMethod(master, "private Task OpenBackupManagerAsync()", "private async void Undo_ModuleChanges_Click");
        Assert.Contains("mainWindow.NavigateToPage(\"backup-manager\")", masterEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("TryBeginOperation", masterEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteRestoreAsync", masterEntry, StringComparison.Ordinal);

        var gameMode = File.ReadAllText(FindRepositoryFile("GameOptimizer.Shell", "Views", "GameModeHubPage.xaml.cs"));
        var gameModeEntry = SliceMethod(gameMode, "private void OpenBackupManager_Click", "游戏模式入口保持安全阻断");
        Assert.Contains("mainWindow.NavigateToPage(\"backup-manager\")", gameModeEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreWindowsDefaultCommand", gameModeEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("恢复完成", gameModeEntry, StringComparison.Ordinal);

        var dashboard = File.ReadAllText(FindRepositoryFile("GameOptimizer.Shell", "ViewModels", "DashboardViewModel.cs"));
        var dashboardEntry = SliceMethod(dashboard, "private async Task RestoreWindowsDefaultAsync()", "private Task RestoreUserBackupAsync()");
        Assert.Contains("t.IsApplied && t.CanRestore", dashboardEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("where !string.IsNullOrWhiteSpace(t.OperationId)\n", dashboardEntry, StringComparison.Ordinal);
    }

    private static OperationExecutionSummary OptimizationSummary(
        bool success,
        bool changed,
        string message,
        CoreOperationStatus after) =>
        new(
            "test.operation",
            "Test operation",
            success,
            changed,
            message,
            new CoreOperationStatus(false, "Before"),
            after);

    private static RepairItem CreateRepairItem() =>
        new()
        {
            Id = "test.repair",
            Name = "Test repair",
            Description = "Test repair",
            CheckHealthAsync = () => Task.FromResult(true),
            RepairAsync = _ => Task.FromResult(RepairResult.Succeeded("Repair applied."))
        };

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository source file was not found.", Path.Combine(pathParts));
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source marker: {endMarker}");
        return source[start..end];
    }

    private sealed class FakeOptimizationExecutor : IOptimizationExecutor
    {
        private readonly OperationExecutionSummary _summary;

        public FakeOptimizationExecutor(OperationExecutionSummary summary)
        {
            _summary = summary;
        }

        public Task<OptimizationBatchResult> ApplyOperationsAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            IOperationProgress? progress) =>
            Task.FromResult(CreateBatch(moduleKey));

        public Task<OptimizationBatchResult> ApplyOperationsAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            IOperationProgress? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateBatch(moduleKey));

        public Task<OptimizationBatchResult> RestoreOperationsAsync(
            string moduleKey,
            IEnumerable<string> operationIds) =>
            Task.FromResult(CreateBatch(moduleKey));

        public Task<OptimizationBatchResult> RestoreOperationsAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateBatch(moduleKey));

        private OptimizationBatchResult CreateBatch(string moduleKey) =>
            new(moduleKey, new[] { _summary });
    }

    private sealed class CapturingBatchExecutor : IOptimizationExecutor
    {
        public int ApplyCallCount { get; private set; }
        public IReadOnlyList<string> LastApplyOperationIds { get; private set; } = [];

        public Task<OptimizationBatchResult> ApplyOperationsAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            IOperationProgress? progress) =>
            ApplyOperationsAsync(moduleKey, operationIds, progress, CancellationToken.None);

        public Task<OptimizationBatchResult> ApplyOperationsAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            IOperationProgress? progress,
            CancellationToken cancellationToken)
        {
            ApplyCallCount++;
            LastApplyOperationIds = operationIds.ToList();
            var summaries = LastApplyOperationIds.Select(operationId => new OperationExecutionSummary(
                operationId,
                operationId,
                Success: true,
                Changed: true,
                Message: "Applied.",
                Before: new CoreOperationStatus(false, "Before"),
                After: new CoreOperationStatus(true, "After"))).ToList();
            return Task.FromResult(new OptimizationBatchResult(moduleKey, summaries));
        }

        public Task<OptimizationBatchResult> RestoreOperationsAsync(
            string moduleKey,
            IEnumerable<string> operationIds) =>
            RestoreOperationsAsync(moduleKey, operationIds, CancellationToken.None);

        public Task<OptimizationBatchResult> RestoreOperationsAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OptimizationBatchResult(moduleKey, []));
    }

    private sealed class FakeRepairService : IRepairService
    {
        public int RepairCalls { get; private set; }

        public IReadOnlyList<RepairItem> GetAllRepairItems() => [];

        public IEnumerable<RepairItem> GetByCategory(RepairCategory category) => [];

        public IEnumerable<RepairItem> Search(string query) => [];

        public Task<ScanResult> ScanForIssuesAsync(
            IProgress<ScanProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanResult());

        public Task<RepairResult> RepairAsync(
            RepairItem item,
            IProgress<RepairProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            RepairCalls++;
            return Task.FromResult(RepairResult.Succeeded("Repair applied."));
        }

        public Task<IReadOnlyList<RepairResult>> RepairAllAsync(
            IEnumerable<RepairItem> items,
            IProgress<RepairProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepairResult>>([]);

        public Task<RepairResult> RestoreAsync(
            RepairRecoveryRequest request,
            IProgress<RepairProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RepairResult.Succeeded("Restored."));

        public IReadOnlyList<RepairLogEntry> GetRepairHistory(int count = 50) => [];

        public void ClearHistory()
        {
        }
    }
}
