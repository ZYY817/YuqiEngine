using System.Collections.Generic;

namespace GameOptimizer.Core.Optimization;

public sealed record OptimizationModuleDescriptor(string Key, string Title, string Description, IReadOnlyList<IOptimizationOperation> Operations);
