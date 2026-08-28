using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Optimization;

public interface IOperationRestoreStore
{
	Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken);

	Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken);

	Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken);

	Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken);
}
