// File: GameOptimizer.Core/Diagnostics/EnhancedOperationResult.cs
using System;
using System.Collections.Generic;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Diagnostics;

/// <summary>
/// 增强的操作结果，包含详细诊断信息和自动修复选项
/// </summary>
public record EnhancedOperationResult : OperationResult
{
    /// <summary>
    /// 详细诊断信息（如果操作失败）
    /// </summary>
    public DiagnosticResult? Diagnostic { get; init; }
    
    /// <summary>
    /// 自动修复选项列表
    /// </summary>
    public List<AutoRepairOption>? RepairOptions { get; init; }
    
    /// <summary>
    /// 兼容性检查信息
    /// </summary>
    public CompatibilityInfo? Compatibility { get; init; }
    
    /// <summary>
    /// 性能影响评估
    /// </summary>
    public PerformanceImpact? Impact { get; init; }
    
    /// <summary>
    /// 是否可以自动修复
    /// </summary>
    public bool CanAutoRepair => RepairOptions?.Count > 0;
    
    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static new EnhancedOperationResult Success(string? message = null, TimeSpan duration = default)
    {
        return new EnhancedOperationResult
        {
            Code = OperationResultCode.Success,
            Message = message ?? LocalizationService.Instance.Get("Operation.Success"),
            Duration = duration
        };
    }
    
    /// <summary>
    /// 创建失败结果（带诊断信息）
    /// </summary>
    public static EnhancedOperationResult FailedWithDiagnostic(
        string message,
        DiagnosticResult diagnostic,
        List<AutoRepairOption>? repairOptions = null)
    {
        return new EnhancedOperationResult
        {
            Code = OperationResultCode.Failed,
            Message = message,
            Detail = diagnostic.UserMessage,
            Exception = diagnostic.OriginalException,
            Diagnostic = diagnostic,
            RepairOptions = repairOptions
        };
    }
    
    /// <summary>
    /// 创建不支持结果（带兼容性信息）
    /// </summary>
    public static EnhancedOperationResult NotSupportedWithReason(
        string message,
        CompatibilityInfo compatibility)
    {
        return new EnhancedOperationResult
        {
            Code = OperationResultCode.NotSupported,
            Message = message,
            Detail = compatibility.Reason,
            Compatibility = compatibility
        };
    }
}

/// <summary>
/// 自动修复选项
/// </summary>
public record AutoRepairOption
{
    /// <summary>
    /// 修复选项ID
    /// </summary>
    public string Id { get; init; } = string.Empty;
    
    /// <summary>
    /// 修复描述（用户友好）
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// 修复风险级别
    /// </summary>
    public RepairRiskLevel RiskLevel { get; init; }
    
    /// <summary>
    /// 是否需要重启
    /// </summary>
    public bool RequiresReboot { get; init; }
    
    /// <summary>
    /// 是否需要管理员权限
    /// </summary>
    public bool RequiresAdmin { get; init; }
    
    /// <summary>
    /// 预计修复时间（秒）
    /// </summary>
    public int EstimatedSeconds { get; init; }
    
    /// <summary>
    /// 修复成功率（0-100）
    /// </summary>
    public int SuccessRate { get; init; } = 80;
    
    /// <summary>
    /// 修复操作的技术细节
    /// </summary>
    public string? TechnicalDetails { get; init; }
}

/// <summary>
/// 修复风险级别
/// </summary>
public enum RepairRiskLevel
{
    /// <summary>
    /// 安全（无风险）
    /// </summary>
    Safe = 0,
    
    /// <summary>
    /// 低风险（可能需要重启）
    /// </summary>
    Low = 1,
    
    /// <summary>
    /// 中等风险（可能影响其他功能）
    /// </summary>
    Medium = 2,
    
    /// <summary>
    /// 高风险（可能导致系统不稳定）
    /// </summary>
    High = 3
}

/// <summary>
/// 兼容性信息
/// </summary>
public record CompatibilityInfo
{
    /// <summary>
    /// 是否兼容
    /// </summary>
    public bool IsCompatible { get; init; }
    
    /// <summary>
    /// 不兼容的原因
    /// </summary>
    public string Reason { get; init; } = string.Empty;
    
    /// <summary>
    /// 最低系统要求
    /// </summary>
    public SystemRequirement? MinimumRequirement { get; init; }
    
    /// <summary>
    /// 当前系统信息
    /// </summary>
    public SystemInfo? CurrentSystem { get; init; }
    
    /// <summary>
    /// 缺失的依赖项
    /// </summary>
    public List<string>? MissingDependencies { get; init; }
}

/// <summary>
/// 系统要求
/// </summary>
public record SystemRequirement
{
    /// <summary>
    /// 最低 Windows 版本（Build Number）
    /// </summary>
    public int? MinWindowsBuild { get; init; }
    
    /// <summary>
    /// 需要的服务列表
    /// </summary>
    public List<string>? RequiredServices { get; init; }
    
    /// <summary>
    /// 需要的硬件特性
    /// </summary>
    public List<string>? RequiredHardwareFeatures { get; init; }
    
    /// <summary>
    /// 需要的注册表键
    /// </summary>
    public List<string>? RequiredRegistryKeys { get; init; }
}

/// <summary>
/// 当前系统信息
/// </summary>
public record SystemInfo
{
    /// <summary>
    /// Windows 版本（Build Number）
    /// </summary>
    public int WindowsBuild { get; init; }
    
    /// <summary>
    /// Windows 版本名称
    /// </summary>
    public string WindowsVersion { get; init; } = string.Empty;
    
    /// <summary>
    /// 是否为管理员模式
    /// </summary>
    public bool IsAdminMode { get; init; }
    
    /// <summary>
    /// 可用的服务列表
    /// </summary>
    public List<string>? AvailableServices { get; init; }
    
    /// <summary>
    /// 可用的硬件特性
    /// </summary>
    public List<string>? AvailableHardwareFeatures { get; init; }
}

/// <summary>
/// 性能影响评估
/// </summary>
public record PerformanceImpact
{
    /// <summary>
    /// CPU 使用率变化（百分比，正数表示增加）
    /// </summary>
    public int CpuUsageDelta { get; init; }
    
    /// <summary>
    /// 内存使用变化（MB，正数表示增加）
    /// </summary>
    public int MemoryUsageDelta { get; init; }
    
    /// <summary>
    /// 磁盘空间变化（MB，正数表示增加）
    /// </summary>
    public int DiskSpaceDelta { get; init; }
    
    /// <summary>
    /// 预期性能提升（百分比）
    /// </summary>
    public int ExpectedPerformanceGain { get; init; }
    
    /// <summary>
    /// 影响描述
    /// </summary>
    public string Description { get; init; } = string.Empty;
}
