// File: GameOptimizer.Core/Services/PowerOperationDiagnostics.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 电源操作诊断服务 - 专门诊断电源相关操作失败的原因
/// </summary>
public class PowerOperationDiagnostics
{
    private readonly ILogger<PowerOperationDiagnostics> _logger;
    private readonly ErrorDiagnosticEngine _diagnosticEngine;
    private readonly AutoRepairService _repairService;
    
    public PowerOperationDiagnostics(
        ILogger<PowerOperationDiagnostics> logger,
        ErrorDiagnosticEngine diagnosticEngine,
        AutoRepairService repairService)
    {
        _logger = logger;
        _diagnosticEngine = diagnosticEngine;
        _repairService = repairService;
    }
    
    /// <summary>
    /// 诊断电源操作失败
    /// </summary>
    public async Task<EnhancedOperationResult> DiagnosePowerOperationFailureAsync(
        string operationId,
        string operationTitle,
        Exception? exception,
        string? stdErr,
        int exitCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始诊断电源操作失败: {OperationId} - {Title}", operationId, operationTitle);
            
            // 检查电源服务健康状态
            var health = SystemCompatibilityChecker.CheckPowerServiceHealth();
            
            if (!health.IsHealthy)
            {
                _logger.LogWarning("电源服务不健康: {Message}", health.GetDiagnosticMessage());
                return await CreatePowerServiceUnhealthyResultAsync(operationTitle, health, cancellationToken);
            }
            
            // 如果有异常，使用诊断引擎分析
            if (exception != null)
            {
                var context = new DiagnosticContext
                {
                    OperationType = $"电源操作: {operationTitle}",
                    TargetResource = "电源计划设置",
                    IsAdminMode = SystemCompatibilityChecker.CurrentSystem.IsAdminMode
                };
                
                var diagnostic = _diagnosticEngine.Diagnose(exception, context);
                var repairOptions = _repairService.GetRepairOptions(diagnostic);
                
                return EnhancedOperationResult.FailedWithDiagnostic(
                    $"{operationTitle} 失败",
                    diagnostic,
                    repairOptions);
            }
            
            // 如果有 StdErr 输出，分析错误信息
            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                return AnalyzeStdErrOutput(operationTitle, stdErr, exitCode);
            }
            
            // 通用失败
            return new EnhancedOperationResult
            {
                Code = OperationResultCode.Failed,
                Message = $"{operationTitle} 失败",
                Detail = $"powercfg.exe 返回错误代码: {exitCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "诊断电源操作失败时发生异常");
            return new EnhancedOperationResult
            {
                Code = OperationResultCode.Failed,
                Message = $"{operationTitle} 失败",
                Detail = "诊断过程中发生异常",
                Exception = ex
            };
        }
    }
    
    /// <summary>
    /// 创建电源服务不健康的结果
    /// </summary>
    private Task<EnhancedOperationResult> CreatePowerServiceUnhealthyResultAsync(
        string operationTitle,
        PowerServiceHealth health,
        CancellationToken cancellationToken)
    {
        var diagnostic = new DiagnosticResult
        {
            Code = new ErrorCode(ErrorCategory.Power, ErrorSeverity.High, 4001),
            Category = ErrorCategory.Power,
            Severity = ErrorSeverity.High,
            Title = "电源服务异常",
            UserMessage = health.GetDiagnosticMessage(),
            TechnicalDetails = BuildHealthTechnicalDetails(health),
            PossibleCauses = BuildHealthCauses(health),
            Solutions = new List<SolutionStep>(),
            Context = new DiagnosticContext
            {
                OperationType = $"电源操作: {operationTitle}",
                TargetResource = "电源服务",
                IsAdminMode = SystemCompatibilityChecker.CurrentSystem.IsAdminMode
            }
        };
        
        var repairOptions = _repairService.GetRepairOptions(diagnostic);
        
        return Task.FromResult(EnhancedOperationResult.FailedWithDiagnostic(
            $"{operationTitle} 失败: 电源服务异常",
            diagnostic,
            repairOptions));
    }
    
    /// <summary>
    /// 分析 powercfg.exe 的 StdErr 输出
    /// </summary>
    private EnhancedOperationResult AnalyzeStdErrOutput(string operationTitle, string stdErr, int exitCode)
    {
        var lowerErr = stdErr.ToLowerInvariant();
        
        // 访问被拒绝
        if (lowerErr.Contains("access") && lowerErr.Contains("denied"))
        {
            var diagnostic = new DiagnosticResult
            {
                Code = new ErrorCode(ErrorCategory.Permission, ErrorSeverity.High, 1001),
                Category = ErrorCategory.Permission,
                Severity = ErrorSeverity.High,
                Title = "权限不足",
                UserMessage = "需要管理员权限才能修改电源设置",
                TechnicalDetails = $"powercfg.exe 错误输出:\n{stdErr}\n退出代码: {exitCode}",
                PossibleCauses = new List<string>
                {
                    "程序未以管理员身份运行",
                    "UAC 权限不足",
                    "组策略限制"
                },
                Solutions = new List<SolutionStep>
                {
                    new()
                    {
                        Order = 1,
                        Description = "右键程序图标，选择\"以管理员身份运行\"",
                        RequiresAdmin = true
                    }
                },
                Context = new DiagnosticContext
                {
                    OperationType = $"电源操作: {operationTitle}",
                    TargetResource = "电源计划设置",
                    IsAdminMode = SystemCompatibilityChecker.CurrentSystem.IsAdminMode
                }
            };
            
            return EnhancedOperationResult.FailedWithDiagnostic(
                $"{operationTitle} 失败: 权限不足",
                diagnostic);
        }
        
        // 电源计划不存在
        if (lowerErr.Contains("scheme") || lowerErr.Contains("guid"))
        {
            var diagnostic = new DiagnosticResult
            {
                Code = new ErrorCode(ErrorCategory.Power, ErrorSeverity.High, 4002),
                Category = ErrorCategory.Power,
                Severity = ErrorSeverity.High,
                Title = "电源计划不存在",
                UserMessage = "指定的电源计划不存在或已损坏",
                TechnicalDetails = $"powercfg.exe 错误输出:\n{stdErr}\n退出代码: {exitCode}",
                PossibleCauses = new List<string>
                {
                    "电源计划已被删除",
                    "电源计划 GUID 无效",
                    "电源配置文件损坏"
                },
                Solutions = new List<SolutionStep>
                {
                    new()
                    {
                        Order = 1,
                        Description = "重置电源计划到默认设置",
                        Command = "powercfg /restoredefaultschemes",
                        RequiresAdmin = true
                    }
                },
                Context = new DiagnosticContext
                {
                    OperationType = $"电源操作: {operationTitle}",
                    TargetResource = "电源计划",
                    IsAdminMode = SystemCompatibilityChecker.CurrentSystem.IsAdminMode
                }
            };
            
            var repairOptions = new List<AutoRepairOption>
            {
                new()
                {
                    Id = "repair_power_scheme_reset",
                    Description = "重置电源计划到默认设置",
                    RiskLevel = RepairRiskLevel.Low,
                    RequiresReboot = false,
                    RequiresAdmin = true,
                    EstimatedSeconds = 10,
                    SuccessRate = 85
                }
            };
            
            return EnhancedOperationResult.FailedWithDiagnostic(
                $"{operationTitle} 失败: 电源计划不存在",
                diagnostic,
                repairOptions);
        }
        
        // 参数无效
        if (lowerErr.Contains("parameter") || lowerErr.Contains("invalid"))
        {
            var diagnostic = new DiagnosticResult
            {
                Code = new ErrorCode(ErrorCategory.Configuration, ErrorSeverity.Medium, 8002),
                Category = ErrorCategory.Configuration,
                Severity = ErrorSeverity.Medium,
                Title = "参数无效",
                UserMessage = "电源设置参数无效或不支持",
                TechnicalDetails = $"powercfg.exe 错误输出:\n{stdErr}\n退出代码: {exitCode}",
                PossibleCauses = new List<string>
                {
                    "设置值超出允许范围",
                    "当前 Windows 版本不支持此设置",
                    "硬件不支持此功能"
                },
                Solutions = new List<SolutionStep>
                {
                    new()
                    {
                        Order = 1,
                        Description = "检查 Windows 版本是否支持此功能",
                        RequiresAdmin = false
                    },
                    new()
                    {
                        Order = 2,
                        Description = "跳过此项优化，继续其他优化",
                        RequiresAdmin = false
                    }
                },
                Context = new DiagnosticContext
                {
                    OperationType = $"电源操作: {operationTitle}",
                    TargetResource = "电源设置参数",
                    IsAdminMode = SystemCompatibilityChecker.CurrentSystem.IsAdminMode
                }
            };
            
            return EnhancedOperationResult.FailedWithDiagnostic(
                $"{operationTitle} 失败: 参数无效",
                diagnostic);
        }
        
        // 通用错误
        var genericDiagnostic = new DiagnosticResult
        {
            Code = new ErrorCode(ErrorCategory.Power, ErrorSeverity.Medium, 4000),
            Category = ErrorCategory.Power,
            Severity = ErrorSeverity.Medium,
            Title = "电源操作失败",
            UserMessage = $"powercfg.exe 执行失败: {stdErr.Trim()}",
            TechnicalDetails = $"powercfg.exe 错误输出:\n{stdErr}\n退出代码: {exitCode}",
            PossibleCauses = new List<string>
            {
                "电源服务异常",
                "系统配置错误",
                "权限不足"
            },
            Solutions = new List<SolutionStep>
            {
                new()
                {
                    Order = 1,
                    Description = "以管理员身份运行程序",
                    RequiresAdmin = true
                },
                new()
                {
                    Order = 2,
                    Description = "重启 Power 服务",
                    Command = "net stop Power && net start Power",
                    RequiresAdmin = true
                }
            },
            Context = new DiagnosticContext
            {
                OperationType = $"电源操作: {operationTitle}",
                TargetResource = "电源设置",
                IsAdminMode = SystemCompatibilityChecker.CurrentSystem.IsAdminMode
            }
        };
        
        return EnhancedOperationResult.FailedWithDiagnostic(
            $"{operationTitle} 失败",
            genericDiagnostic);
    }
    
    // ==================== 辅助方法 ====================
    
    private string BuildHealthTechnicalDetails(PowerServiceHealth health)
    {
        return $@"电源服务健康检查:
- Power 服务存在: {health.PowerServiceExists}
- Power 服务运行: {health.PowerServiceRunning}
- Power 服务启动类型: {health.PowerServiceStartMode}
- 电源计划可访问: {health.PowerSchemesAccessible}
- powercfg.exe 可用: {health.PowerCfgAvailable}
- 错误信息: {health.ErrorMessage ?? "None"}";
    }
    
    private List<string> BuildHealthCauses(PowerServiceHealth health)
    {
        var causes = new List<string>();
        
        if (!health.PowerServiceExists)
            causes.Add("Power 服务不存在（系统异常）");
        else if (!health.PowerServiceRunning)
            causes.Add("Power 服务未运行");
        else if (health.PowerServiceStartMode == System.ServiceProcess.ServiceStartMode.Disabled)
            causes.Add("Power 服务被禁用");
        
        if (!health.PowerSchemesAccessible)
            causes.Add("无法访问电源计划（可能损坏）");
        
        if (!health.PowerCfgAvailable)
            causes.Add("powercfg.exe 不可用");
        
        if (causes.Count == 0)
            causes.Add("未知原因");
        
        return causes;
    }
}
