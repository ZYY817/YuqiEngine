using System.Collections.Generic;
using System.Linq;

namespace GameOptimizer.Core.Optimization;

public sealed record OptimizationBatchResult(string ModuleKey, IReadOnlyList<OperationExecutionSummary> Operations)
{
	public bool Success => Operations.Count > 0 && Operations.All((OperationExecutionSummary o) => o.Success);

	public int ChangedCount => Operations.Count((OperationExecutionSummary o) => o.Changed);
}
