// File: GameOptimizer.Shell/Models/GuideStep.cs
namespace GameOptimizer.Shell.Models;

public enum GuideTooltipPosition
{
    Auto,
    Top,
    Bottom,
    Left,
    Right
}

public class GuideStep
{
    public string Id { get; set; } = string.Empty;
    public string TargetElementName { get; set; } = string.Empty;
    public string TitleKey { get; set; } = string.Empty;
    public string ContentKey { get; set; } = string.Empty;
    public GuideTooltipPosition Position { get; set; } = GuideTooltipPosition.Auto;
    public bool RequireInteraction { get; set; } = false;
    public int HighlightPadding { get; set; } = 12;
    public bool ShowArrow { get; set; } = true;
    public bool AutoScroll { get; set; } = true;
}
