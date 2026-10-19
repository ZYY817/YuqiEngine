namespace GameOptimizer.Core.Models;

public sealed record OptimizationRecommendation(string Title, string Description, string Impact, bool RequiresRestart);
