// File: GameOptimizer.Tests/Esports/EsportsModeWorkflowTests.cs
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Services.Baseline;
using CoreOperationStatus = GameOptimizer.Core.Optimization.OperationStatus;

namespace GameOptimizer.Tests.Esports;

public sealed class EsportsModeWorkflowTests
{
    private static readonly EsportsWorkflowOptions FastOptions = new(
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(50));

    [Fact]
    public async Task Apply_NormalAndDuplicateIds_BackupOnceExecuteOnceEachAndReadback()
    {
        var first = new FakeOperation("op.first", requiresAdmin: false);
        var second = new FakeOperation("op.second", requiresAdmin: false);
        var engine = new FakeEngine(first, second);
        engine.OnApply = (operation, _) =>
        {
            operation.IsCompliant = true;
            return Task.FromResult(SuccessBatch("esports", operation, changed: true));
        };
        var gate = new FakeBaselineGateService();
        var service = CreateService(engine, gate, isAdministrator: false);

        var plan = service.CreatePlan(
            new[] { first.Id, first.Id.ToUpperInvariant(), second.Id },
            EsportsWorkflowAction.Apply);
        var report = await service.ExecuteApplyPlanAsync(plan);

        Assert.True(plan.CanExecute);
        Assert.Equal(2, plan.Items.Count);
        Assert.True(report.Success);
        Assert.Equal(1, gate.EnsureCalls);
        Assert.Equal(new[] { first.Id, second.Id }, engine.ApplyCalls);
        Assert.All(report.Results, result =>
        {
            Assert.Equal(EsportsExecutionOutcome.Succeeded, result.Outcome);
            Assert.NotNull(result.ReadbackStatus);
            Assert.True(result.ReadbackStatus!.IsCompliant);
        });
    }

    [Fact]
    public async Task Apply_MissingAdministrator_BlocksWholePlanBeforeBackupAndWrite()
    {
        var safe = new FakeOperation("op.safe", requiresAdmin: false);
        var elevated = new FakeOperation("op.admin", requiresAdmin: true);
        var engine = new FakeEngine(safe, elevated);
        var gate = new FakeBaselineGateService();
        var service = CreateService(engine, gate, isAdministrator: false);

        var plan = service.CreatePlan(new[] { safe.Id, elevated.Id }, EsportsWorkflowAction.Apply);
        var report = await service.ExecuteApplyPlanAsync(plan);

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.RejectedItems, result => result.FailureKind == EsportsFailureKind.PermissionDenied);
        Assert.Equal(0, gate.EnsureCalls);
        Assert.Empty(engine.ApplyCalls);
        Assert.All(report.Results, result => Assert.Equal(EsportsExecutionOutcome.Blocked, result.Outcome));
    }

    [Fact]
    public async Task Apply_RiskConfirmationDeclined_BlocksAllWrites()
    {
        var operation = new FakeOperation("op.one", requiresAdmin: false);
        var engine = new FakeEngine(operation);
        var gate = new FakeBaselineGateService { Result = false };
        var service = CreateService(engine, gate, isAdministrator: false);
        var plan = service.CreatePlan(new[] { operation.Id }, EsportsWorkflowAction.Apply);

        var report = await service.ExecuteApplyPlanAsync(plan);

        Assert.False(report.BackupGate.Allowed);
        Assert.Equal(EsportsFailureKind.SafetyConfirmationDeclined, report.BackupGate.FailureKind);
        Assert.Empty(engine.ApplyCalls);
        Assert.Single(report.Results);
        Assert.Equal(EsportsExecutionOutcome.Blocked, report.Results[0].Outcome);
    }

    [Fact]
    public async Task Apply_MissingBaselineAcknowledged_ExecutesWithoutCreatingBackup()
    {
        var operation = new FakeOperation("op.one", requiresAdmin: false);
        var engine = new FakeEngine(operation);
        engine.OnApply = (item, _) =>
        {
            item.IsCompliant = true;
            return Task.FromResult(SuccessBatch("esports", item, changed: true));
        };
        var gate = new FakeBaselineGateService
        {
            DecisionKind = BaselineGateDecisionKind.AllowedWithRiskAcknowledgement
        };
        var service = CreateService(engine, gate, isAdministrator: false);
        var plan = service.CreatePlan(new[] { operation.Id }, EsportsWorkflowAction.Apply);

        var report = await service.ExecuteApplyPlanAsync(plan);

        Assert.True(report.BackupGate.Allowed);
        Assert.True(report.Success);
        Assert.Equal(1, gate.EnsureCalls);
        Assert.Single(engine.ApplyCalls);
    }

    [Fact]
    public async Task Apply_CancelDuringFirstItem_CancelsCurrentAndUnstartedItems()
    {
        var first = new FakeOperation("op.first", requiresAdmin: false);
        var second = new FakeOperation("op.second", requiresAdmin: false);
        var engine = new FakeEngine(first, second)
        {
            OnApply = static async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("unreachable");
            }
        };
        var service = CreateService(engine, new FakeBaselineGateService(), isAdministrator: false);
        var plan = service.CreatePlan(new[] { first.Id, second.Id }, EsportsWorkflowAction.Apply);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        var report = await service.ExecuteApplyPlanAsync(plan, cancellationToken: cancellation.Token);

        Assert.True(report.WasCancelled);
        Assert.Equal(new[] { first.Id }, engine.ApplyCalls);
        Assert.Equal(EsportsExecutionOutcome.Cancelled, report.Results[0].Outcome);
        Assert.Equal(EsportsExecutionOutcome.Cancelled, report.Results[1].Outcome);
    }

    [Fact]
    public async Task Apply_OperationTimeout_StopsLaterWrites()
    {
        var first = new FakeOperation("op.first", requiresAdmin: false);
        var second = new FakeOperation("op.second", requiresAdmin: false);
        var never = new TaskCompletionSource<OptimizationBatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new FakeEngine(first, second)
        {
            OnApply = (_, _) => never.Task
        };
        var service = CreateService(engine, new FakeBaselineGateService(), isAdministrator: false);
        var plan = service.CreatePlan(new[] { first.Id, second.Id }, EsportsWorkflowAction.Apply);

        var report = await service.ExecuteApplyPlanAsync(plan);

        Assert.Equal(new[] { first.Id }, engine.ApplyCalls);
        Assert.Equal(EsportsExecutionOutcome.TimedOut, report.Results[0].Outcome);
        Assert.Equal(EsportsFailureKind.ExecutionTimedOut, report.Results[0].FailureKind);
        Assert.Equal(EsportsExecutionOutcome.Blocked, report.Results[1].Outcome);
        Assert.Equal(EsportsFailureKind.BlockedByPriorFailure, report.Results[1].FailureKind);
    }

    [Fact]
    public async Task Apply_PartialFailure_PreservesPerItemResultsAndBlocksRemainder()
    {
        var first = new FakeOperation("op.first", requiresAdmin: false);
        var second = new FakeOperation("op.second", requiresAdmin: false);
        var third = new FakeOperation("op.third", requiresAdmin: false);
        var engine = new FakeEngine(first, second, third);
        engine.OnApply = (operation, _) =>
        {
            if (operation == first)
            {
                operation.IsCompliant = true;
                return Task.FromResult(SuccessBatch("esports", operation, changed: true));
            }

            return Task.FromResult(FailedBatch("esports", operation, "fake failure"));
        };
        var service = CreateService(engine, new FakeBaselineGateService(), isAdministrator: false);
        var plan = service.CreatePlan(new[] { first.Id, second.Id, third.Id }, EsportsWorkflowAction.Apply);

        var report = await service.ExecuteApplyPlanAsync(plan);

        Assert.Equal(new[] { first.Id, second.Id }, engine.ApplyCalls);
        Assert.Equal(EsportsExecutionOutcome.Succeeded, report.Results[0].Outcome);
        Assert.Equal(EsportsExecutionOutcome.Failed, report.Results[1].Outcome);
        Assert.Equal("fake failure", report.Results[1].Message);
        Assert.Equal(EsportsExecutionOutcome.Blocked, report.Results[2].Outcome);
    }

    [Fact]
    public async Task Apply_ReadbackMismatch_FailsAndBlocksRemainder()
    {
        var first = new FakeOperation("op.first", requiresAdmin: false);
        var second = new FakeOperation("op.second", requiresAdmin: false);
        var engine = new FakeEngine(first, second)
        {
            OnApply = (operation, _) => Task.FromResult(SuccessBatch("esports", operation, changed: true))
        };
        var service = CreateService(engine, new FakeBaselineGateService(), isAdministrator: false);
        var plan = service.CreatePlan(new[] { first.Id, second.Id }, EsportsWorkflowAction.Apply);

        var report = await service.ExecuteApplyPlanAsync(plan);

        Assert.Equal(new[] { first.Id }, engine.ApplyCalls);
        Assert.Equal(EsportsFailureKind.ReadbackFailed, report.Results[0].FailureKind);
        Assert.Equal(EsportsFailureKind.BlockedByPriorFailure, report.Results[1].FailureKind);
    }

    [Fact]
    public async Task Restore_DoesNotRequireBaselineRiskGateAndReadbackFormsRecoveryLoop()
    {
        var first = new FakeOperation("op.first", requiresAdmin: false) { IsCompliant = true };
        var second = new FakeOperation("op.second", requiresAdmin: false) { IsCompliant = true };
        var engine = new FakeEngine(first, second);
        engine.OnRestore = (operation, _) =>
        {
            operation.IsCompliant = false;
            return Task.FromResult(SuccessBatch("esports", operation, changed: true));
        };
        var gate = new FakeBaselineGateService { Result = false };
        var service = CreateService(engine, gate, isAdministrator: false);
        var plan = service.CreatePlan(new[] { first.Id, second.Id }, EsportsWorkflowAction.Restore);

        var report = await service.ExecuteRestorePlanAsync(plan);

        Assert.True(report.Success);
        Assert.Equal(0, gate.EnsureCalls);
        Assert.Equal(new[] { first.Id, second.Id }, engine.RestoreCalls);
        Assert.All(report.Results, result =>
        {
            Assert.NotNull(result.ReadbackStatus);
            Assert.False(result.ReadbackStatus!.IsCompliant);
        });
    }

    [Fact]
    public async Task Detect_TimeoutAndFailure_AreExplicitAndReportIsIncomplete()
    {
        var timeout = new FakeOperation("op.timeout", requiresAdmin: false)
        {
            OnStatus = _ => new TaskCompletionSource<CoreOperationStatus>().Task
        };
        var failure = new FakeOperation("op.failure", requiresAdmin: false)
        {
            OnStatus = _ => Task.FromException<CoreOperationStatus>(new InvalidOperationException("probe failed"))
        };
        var service = CreateService(new FakeEngine(timeout, failure), new FakeBaselineGateService(), isAdministrator: false);

        var report = await service.DetectAllStatesAsync(new[] { timeout.Id, failure.Id });

        Assert.False(report.IsComplete);
        Assert.Empty(report.States);
        Assert.Equal(EsportsProbeOutcome.TimedOut, report.Items[0].Outcome);
        Assert.Equal(EsportsProbeOutcome.Failed, report.Items[1].Outcome);
    }

    [Fact]
    public async Task Detect_MissingOperation_IsUnavailableButDoesNotLeavePlanningInLoadingState()
    {
        var available = new FakeOperation("op.available", requiresAdmin: false);
        var service = CreateService(
            new FakeEngine(available),
            new FakeBaselineGateService(),
            isAdministrator: false);

        var report = await service.DetectAllStatesAsync(new[] { available.Id, "op.not_registered" });

        Assert.False(report.IsComplete);
        Assert.True(report.IsReadyForPlanning);
        Assert.Equal(EsportsProbeOutcome.Detected, report.Items[0].Outcome);
        Assert.Equal(EsportsProbeOutcome.MissingOperation, report.Items[1].Outcome);
    }

    private static EsportsModeStateService CreateService(
        FakeEngine engine,
        IBaselineGateService baselineGate,
        bool isAdministrator)
    {
        return new EsportsModeStateService(engine, baselineGate, () => isAdministrator, FastOptions);
    }

    private static OptimizationBatchResult SuccessBatch(string moduleKey, FakeOperation operation, bool changed)
    {
        var before = new CoreOperationStatus(!operation.IsCompliant, "before");
        var after = new CoreOperationStatus(operation.IsCompliant, "after");
        return new OptimizationBatchResult(moduleKey, new[]
        {
            new OperationExecutionSummary(operation.Id, operation.Title, true, changed, "ok", before, after)
        });
    }

    private static OptimizationBatchResult FailedBatch(string moduleKey, FakeOperation operation, string message)
    {
        var status = new CoreOperationStatus(operation.IsCompliant, "unchanged");
        return new OptimizationBatchResult(moduleKey, new[]
        {
            new OperationExecutionSummary(operation.Id, operation.Title, false, false, message, status, status)
        });
    }

    private sealed class FakeBaselineGateService : IBaselineGateService
    {
        public bool Result { get; init; } = true;
        public BaselineGateDecisionKind DecisionKind { get; init; } = BaselineGateDecisionKind.Allowed;
        public Exception? Exception { get; init; }
        public int EnsureCalls { get; private set; }

        public Task<BaselineGateDecision> EnsureCanOptimizeAsync(string entryPoint, CancellationToken ct = default)
        {
            EnsureCalls++;
            return Exception == null
                ? Task.FromResult(new BaselineGateDecision(
                    Result ? DecisionKind : BaselineGateDecisionKind.BlockedRiskNotAcknowledged,
                    Result ? "risk acknowledged" : "risk acknowledgement declined",
                    new BaselineSafetyState { FirstRunState = FirstRunBaselineState.NotStarted },
                    Result ? new string('E', 64) : null))
                : Task.FromException<BaselineGateDecision>(Exception);
        }

        public Task<BaselineSafetyState> GetStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new BaselineSafetyState());
        public Task SetReadonlyModeAsync(bool isReadonly, string entryPoint, string? reason = null, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task MarkBaselineCompletedVerifiedAsync(string entryPoint, string packageId, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task MarkBaselineFailureAsync(string entryPoint, FirstRunBaselineState state, string? reason = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeEngine : IOptimizationEngine
    {
        private readonly Dictionary<string, FakeOperation> _operations;

        public FakeEngine(params FakeOperation[] operations)
        {
            _operations = operations.ToDictionary(operation => operation.Id, StringComparer.OrdinalIgnoreCase);
            Modules = new[]
            {
                new OptimizationModuleDescriptor("esports", "Esports", "fake", operations)
            };
        }

        public Func<FakeOperation, CancellationToken, Task<OptimizationBatchResult>>? OnApply { get; set; }
        public Func<FakeOperation, CancellationToken, Task<OptimizationBatchResult>>? OnRestore { get; set; }
        public List<string> ApplyCalls { get; } = new();
        public List<string> RestoreCalls { get; } = new();
        public IReadOnlyList<OptimizationModuleDescriptor> Modules { get; }

        public Task<OptimizationBatchResult> ApplyAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            ApplyAsync(moduleKey, _operations.Keys, cancellationToken);

        public Task<OptimizationBatchResult> ApplyAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            CancellationToken cancellationToken = default) =>
            ApplyAsync(moduleKey, operationIds, progress: null, cancellationToken);

        public Task<OptimizationBatchResult> ApplyAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            IOperationProgress? progress,
            CancellationToken cancellationToken = default)
        {
            var operation = ResolveSingle(operationIds);
            ApplyCalls.Add(operation.Id);
            return OnApply?.Invoke(operation, cancellationToken)
                ?? Task.FromResult(SuccessBatch(moduleKey, operation, changed: false));
        }

        public Task<OptimizationBatchResult> RestoreAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            RestoreAsync(moduleKey, _operations.Keys, cancellationToken);

        public Task<OptimizationBatchResult> RestoreAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            CancellationToken cancellationToken = default) =>
            RestoreAsync(moduleKey, operationIds, progress: null, cancellationToken);

        public Task<OptimizationBatchResult> RestoreAsync(
            string moduleKey,
            IEnumerable<string> operationIds,
            IOperationProgress? progress,
            CancellationToken cancellationToken = default)
        {
            var operation = ResolveSingle(operationIds);
            RestoreCalls.Add(operation.Id);
            return OnRestore?.Invoke(operation, cancellationToken)
                ?? Task.FromResult(SuccessBatch(moduleKey, operation, changed: false));
        }

        public Task<IReadOnlyList<CoreOperationStatus>> GetStatusesAsync(
            string moduleKey,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CoreOperationStatus> statuses = _operations.Values
                .Select(operation => new CoreOperationStatus(operation.IsCompliant, "fake"))
                .ToList();
            return Task.FromResult(statuses);
        }

        private FakeOperation ResolveSingle(IEnumerable<string> operationIds)
        {
            var id = Assert.Single(operationIds);
            return _operations[id];
        }
    }

    private sealed class FakeOperation : IOptimizationOperation
    {
        public FakeOperation(string id, bool requiresAdmin)
        {
            Id = id;
            RequiresAdmin = requiresAdmin;
        }

        public string Id { get; }
        public string Title => Id;
        public string Description => "fake";
        public string Category => "Esports";
        public OperationRiskLevel RiskLevel => OperationRiskLevel.Safe;
        public bool RequiresAdmin { get; }
        public bool IsCompliant { get; set; }
        public Func<CancellationToken, Task<CoreOperationStatus>>? OnStatus { get; init; }

        public Task<CoreOperationStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            return OnStatus?.Invoke(cancellationToken)
                ?? Task.FromResult(new CoreOperationStatus(IsCompliant, "fake readback"));
        }

        public Task<OperationInvokeResult> ApplyAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationInvokeResult> RestoreAsync(
            OperationExecutionContext context,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
