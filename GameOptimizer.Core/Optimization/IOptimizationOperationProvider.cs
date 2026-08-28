using System.Collections.Generic;

namespace GameOptimizer.Core.Optimization;

public interface IOptimizationOperationProvider
{
	IEnumerable<OptimizationModuleDescriptor> GetModules();
}
