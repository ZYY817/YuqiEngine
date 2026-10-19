using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairRestoreServiceTests
{
    [Fact]
    public async Task Restore_SuccessWithoutVerifiedReceiptBecomesRecoveryRequired()
    {
        var executor = new ScriptedRepairExecutor
        {
            RestoreResult = RepairResult.Succeeded("假的恢复成功")
        };
        var service = new RepairService(new RepairItemRegistry(), new AllowGate(), executor);
        var request = Request();

        var result = await service.RestoreAsync(request);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.RecoveryRequired, result.Code);
        Assert.Equal("restore.success-receipt-missing", result.ReasonCode);
        Assert.Equal(request.Receipt.ReceiptId, result.ChangeReceipt?.ReceiptId);
    }

    [Fact]
    public async Task Restore_SuccessWithForgedGroupReceiptBecomesRecoveryRequired()
    {
        var request = Request();
        var forged = request.Receipt with { ReceiptId = Guid.NewGuid() };
        var executor = new ScriptedRepairExecutor
        {
            RestoreResult = RepairResult.Succeeded("伪造的恢复成功") with { ChangeReceipt = forged }
        };
        var service = new RepairService(new RepairItemRegistry(), new AllowGate(), executor);

        var result = await service.RestoreAsync(request);

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.RecoveryRequired, result.Code);
        Assert.Equal("restore.success-receipt-missing", result.ReasonCode);
        Assert.Equal(forged.ReceiptId, result.ChangeReceipt?.ReceiptId);
    }

    [Fact]
    public async Task Restore_OnlySameVerifiedPointerSetCanBeSuccessful()
    {
        var request = Request();
        var executor = new ScriptedRepairExecutor
        {
            RestoreResult = RepairResult.Succeeded("已恢复") with
            {
                ChangeReceipt = request.Receipt with
                {
                    Operations = [request.Receipt.Operations[0] with { Outcome = ChangeOutcome.RestoredVerified }]
                }
            }
        };
        var service = new RepairService(new RepairItemRegistry(), new AllowGate(), executor);

        var result = await service.RestoreAsync(request);

        Assert.True(result.Success);
        Assert.Equal(RepairResultCode.Succeeded, result.Code);
    }

    [Fact]
    public async Task Restore_NoChangeAndUnknownFailureRemainRecoveryRequired()
    {
        var request = Request();
        var executor = new ScriptedRepairExecutor
        {
            RestoreResult = RepairResult.NoChange("恢复没有返回验证结果")
        };
        var service = new RepairService(new RepairItemRegistry(), new AllowGate(), executor);

        var noChange = await service.RestoreAsync(request);
        executor.RestoreResult = RepairResult.Failed("恢复回读失败", reasonCode: "restore.readback-failed");
        var failed = await service.RestoreAsync(request);

        Assert.Equal(RepairResultCode.RecoveryRequired, noChange.Code);
        Assert.Equal(RepairResultCode.RecoveryRequired, failed.Code);
        Assert.False(noChange.Success);
        Assert.False(failed.Success);
    }

    [Fact]
    public async Task Restore_GatePermissionFailureDoesNotCallExecutor()
    {
        var executor = new ScriptedRepairExecutor();
        var service = new RepairService(
            new RepairItemRegistry(),
            new FixedGate(new RepairExecutionGateDecision(
                RepairExecutionGateDecisionKind.ReadonlyOnly,
                "权限已撤销",
                "permission.denied")),
            executor);

        var result = await service.RestoreAsync(Request());

        Assert.False(result.Success);
        Assert.Equal(RepairResultCode.PermissionDenied, result.Code);
        Assert.Equal(0, executor.RestoreCalls);
    }

    private static RepairRecoveryRequest Request()
    {
        var operation = new RepairChangeOperationReceipt(
            RepairChangeCapabilityIds.FeedbackReset,
            Guid.NewGuid(),
            Guid.NewGuid(),
            ChangeOutcome.SucceededVerified,
            true);
        return new RepairRecoveryRequest(
            "repair.feedback",
            new RepairChangeReceipt(
                Guid.NewGuid(),
                "repair.feedback",
                [operation],
                false,
                DateTimeOffset.UtcNow));
    }

    private sealed class AllowGate : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RepairExecutionGateDecision.Allowed());
    }

    private sealed class FixedGate(RepairExecutionGateDecision decision) : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(decision);
    }

    private sealed class ScriptedRepairExecutor : IRepairChangeExecutor
    {
        public RepairResult RestoreResult { get; set; } = RepairResult.Failed("未配置 fake 恢复结果");
        public int RestoreCalls { get; private set; }

        public bool IsManaged(string repairItemId) => repairItemId == "repair.feedback";

        public Task<RepairResult> ExecuteAsync(
            RepairItem item,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(RepairResult.Failed("未配置 fake 执行结果"));

        public Task<RepairResult> RestoreAsync(
            RepairRecoveryRequest request,
            IProgress<RepairProgress>? progress,
            CancellationToken cancellationToken)
        {
            RestoreCalls++;
            return Task.FromResult(RestoreResult);
        }
    }
}
