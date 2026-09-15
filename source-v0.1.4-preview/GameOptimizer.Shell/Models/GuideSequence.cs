// File: GameOptimizer.Shell/Models/GuideSequence.cs
namespace GameOptimizer.Shell.Models;

public class GuideSequence
{
    public string Id { get; set; } = string.Empty;
    public string PageKey { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public List<GuideStep> Steps { get; set; } = new();
    public bool IsCompleted { get; set; }
    public string IconGlyph { get; set; } = "\uE946";
}
