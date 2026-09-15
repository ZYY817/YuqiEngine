using System.Collections.Generic;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

public interface ISystemInsightService
{
	SystemResourceSnapshot GetCurrentSnapshot();

	DetectedGame? GetActiveGame();

	IReadOnlyList<OptimizationRecommendation> GetRecommendations();
}
