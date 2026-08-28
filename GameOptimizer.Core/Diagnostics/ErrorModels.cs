// File: GameOptimizer.Core/Diagnostics/ErrorModels.cs
using System;
using System.Collections.Generic;

namespace GameOptimizer.Core.Diagnostics;

public enum ErrorCategory
{
    Unknown,
    Permission,
    FileSystem,
    Network,
    Service,
    Hardware,
    Power,
    Security,
    System,
    Configuration
}

public enum ErrorSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public readonly struct ErrorCode
{
    public ErrorCategory Category { get; init; }
    public ErrorSeverity Severity { get; init; }
    public int Number { get; init; }

    public ErrorCode(ErrorCategory category, ErrorSeverity severity, int number)
    {
        Category = category;
        Severity = severity;
        Number = number;
    }

    public override string ToString()
    {
        var categoryCode = Category switch
        {
            ErrorCategory.Permission => "PERM",
            ErrorCategory.FileSystem => "FILE",
            ErrorCategory.Network => "NET",
            ErrorCategory.Service => "SVC",
            ErrorCategory.Hardware => "HW",
            ErrorCategory.Power => "PWR",
            ErrorCategory.Security => "SEC",
            ErrorCategory.System => "SYS",
            ErrorCategory.Configuration => "CFG",
            _ => "UNK"
        };

        var severityCode = Severity switch
        {
            ErrorSeverity.Critical => "CRIT",
            ErrorSeverity.High => "HIGH",
            ErrorSeverity.Medium => "MED",
            ErrorSeverity.Low => "LOW",
            _ => "UNK"
        };

        return $"GO-{categoryCode}-{severityCode}-{Number:D4}";
    }
}

public class DiagnosticContext
{
    public string OperationType { get; set; } = string.Empty;
    public string TargetResource { get; set; } = string.Empty;
    public bool IsAdminMode { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

public class DiagnosticResult
{
    public ErrorCode Code { get; set; }
    public ErrorCategory Category { get; set; }
    public ErrorSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
    public string TechnicalDetails { get; set; } = string.Empty;
    public List<string> PossibleCauses { get; set; } = new();
    public List<SolutionStep> Solutions { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public DiagnosticContext Context { get; set; } = new();
    public Exception? OriginalException { get; set; }
}

public class SolutionStep
{
    public int Order { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Command { get; set; }
    public bool RequiresAdmin { get; set; }
    public bool RequiresReboot { get; set; }
}

public class ErrorReport
{
    public string ReportId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public string SystemInfo { get; set; } = string.Empty;
    public List<DiagnosticResult> Errors { get; set; } = new();
    
    public string ToText()
    {
        var lines = new List<string>
        {
            "=".PadRight(80, '='),
            "GameOptimizer 错误诊断报告",
            "=".PadRight(80, '='),
            $"报告ID: {ReportId}",
            $"生成时间: {GeneratedAt:yyyy-MM-dd HH:mm:ss}",
            $"系统信息: {SystemInfo}",
            "",
            $"错误总数: {Errors.Count}",
            ""
        };

        foreach (var error in Errors)
        {
            lines.Add("-".PadRight(80, '-'));
            lines.Add($"错误码: {error.Code}");
            lines.Add($"类别: {error.Category} | 严重程度: {error.Severity}");
            lines.Add($"标题: {error.Title}");
            lines.Add($"时间: {error.Timestamp:yyyy-MM-dd HH:mm:ss}");
            lines.Add("");
            lines.Add("用户消息:");
            lines.Add($"  {error.UserMessage}");
            lines.Add("");
            
            if (!string.IsNullOrEmpty(error.TechnicalDetails))
            {
                lines.Add("技术详情:");
                lines.Add($"  {error.TechnicalDetails}");
                lines.Add("");
            }

            if (error.PossibleCauses.Count > 0)
            {
                lines.Add("可能原因:");
                foreach (var cause in error.PossibleCauses)
                {
                    lines.Add($"  • {cause}");
                }
                lines.Add("");
            }

            if (error.Solutions.Count > 0)
            {
                lines.Add("解决方案:");
                foreach (var solution in error.Solutions)
                {
                    var prefix = solution.RequiresAdmin ? "[需要管理员] " : "";
                    var suffix = solution.RequiresReboot ? " [需要重启]" : "";
                    lines.Add($"  {solution.Order}. {prefix}{solution.Description}{suffix}");
                    if (!string.IsNullOrEmpty(solution.Command))
                    {
                        lines.Add($"     命令: {solution.Command}");
                    }
                }
                lines.Add("");
            }
        }

        lines.Add("=".PadRight(80, '='));
        lines.Add("报告结束");
        lines.Add("=".PadRight(80, '='));

        return string.Join(Environment.NewLine, lines);
    }
}
