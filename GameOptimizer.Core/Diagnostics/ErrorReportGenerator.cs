// File: GameOptimizer.Core/Diagnostics/ErrorReportGenerator.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Diagnostics;

public class ErrorReportGenerator
{
    public ErrorReport GenerateReport(List<DiagnosticResult> diagnostics)
    {
        var systemInfo = CollectSystemInfo();
        
        return new ErrorReport
        {
            ReportId = Guid.NewGuid().ToString("N"),
            GeneratedAt = DateTime.Now,
            SystemInfo = systemInfo,
            Errors = diagnostics
        };
    }

    public async Task<string> ExportReportAsync(ErrorReport report, string format, CancellationToken cancellationToken = default)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => await ExportAsJsonAsync(report, cancellationToken),
            "html" => await ExportAsHtmlAsync(report, cancellationToken),
            "text" or _ => await Task.FromResult(report.ToText())
        };
    }

    public async Task SaveReportToFileAsync(ErrorReport report, string filePath, string format, CancellationToken cancellationToken = default)
    {
        var content = await ExportReportAsync(report, format, cancellationToken);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
    }

    private async Task<string> ExportAsJsonAsync(ErrorReport report, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(new
        {
            report.ReportId,
            GeneratedAt = report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            report.SystemInfo,
            ErrorCount = report.Errors.Count,
            Errors = report.Errors.Select(e => new
            {
                ErrorCode = e.Code.ToString(),
                e.Category,
                e.Severity,
                e.Title,
                e.UserMessage,
                e.TechnicalDetails,
                e.PossibleCauses,
                Solutions = e.Solutions.Select(s => new
                {
                    s.Order,
                    s.Description,
                    s.Command,
                    s.RequiresAdmin,
                    s.RequiresReboot
                }),
                Timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                Context = new
                {
                    e.Context.OperationType,
                    e.Context.TargetResource,
                    e.Context.IsAdminMode
                }
            })
        }, options);

        return await Task.FromResult(json);
    }

    private async Task<string> ExportAsHtmlAsync(ErrorReport report, CancellationToken cancellationToken)
    {
        var html = new StringBuilder();
        
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang='zh-CN'>");
        html.AppendLine("<head>");
        html.AppendLine("    <meta charset='UTF-8'>");
        html.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        html.AppendLine("    <title>GameOptimizer 错误诊断报告</title>");
        html.AppendLine("    <style>");
        html.AppendLine("        body { font-family: 'Microsoft YaHei', Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
        html.AppendLine("        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }");
        html.AppendLine("        h1 { color: #333; border-bottom: 3px solid #4CAF50; padding-bottom: 10px; }");
        html.AppendLine("        h2 { color: #555; margin-top: 30px; }");
        html.AppendLine("        .meta { background: #f9f9f9; padding: 15px; border-radius: 4px; margin: 20px 0; }");
        html.AppendLine("        .meta-item { margin: 5px 0; }");
        html.AppendLine("        .error-card { border: 1px solid #ddd; border-radius: 4px; padding: 20px; margin: 20px 0; background: #fafafa; }");
        html.AppendLine("        .error-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 15px; }");
        html.AppendLine("        .error-code { font-family: 'Consolas', monospace; background: #333; color: #fff; padding: 5px 10px; border-radius: 3px; font-size: 14px; }");
        html.AppendLine("        .severity-critical { color: #d32f2f; font-weight: bold; }");
        html.AppendLine("        .severity-high { color: #f57c00; font-weight: bold; }");
        html.AppendLine("        .severity-medium { color: #fbc02d; font-weight: bold; }");
        html.AppendLine("        .severity-low { color: #388e3c; font-weight: bold; }");
        html.AppendLine("        .error-title { font-size: 18px; font-weight: bold; color: #333; }");
        html.AppendLine("        .section { margin: 15px 0; }");
        html.AppendLine("        .section-title { font-weight: bold; color: #555; margin-bottom: 8px; }");
        html.AppendLine("        .user-message { background: #e3f2fd; padding: 12px; border-left: 4px solid #2196F3; border-radius: 3px; }");
        html.AppendLine("        .technical-details { background: #f5f5f5; padding: 12px; border-radius: 3px; font-family: 'Consolas', monospace; font-size: 12px; white-space: pre-wrap; }");
        html.AppendLine("        .causes-list, .solutions-list { margin: 10px 0; padding-left: 20px; }");
        html.AppendLine("        .causes-list li, .solutions-list li { margin: 5px 0; }");
        html.AppendLine("        .solution-item { background: #fff; padding: 10px; margin: 8px 0; border-left: 3px solid #4CAF50; border-radius: 3px; }");
        html.AppendLine("        .solution-command { background: #263238; color: #aed581; padding: 8px; border-radius: 3px; font-family: 'Consolas', monospace; margin-top: 5px; }");
        html.AppendLine("        .badge { display: inline-block; padding: 3px 8px; border-radius: 3px; font-size: 11px; margin-left: 5px; }");
        html.AppendLine("        .badge-admin { background: #ff9800; color: white; }");
        html.AppendLine("        .badge-reboot { background: #f44336; color: white; }");
        html.AppendLine("        .timestamp { color: #999; font-size: 12px; }");
        html.AppendLine("    </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("    <div class='container'>");
        html.AppendLine("        <h1>GameOptimizer 错误诊断报告</h1>");
        
        html.AppendLine("        <div class='meta'>");
        html.AppendLine($"            <div class='meta-item'><strong>报告ID:</strong> {report.ReportId}</div>");
        html.AppendLine($"            <div class='meta-item'><strong>生成时间:</strong> {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}</div>");
        html.AppendLine($"            <div class='meta-item'><strong>系统信息:</strong> {report.SystemInfo}</div>");
        html.AppendLine($"            <div class='meta-item'><strong>错误总数:</strong> {report.Errors.Count}</div>");
        html.AppendLine("        </div>");

        foreach (var error in report.Errors)
        {
            var severityClass = error.Severity switch
            {
                ErrorSeverity.Critical => "severity-critical",
                ErrorSeverity.High => "severity-high",
                ErrorSeverity.Medium => "severity-medium",
                ErrorSeverity.Low => "severity-low",
                _ => ""
            };

            html.AppendLine("        <div class='error-card'>");
            html.AppendLine("            <div class='error-header'>");
            html.AppendLine($"                <div class='error-title'>{error.Title}</div>");
            html.AppendLine("                <div>");
            html.AppendLine($"                    <span class='error-code'>{error.Code}</span>");
            html.AppendLine($"                    <span class='{severityClass}' style='margin-left: 10px;'>{error.Severity}</span>");
            html.AppendLine("                </div>");
            html.AppendLine("            </div>");
            html.AppendLine($"            <div class='timestamp'>发生时间: {error.Timestamp:yyyy-MM-dd HH:mm:ss}</div>");

            html.AppendLine("            <div class='section'>");
            html.AppendLine("                <div class='section-title'>用户消息</div>");
            html.AppendLine($"                <div class='user-message'>{error.UserMessage}</div>");
            html.AppendLine("            </div>");

            if (!string.IsNullOrEmpty(error.TechnicalDetails))
            {
                html.AppendLine("            <div class='section'>");
                html.AppendLine("                <div class='section-title'>技术详情</div>");
                html.AppendLine($"                <div class='technical-details'>{System.Web.HttpUtility.HtmlEncode(error.TechnicalDetails)}</div>");
                html.AppendLine("            </div>");
            }

            if (error.PossibleCauses.Count > 0)
            {
                html.AppendLine("            <div class='section'>");
                html.AppendLine("                <div class='section-title'>可能原因</div>");
                html.AppendLine("                <ul class='causes-list'>");
                foreach (var cause in error.PossibleCauses)
                {
                    html.AppendLine($"                    <li>{cause}</li>");
                }
                html.AppendLine("                </ul>");
                html.AppendLine("            </div>");
            }

            if (error.Solutions.Count > 0)
            {
                html.AppendLine("            <div class='section'>");
                html.AppendLine("                <div class='section-title'>解决方案</div>");
                foreach (var solution in error.Solutions)
                {
                    html.AppendLine("                <div class='solution-item'>");
                    html.Append($"                    <strong>{solution.Order}.</strong> {solution.Description}");
                    if (solution.RequiresAdmin)
                        html.Append("<span class='badge badge-admin'>需要管理员</span>");
                    if (solution.RequiresReboot)
                        html.Append("<span class='badge badge-reboot'>需要重启</span>");
                    html.AppendLine();
                    
                    if (!string.IsNullOrEmpty(solution.Command))
                    {
                        html.AppendLine($"                    <div class='solution-command'>命令: {solution.Command}</div>");
                    }
                    html.AppendLine("                </div>");
                }
                html.AppendLine("            </div>");
            }

            html.AppendLine("        </div>");
        }

        html.AppendLine("    </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return await Task.FromResult(html.ToString());
    }

    private string CollectSystemInfo()
    {
        try
        {
            var os = Environment.OSVersion;
            var is64Bit = Environment.Is64BitOperatingSystem;
            var processorCount = Environment.ProcessorCount;
            var machineName = Environment.MachineName;
            var clrVersion = Environment.Version;

            return $"Windows {os.Version} ({(is64Bit ? "64-bit" : "32-bit")}), {processorCount} 核心, 机器名: {machineName}, CLR: {clrVersion}";
        }
        catch
        {
            return "无法获取系统信息";
        }
    }
}
