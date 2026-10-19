using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Contracts.Changes;

public interface IChangeOrchestrator
{
    Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken);
    Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken, bool registerPlan = true);
    Task<BatchReceipt> ExecuteAsync(
        PreparedChangePlan plan,
        ConfirmationToken confirmation,
        CancellationToken cancellationToken);
    Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Why: op 层的恢复指针（ReceiptId/RecoveryId）在变更完成后才落盘，
    /// 崩溃窗口内会丢指针但底层恢复材料已由协调器在写入前持久化。
    /// 该兜底按能力标识反查最新可恢复条目；默认实现返回 null 保持兼容。
    /// </summary>
    Task<BatchReceipt?> RestoreLatestByCapabilityAsync(string capabilityId, CancellationToken cancellationToken) =>
        Task.FromResult<BatchReceipt?>(null);
}
