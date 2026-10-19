using System.Collections.Generic;

namespace GameOptimizer.Shell.Models;

public sealed class EsportsModeConfig
{
    public int PresetPolicyVersion { get; set; }
    public string? SelectedStrategy { get; set; }
    public Dictionary<string, bool> EnabledItems { get; set; } = new();
    public List<string> AppliedItems { get; set; } = new();
}
