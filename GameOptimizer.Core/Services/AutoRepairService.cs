// File: GameOptimizer.Core/Services/AutoRepairService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.FileShellSafety;
using Microsoft.Extensions.Logging;
using GameOptimizer.Core.Services.NetworkReset;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 自动修复服务 - 尝试自动修复常见问题
/// </summary>
public class AutoRepairService
{
    private readonly ILogger<AutoRepairService> _logger;
    private readonly INetworkResetOwner? _networkResetOwner;
    private readonly Dictionary<string, Func<Task<RepairResult>>> _repairStrategies;
    
    public AutoRepairService(ILogger<AutoRepairService> logger, INetworkResetOwner? networkResetOwner = null)
    {
        _logger = logger;
        _networkResetOwner = networkResetOwner;
        _repairStrategies = new Dictionary<string, Func<Task<RepairResult>>>();
        RegisterRepairStrategies();
    }
    
    /// <summary>
    /// 尝试自动修复问题
    /// </summary>
    public async Task<RepairResult> TryRepairAsync(AutoRepairOption option)
    {
        try
        {
            _logger.LogInformation("开始自动修复: {RepairId} - {Description}", option.Id, option.Description);
            
            if (!_repairStrategies.TryGetValue(option.Id, out var strategy))
            {
                return RepairResult.Failed($"未找到修复策略: {option.Id}");
            }
            
            var result = await strategy();
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("自动修复成功: {RepairId}", option.Id);
            }
            else
            {
                _logger.LogWarning("自动修复失败: {RepairId} - {Message}", option.Id, result.Message);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动修复异常: {RepairId}", option.Id);
            return RepairResult.Failed($"修复过程中发生异常: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 获取可用的修复选项
    /// </summary>
    public List<AutoRepairOption> GetRepairOptions(DiagnosticResult diagnostic)
    {
        var options = new List<AutoRepairOption>();
        
        // 根据错误类别生成修复选项
        switch (diagnostic.Category)
        {
            case ErrorCategory.Power:
                options.AddRange(GetPowerRepairOptions(diagnostic));
                break;
            
            case ErrorCategory.Service:
                options.AddRange(GetServiceRepairOptions(diagnostic));
                break;
            
            case ErrorCategory.FileSystem:
                options.AddRange(GetFileSystemRepairOptions(diagnostic));
                break;
            
            case ErrorCategory.Network:
                options.AddRange(GetNetworkRepairOptions(diagnostic));
                break;
            
            case ErrorCategory.Configuration:
                options.AddRange(GetConfigurationRepairOptions(diagnostic));
                break;
        }
        
        // 按成功率和风险级别排序
        return options.OrderByDescending(o => o.SuccessRate)
                     .ThenBy(o => o.RiskLevel)
                     .ToList();
    }
    
    // ==================== 修复策略注册 ====================
    
    private void RegisterRepairStrategies()
    {
        // 电源服务修复
        _repairStrategies["repair_power_service_start"] = RepairPowerServiceStartAsync;
        _repairStrategies["repair_power_service_enable"] = RepairPowerServiceEnableAsync;
        _repairStrategies["repair_power_scheme_reset"] = RepairPowerSchemeResetAsync;
        
        // 服务修复
        _repairStrategies["repair_service_start"] = RepairGenericServiceStartAsync;
        _repairStrategies["repair_service_enable"] = RepairGenericServiceEnableAsync;
        
        // 配置修复
        _repairStrategies["repair_registry_permissions"] = RepairRegistryPermissionsAsync;
        _repairStrategies["repair_file_permissions"] = RepairFilePermissionsAsync;
        
        // 网络修复
        _repairStrategies["repair_network_reset"] = RepairNetworkResetAsync;
        _repairStrategies["repair_winsock_reset"] = RepairWinsockResetAsync;
        
        // 备份修复
        _repairStrategies["repair_backup_create_directory"] = RepairBackupCreateDirectoryAsync;
        _repairStrategies["repair_backup_request_permission"] = RepairBackupRequestPermissionAsync;
    }
    
    // ==================== 电源相关修复 ====================
    
    private Task<RepairResult> RepairPowerServiceStartAsync() =>
        PowerRepairRequiresCentralOwner("启动 Power 服务");
    
    private Task<RepairResult> RepairPowerServiceEnableAsync() =>
        PowerRepairRequiresCentralOwner("修改 Power 服务启动类型");
    
    private Task<RepairResult> RepairPowerSchemeResetAsync() =>
        PowerRepairRequiresCentralOwner("重置默认电源方案");

    private Task<RepairResult> PowerRepairRequiresCentralOwner(string action)
    {
        _logger.LogWarning("已阻断旧 AutoRepair 电源写入入口：{Action}", action);
        return Task.FromResult(RepairResult.Failed(
            $"{action} 需要可恢复快照和统一协调器支持，当前已安全阻断。"));
    }
    
    // ==================== 通用服务修复 ====================
    
    private Task<RepairResult> RepairGenericServiceStartAsync() =>
        Task.FromResult(RepairResult.Failed(
            "通用服务名不允许由调用方动态指定；请使用固定白名单的服务 Owner。"));
    
    private Task<RepairResult> RepairGenericServiceEnableAsync() =>
        Task.FromResult(RepairResult.Failed(
            "通用服务启动类型写入已禁用；请使用固定白名单的服务 Owner。"));
    
    // ==================== 注册表和文件权限修复 ====================
    
    private async Task<RepairResult> RepairRegistryPermissionsAsync()
    {
        return await Task.FromResult(RepairResult.Failed("注册表权限修复需要手动操作"));
    }
    
    private async Task<RepairResult> RepairFilePermissionsAsync()
    {
        return await Task.FromResult(RepairResult.Failed("文件权限修复需要手动操作"));
    }
    
    // ==================== 网络修复 ====================
    
    private async Task<RepairResult> RepairNetworkResetAsync()
    {
        return await RunOwnedNetworkResetAsync(NetworkResetProfile.Full).ConfigureAwait(false);
    }
    
    private async Task<RepairResult> RepairWinsockResetAsync()
    {
        return await RunOwnedNetworkResetAsync(NetworkResetProfile.Winsock).ConfigureAwait(false);
    }

    private async Task<RepairResult> RunOwnedNetworkResetAsync(NetworkResetProfile profile)
    {
        if (_networkResetOwner is null)
            return RepairResult.Failed("Network Reset Owner 未接入，已安全阻断；不会回退旧命令直写。");
        var receipt = await _networkResetOwner.RunInteractiveAsync(profile).ConfigureAwait(false);
        return receipt.Outcome is NetworkResetOutcome.SucceededVerified
            or NetworkResetOutcome.AppliedRequiresRestartManualRecovery
            ? RepairResult.Success(receipt.Message, receipt.RebootRequired)
            : RepairResult.Failed(receipt.Message);
    }
    
    // ==================== 修复选项生成 ====================
    
    private List<AutoRepairOption> GetPowerRepairOptions(DiagnosticResult diagnostic)
    {
        var options = new List<AutoRepairOption>();
        var health = SystemCompatibilityChecker.CheckPowerServiceHealth();
        
        if (!health.PowerServiceRunning && health.PowerServiceExists)
        {
            options.Add(new AutoRepairOption
            {
                Id = "repair_power_service_start",
                Description = "启动 Power 服务",
                RiskLevel = RepairRiskLevel.High,
                RequiresReboot = false,
                RequiresAdmin = true,
                EstimatedSeconds = 5,
                SuccessRate = 0,
                TechnicalDetails = "需要保存服务原状态并接入统一协调器；当前安全阻断"
            });
        }
        
        if (health.PowerServiceStartMode == ServiceStartMode.Disabled)
        {
            options.Add(new AutoRepairOption
            {
                Id = "repair_power_service_enable",
                Description = "启用 Power 服务（设置为自动启动）",
                RiskLevel = RepairRiskLevel.High,
                RequiresReboot = false,
                RequiresAdmin = true,
                EstimatedSeconds = 5,
                SuccessRate = 0,
                TechnicalDetails = "需要保存原启动类型并接入统一协调器；当前安全阻断"
            });
        }
        
        if (!health.PowerSchemesAccessible)
        {
            options.Add(new AutoRepairOption
            {
                Id = "repair_power_scheme_reset",
                Description = "重置电源计划到默认设置",
                RiskLevel = RepairRiskLevel.High,
                RequiresReboot = false,
                RequiresAdmin = true,
                EstimatedSeconds = 10,
                SuccessRate = 0,
                TechnicalDetails = "需要统一协调器保存完整电源方案快照后才能执行；当前安全阻断"
            });
        }
        
        return options;
    }
    
    private List<AutoRepairOption> GetServiceRepairOptions(DiagnosticResult diagnostic)
    {
        var options = new List<AutoRepairOption>();
        
        // 根据具体服务错误生成修复选项
        // 这里需要从 diagnostic.Context 中获取服务名称
        
        return options;
    }
    
    private List<AutoRepairOption> GetFileSystemRepairOptions(DiagnosticResult diagnostic)
    {
        return new List<AutoRepairOption>();
    }
    
    private List<AutoRepairOption> GetNetworkRepairOptions(DiagnosticResult diagnostic)
    {
        var options = new List<AutoRepairOption>
        {
            new()
            {
                Id = "repair_winsock_reset",
                Description = "重置 Winsock 目录",
                RiskLevel = RepairRiskLevel.Low,
                RequiresReboot = true,
                RequiresAdmin = true,
                EstimatedSeconds = 10,
                SuccessRate = 80,
                TechnicalDetails = "必须通过 Network Reset Owner 的 Winsock profile 执行"
            },
            new()
            {
                Id = "repair_network_reset",
                Description = "完全重置网络设置",
                RiskLevel = RepairRiskLevel.Medium,
                RequiresReboot = true,
                RequiresAdmin = true,
                EstimatedSeconds = 30,
                SuccessRate = 75,
                TechnicalDetails = "重置 Winsock、TCP/IP 和 DNS 缓存"
            }
        };
        
        return options;
    }
    
    private List<AutoRepairOption> GetConfigurationRepairOptions(DiagnosticResult diagnostic)
    {
        return new List<AutoRepairOption>();
    }
    
    // ==================== 备份修复 ====================
    
    private async Task<RepairResult> RepairBackupCreateDirectoryAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("正在创建备份目录...");
                Constants.AppPaths.EnsureDirectoriesExist();
                
                var (success, errors) = Constants.AppPaths.ValidateDirectories();
                if (success)
                {
                    _logger.LogInformation("备份目录创建成功");
                    return RepairResult.Success("备份目录已创建并验证");
                }
                
                return RepairResult.Failed($"目录验证失败: {string.Join(", ", errors)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建备份目录失败");
                return RepairResult.Failed($"创建失败: {ex.Message}");
            }
        });
    }
    
    private async Task<RepairResult> RepairBackupRequestPermissionAsync()
    {
        var decision = await UnavailableFileShellSafetyOwner.Instance.EvaluateAsync(
            new FileShellSafetyRequest("repair_backup_request_permission", FileShellMutationKind.AccessControlMutation),
            CancellationToken.None).ConfigureAwait(false);
        _logger.LogWarning("Backup ACL repair blocked by {OwnerId}: {Status}", decision.OwnerId, decision.Status);
        return RepairResult.Failed($"{decision.Status}: {decision.Message}");
    }
}

/// <summary>
/// 修复结果
/// </summary>
public record RepairResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool RequiresReboot { get; init; }
    public string? TechnicalDetails { get; init; }
    
    public static RepairResult Success(string message, bool requiresReboot = false)
    {
        return new RepairResult
        {
            IsSuccess = true,
            Message = message,
            RequiresReboot = requiresReboot
        };
    }
    
    public static RepairResult Failed(string message)
    {
        return new RepairResult
        {
            IsSuccess = false,
            Message = message
        };
    }
}
