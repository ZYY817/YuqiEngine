// File: GameOptimizer.Shell/Services/PowerScenarioService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 电源场景服务实现 - 协调各个模块完成场景应用
/// </summary>
public class PowerScenarioService : IPowerScenarioService
{
    private readonly PowerPlanEditorService _editorService = PowerPlanEditorService.CreateFailClosedCompatibility();
    private readonly PowerSettingComparer _comparer = new();
    private readonly LocalizationService _localization = LocalizationService.Instance;
    
    /// <summary>
    /// 应用场景预设
    /// </summary>
    public Task<OperationResult> ApplyScenarioAsync(PowerScenarioApplication application) =>
        Task.FromResult(OperationResult.Failed(
            "Power scenario creation/import/deletion is blocked until it is migrated to the typed Power owner with durable recovery."));

    /// <summary>
    /// 获取场景对比数据
    /// </summary>
    public async Task<ScenarioComparisonResult> GetScenarioComparisonAsync(
        string scenarioId,
        HardwareInfo hardware)
    {
        try
        {
            // 1. 获取场景预设参数
            var preset = PowerPresetService.GetPresetForCpu(
                scenarioId,
                hardware.CpuVendor,
                hardware.IsIntelHybrid);

            // 2. 生成对比列表
            var comparisons = await _comparer.CompareSettingsAsync(preset.Settings);

            // 3. 获取当前电源计划名称
            var currentPlan = _editorService.GetActivePowerPlan();
            var currentPlanName = currentPlan?.Name ?? _localization.Get("Common.Unknown");

            // 4. 生成新电源计划名称
            var newPlanName = $"宇奇引擎 - {preset.Name}";

            // 5. 检查是否存在同名计划
            var existingPlan = await CheckExistingPlanAsync(newPlanName);

            return new ScenarioComparisonResult
            {
                ScenarioName = preset.Name,
                ScenarioDescription = preset.Description,
                CurrentPowerPlanName = currentPlanName,
                NewPowerPlanName = newPlanName,
                Comparisons = comparisons,
                HasConflict = existingPlan != null,
                ConflictMessage = existingPlan != null
                    ? _localization.Get("PowerScenario.Warning.PlanExists", newPlanName)
                    : null
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PowerScenarioService] GetScenarioComparison error: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 恢复到 Windows 默认平衡计划
    /// </summary>
    public Task<OperationResult> RestoreToDefaultAsync() =>
        Task.FromResult(OperationResult.Failed(
            "Restoring a guessed Windows default is blocked; only a verified typed Power receipt may be restored."));

    /// <summary>
    /// 检查是否存在同名电源计划
    /// </summary>
    public async Task<PowerPlanInfo?> CheckExistingPlanAsync(string planName)
    {
        return await Task.Run(() =>
        {
            var allPlans = _editorService.GetAllPowerPlans();
            return allPlans.FirstOrDefault(p =>
                p.Name.Equals(planName, StringComparison.OrdinalIgnoreCase));
        });
    }

}
