using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Optimization;

public sealed class RegistryOperationLifecycleTests
{
    [Fact]
    public async Task Detect_Apply_Verify_Restore_RegistryDword_CompletesFullCycle()
    {
        var orchestrator = new FakeRegistryOrchestrator();
        var operation = new OrchestratedRegistryDwordOperation(
            orchestrator,
            "lifecycle.test.dword",
            "测试 DWORD 优化",
            "用于验证检测-应用-回读-恢复完整生命周期。",
            "lifecycle",
            @"HKEY_CURRENT_USER\Software\YuqiEngineTest",
            "TestValue",
            1,
            OperationRiskLevel.Safe,
            "优化测试",
            "无",
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: false);

        var context = new OperationExecutionContext(new InMemoryRestoreStore(), NullLogger.Instance);

        orchestrator.CurrentValue = "0";

        OperationStatus before = await operation.GetStatusAsync(default);
        Assert.False(before.IsCompliant, "当前值不是优化值时应为未优化。");

        OperationInvokeResult apply = await operation.ApplyAsync(context, default);
        Assert.True(apply.Success, apply.Message);
        Assert.True(apply.Changed, "应用成功后应报告状态已改变。");

        OperationStatus after = await operation.GetStatusAsync(default);
        Assert.True(after.IsCompliant, "应用后读取应确认已到达优化值。");

        OperationInvokeResult restore = await operation.RestoreAsync(context, default);
        Assert.True(restore.Success, restore.Message);
        Assert.True(restore.Changed, "恢复成功后应报告状态已回滚。");

        OperationStatus final = await operation.GetStatusAsync(default);
        Assert.False(final.IsCompliant, "恢复后应回到原始值。");
        Assert.Equal("0", orchestrator.CurrentValue);
    }

    private sealed class InMemoryRestoreStore : IOperationRestoreStore
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);

        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken)
        {
            _store[operationId] = payload;
            return Task.CompletedTask;
        }

        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(_store.TryGetValue(operationId, out string? value) ? value : null);

        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken)
        {
            _store.Remove(operationId);
            return Task.CompletedTask;
        }

        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) =>
            Task.FromResult(_store.ContainsKey(operationId));
    }

    private sealed class FakeRegistryOrchestrator : IChangeOrchestrator
    {
        private readonly Dictionary<Guid, string> _receiptOriginalValue = new();
        private string _currentValue = "0";

        public string CurrentValue
        {
            get => _currentValue;
            set => _currentValue = value;
        }

        public Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<StateObservation?>(new StateObservation(
                scope,
                _currentValue,
                _currentValue,
                DateTimeOffset.UtcNow,
                "fake",
                "test"));

        public Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken, bool registerPlan = true)
        {
            bool noChange = string.Equals(_currentValue, request.TargetPayload, StringComparison.Ordinal);
            if (noChange)
            {
                return Task.FromResult(new PreparationResult(
                    ChangePreparationStatus.NoChange,
                    null,
                    "capability.already-optimized",
                    "当前已经是优化值。",
                    new StateObservation(request.Scope, _currentValue, _currentValue, DateTimeOffset.UtcNow, "fake", "test")));
            }

            var change = new PreparedCapabilityChange(
                request.CapabilityId,
                "1.0",
                request.Scope,
                _currentValue,
                _currentValue,
                request.TargetPayload,
                request.TargetPayload,
                request.CapabilityId);

            var plan = new PreparedChangePlan(
                Guid.NewGuid(),
                change,
                $"{change.BeforeFingerprint}:{change.TargetFingerprint}",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(5),
                RequiresAdministrator: false,
                RequiresExplicitConfirmation: false,
                RequiresRecoveryMaterial: true,
                ChangeRisk.LowReversible);

            return Task.FromResult(new PreparationResult(
                ChangePreparationStatus.Ready,
                plan,
                "capability.ready",
                "准备就绪。",
                new StateObservation(request.Scope, _currentValue, _currentValue, DateTimeOffset.UtcNow, "fake", "test")));
        }

        public Task<BatchReceipt> ExecuteAsync(PreparedChangePlan plan, ConfirmationToken confirmation, CancellationToken cancellationToken)
        {
            _currentValue = plan.Change.TargetPayload;
            Guid receiptId = Guid.NewGuid();
            Guid recoveryId = Guid.NewGuid();
            _receiptOriginalValue[receiptId] = plan.Change.BeforePayload;

            return Task.FromResult(new BatchReceipt(new OperationReceipt(
                receiptId,
                plan.PlanId,
                recoveryId,
                plan.Change.CapabilityId,
                plan.Change.Scope,
                ChangeOutcome.SucceededVerified,
                plan.Change.BeforeFingerprint,
                plan.Change.TargetFingerprint,
                plan.Change.TargetPayload,
                "capability.applied-verified",
                "已应用并通过回读验证。",
                true,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)));
        }

        public Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken)
        {
            string original = _receiptOriginalValue.GetValueOrDefault(request.ReceiptId) ?? "0";
            _currentValue = original;

            return Task.FromResult(new BatchReceipt(new OperationReceipt(
                request.ReceiptId,
                Guid.NewGuid(),
                request.RecoveryId,
                "lifecycle.test.dword",
                TargetScope.Machine(Environment.MachineName),
                ChangeOutcome.RestoredVerified,
                original,
                _currentValue,
                _currentValue,
                "capability.restored-verified",
                "已恢复到原始值并通过回读验证。",
                true,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)));
        }
    }
}
