// File: GameOptimizer.Shell/Services/PowerPlan/PowerPlanTemplateSelector.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameOptimizer.Shell.Services.PowerPlan;

/// <summary>
/// 电源计划模板选择器 - 根据参数选择合适的基础模板
/// </summary>
public class PowerPlanTemplateSelector
{
    // Windows 默认计划 GUID
    private static readonly Guid WindowsBalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid WindowsHighPerformanceGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    /// <summary>
    /// 根据参数选择基础模板
    /// </summary>
    public Guid SelectBaseTemplate(Dictionary<string, int> settings)
    {
        var procMin = settings.GetValueOrDefault("proc_min_state", 5);
        var procMax = settings.GetValueOrDefault("proc_max_state", 100);

        // 如果是高性能场景 (处理器最小状态 >= 80%)，基于高性能计划
        if (procMin >= 80 && procMax == 100)
        {
            return WindowsHighPerformanceGuid;
        }

        // 否则基于平衡计划
        return WindowsBalancedGuid;
    }
}
