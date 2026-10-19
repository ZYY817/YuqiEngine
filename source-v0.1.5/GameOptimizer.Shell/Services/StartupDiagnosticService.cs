// File: GameOptimizer.Shell/Services/StartupDiagnosticService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 启动诊断服务 - 应用启动时自动检查系统健康状态
/// </summary>
[SupportedOSPlatform("windows")]
public class StartupDiagnosticService
{
    private readonly UnifiedPowerService _powerService = new();
    private readonly ISystemRestoreService _restoreService;

    public StartupDiagnosticService(ISystemRestoreService restoreService)
    {
        _restoreService = restoreService;
    }

    /// <summary>
    /// 执行启动诊断
    /// </summary>
    public async Task<StartupDiagnosticReport> RunDiagnosticsAsync()
    {
        var report = new StartupDiagnosticReport();
        var loc = LocalizationService.Instance;

        // 1. 检查管理员权限
        report.HasAdminPrivileges = PermissionChecker.IsRunningAsAdministrator();
        if (!report.HasAdminPrivileges)
        {
            report.Issues.Add(new DiagnosticIssue
            {
                Severity = IssueSeverity.Warning,
                Title = loc.Get("Diagnostic.NoAdminTitle"),
                Description = loc.Get("Diagnostic.NoAdminDesc"),
                SuggestedAction = loc.Get("Diagnostic.NoAdminAction"),
                CanAutoFix = false
            });
        }

        // 2. 检查电源服务
        var (status, error) = SafeServiceHelper.TryGetServiceStatus("Power");
        var powerServiceRunning = status.HasValue && status.Value == System.ServiceProcess.ServiceControllerStatus.Running;
        report.IsPowerServiceRunning = powerServiceRunning;
        
        if (!powerServiceRunning)
        {
            report.Issues.Add(new DiagnosticIssue
            {
                Severity = IssueSeverity.Critical,
                Title = loc.Get("Diagnostic.PowerServiceStoppedTitle"),
                Description = loc.Get("Diagnostic.PowerServiceStoppedDesc"),
                SuggestedAction = loc.Get("Diagnostic.PowerServiceStoppedAction"),
                CanAutoFix = true,
                AutoFixAction = async () =>
                {
                    var (success, message) = await PowerServiceDiagnostics.TryAutoRepairAsync(CancellationToken.None);
                    return success;
                }
            });
        }

        // 3. 检查电源计划可用性
        var powerPlanResult = await _powerService.GetOrCreateGameOptimizerPlanAsync();
        report.HasPowerPlan = powerPlanResult.IsSuccess;
        
        if (!powerPlanResult.IsSuccess)
        {
            report.Issues.Add(new DiagnosticIssue
            {
                Severity = IssueSeverity.High,
                Title = loc.Get("Diagnostic.NoPowerPlanTitle"),
                Description = powerPlanResult.ErrorMessage ?? loc.Get("Diagnostic.NoPowerPlanDesc"),
                SuggestedAction = powerPlanResult.SuggestedAction ?? loc.Get("Diagnostic.NoPowerPlanAction"),
                CanAutoFix = false
            });
        }

        // 4. 检查系统还原状态
        var restoreStatus = await _restoreService.GetSystemRestoreStatusAsync();
        report.IsSystemRestoreEnabled = restoreStatus.IsEnabled;
        
        if (!restoreStatus.IsEnabled && restoreStatus.IsAdministrator)
        {
            report.Issues.Add(new DiagnosticIssue
            {
                Severity = IssueSeverity.Info,
                Title = loc.Get("Diagnostic.SystemRestoreDisabledTitle"),
                Description = loc.Get("Diagnostic.SystemRestoreDisabledDesc"),
                SuggestedAction = loc.Get("Diagnostic.SystemRestoreDisabledAction"),
                CanAutoFix = true,
                AutoFixAction = async () =>
                {
                    return await _restoreService.EnableSystemRestoreAsync();
                }
            });
        }

        report.TotalIssues = report.Issues.Count;
        report.CriticalIssues = report.Issues.Count(i => i.Severity == IssueSeverity.Critical);
        report.IsHealthy = report.CriticalIssues == 0;

        return report;
    }

    /// <summary>
    /// 尝试自动修复所有可修复的问题
    /// </summary>
    public async Task<AutoFixResult> AutoFixAllAsync(StartupDiagnosticReport report)
    {
        var result = new AutoFixResult();
        var fixableIssues = report.Issues.Where(i => i.CanAutoFix && i.AutoFixAction != null).ToList();

        foreach (var issue in fixableIssues)
        {
            try
            {
                var success = await issue.AutoFixAction!();
                if (success)
                {
                    result.FixedIssues.Add(issue.Title);
                }
                else
                {
                    result.FailedIssues.Add(issue.Title);
                }
            }
            catch (Exception ex)
            {
                result.FailedIssues.Add($"{issue.Title}: {ex.Message}");
            }
        }

        result.TotalAttempted = fixableIssues.Count;
        result.SuccessCount = result.FixedIssues.Count;
        result.FailureCount = result.FailedIssues.Count;

        return result;
    }
}

/// <summary>
/// 启动诊断报告
/// </summary>
public class StartupDiagnosticReport
{
    public bool IsHealthy { get; set; }
    public int TotalIssues { get; set; }
    public int CriticalIssues { get; set; }
    public bool HasAdminPrivileges { get; set; }
    public bool IsPowerServiceRunning { get; set; }
    public bool HasPowerPlan { get; set; }
    public bool IsSystemRestoreEnabled { get; set; }
    public List<DiagnosticIssue> Issues { get; set; } = new();
}

/// <summary>
/// 诊断问题
/// </summary>
public class DiagnosticIssue
{
    public IssueSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public bool CanAutoFix { get; set; }
    public Func<Task<bool>>? AutoFixAction { get; set; }
}

/// <summary>
/// 问题严重程度
/// </summary>
public enum IssueSeverity
{
    Info,
    Warning,
    High,
    Critical
}

/// <summary>
/// 自动修复结果
/// </summary>
public class AutoFixResult
{
    public int TotalAttempted { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> FixedIssues { get; set; } = new();
    public List<string> FailedIssues { get; set; } = new();
}
