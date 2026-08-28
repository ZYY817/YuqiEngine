using System;
using System.Collections.Generic;

namespace GameOptimizer.Shell.Models;

public record PowerConfig
{
    public string Version { get; init; } = "1.0";
    public DateTime ExportTime { get; init; } = DateTime.Now;
    public string HardwareInfo { get; init; } = "";
    public Dictionary<string, int> Settings { get; init; } = new();
}
