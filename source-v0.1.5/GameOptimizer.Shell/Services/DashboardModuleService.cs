using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public interface IDashboardModuleService
{
    Task SyncAllModuleStatusesAsync(IEnumerable<OptimizationModuleDefinition> modules);
    Task SyncModuleStatusAsync(OptimizationModuleDefinition module);
    int CalculateOptimizationScore(IEnumerable<OptimizationModuleDefinition> modules);
}

public sealed class DashboardModuleService : IDashboardModuleService
{
    private readonly IOptimizationEngine _optimizationEngine;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public DashboardModuleService(IOptimizationEngine optimizationEngine)
    {
        _optimizationEngine = optimizationEngine;
    }

    public async Task SyncAllModuleStatusesAsync(IEnumerable<OptimizationModuleDefinition> modules)
    {
        if (!await _syncLock.WaitAsync(0)) return;

        try
        {
            var tasks = modules.Where(m => m.SupportsExecution).Select(SyncModuleStatusCoreAsync);
            await Task.WhenAll(tasks);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task SyncModuleStatusAsync(OptimizationModuleDefinition module)
    {
        if (!await _syncLock.WaitAsync(100)) return;

        try
        {
            await SyncModuleStatusCoreAsync(module);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task SyncModuleStatusCoreAsync(OptimizationModuleDefinition module)
    {
        if (!module.SupportsExecution) return;

        try
        {
            var statuses = await _optimizationEngine.GetStatusesAsync(module.Key);
            var statusList = statuses.ToList();

            for (var i = 0; i < module.Toggles.Count; i++)
            {
                if (i < statusList.Count)
                {
                    // Why: 统一走状态机同步入口，避免直接赋值绕过Pending/冷却保护
                    module.Toggles[i].SyncStatus(statusList[i].IsCompliant);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DashboardModuleService] SyncModuleStatus failed for {module.Key}: {ex.Message}");
        }
    }

    public int CalculateOptimizationScore(IEnumerable<OptimizationModuleDefinition> modules)
    {
        var moduleList = modules.Where(m => m.SupportsExecution).ToList();
        if (moduleList.Count == 0) return 100;

        var totalWeight = 0;
        var appliedWeight = 0;

        foreach (var module in moduleList)
        {
            var weight = GetModuleWeight(module.Key);
            totalWeight += weight;

            var totalCount = Math.Max(1, module.Toggles.Count);
            var appliedCount = module.AppliedCount;
            if (appliedCount >= totalCount)
            {
                appliedWeight += weight;
            }
            else if (appliedCount > 0)
            {
                appliedWeight += weight * appliedCount / totalCount;
            }
        }

        return totalWeight > 0 ? (int)(appliedWeight * 100.0 / totalWeight) : 100;
    }

    private static int GetModuleWeight(string moduleKey) => moduleKey switch
    {
        "performance" => 15,
        "cpu" => 12,
        "graphics" => 12,
        "network" => 10,
        "system" => 10,
        "memory" => 8,
        "power_advanced" => 8,
        _ => 5
    };
}
