using System.Collections.Generic;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Models;

public record PowerPreset
{
    public string Id { get; init; } = "";
    public string NameKey { get; init; } = "";
    public string Icon { get; init; } = "";
    public string DescriptionKey { get; init; } = "";
    public Dictionary<string, int> Settings { get; init; } = new();
    
    public string Name => LocalizationService.Instance.Get(NameKey);
    public string Description => LocalizationService.Instance.Get(DescriptionKey);
}
