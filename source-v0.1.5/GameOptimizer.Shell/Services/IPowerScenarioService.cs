// File: GameOptimizer.Shell/Services/IPowerScenarioService.cs
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 电源场景服务接口
/// </summary>
public interface IPowerScenarioService
{
    /// <summary>
    /// 应用场景预设 (创建电源计划 + 应用参数 + 激活)
    /// </summary>
    Task<OperationResult> ApplyScenarioAsync(PowerScenarioApplication application);

    /// <summary>
    /// 获取场景对比数据
    /// </summary>
    Task<ScenarioComparisonResult> GetScenarioComparisonAsync(string scenarioId, HardwareInfo hardware);

    /// <summary>
    /// 恢复到 Windows 默认平衡电源计划
    /// </summary>
    Task<OperationResult> RestoreToDefaultAsync();

    /// <summary>
    /// 检查是否存在同名电源计划
    /// </summary>
    Task<PowerPlanInfo?> CheckExistingPlanAsync(string planName);
}
