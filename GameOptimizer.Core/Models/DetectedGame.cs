namespace GameOptimizer.Core.Models;

public sealed record DetectedGame(string Name, string Executable, string IconPath, string ProfileId, string OptimizationStatus);
