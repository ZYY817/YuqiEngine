// File: GameOptimizer.Core/Services/PowerPlanService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Optimization.Operations;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 电源计划服务 - PowerCfgUtility 的公共包装层
/// 提供对 powercfg 命令的安全访问
/// </summary>
public class PowerPlanService
{
    /// <summary>
    /// 异步执行 powercfg 命令
    /// </summary>
    public async Task<(int ExitCode, string StdOut, string StdErr)> ExecuteCommandAsync(
        string arguments,
        CancellationToken cancellationToken = default)
    {
        return await PowerCfgUtility.RunAsync(arguments, cancellationToken);
    }

    /// <summary>
    /// 获取当前激活的电源计划 GUID
    /// </summary>
    public async Task<Guid?> GetActiveSchemeAsync(CancellationToken cancellationToken = default)
    {
        return await PowerCfgUtility.GetActiveSchemeAsync(cancellationToken);
    }

    /// <summary>
    /// 获取当前激活的电源计划 GUID (带回退机制)
    /// </summary>
    public async Task<Guid?> GetActiveSchemeWithFallbackAsync(CancellationToken cancellationToken = default)
    {
        return await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(cancellationToken);
    }

    /// <summary>
    /// 获取当前激活的电源计划 GUID (带诊断信息)
    /// </summary>
    public async Task<(Guid? Scheme, string DiagnosticInfo)> GetActiveSchemeWithDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        return await PowerCfgUtility.GetActiveSchemeWithDiagnosticsAsync(cancellationToken);
    }
}
