using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Contracts.Changes;

public interface IChangeOrchestrator
{
    Task<StateObservation?> ObserveAsync(string capabilityId, TargetScope scope, CancellationToken cancellationToken);
    Task<PreparationResult> PrepareAsync(ChangeRequest request, CancellationToken cancellationToken);
    Task<BatchReceipt> ExecuteAsync(
        PreparedChangePlan plan,
        ConfirmationToken confirmation,
        CancellationToken cancellationToken);
    Task<BatchReceipt> RestoreAsync(RestoreRequest request, CancellationToken cancellationToken);
}
