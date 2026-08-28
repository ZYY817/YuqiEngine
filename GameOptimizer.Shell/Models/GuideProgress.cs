// File: GameOptimizer.Shell/Models/GuideProgress.cs
namespace GameOptimizer.Shell.Models;

public class GuideProgress
{
    public Dictionary<string, bool> CompletedSequences { get; set; } = new();
    public Dictionary<string, DateTime> CompletionDates { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public int Version { get; set; } = 1;
}
