// File: GameOptimizer.Shell/Services/AiAssistant/AiDiagnosticsRunner.cs
using System.IO;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>只读诊断采集器抽象——测试可注入假实现。</summary>
public interface IAiDiagnosticsRunner
{
    Task<string> CollectAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 运行 Skill 自带的 collect-diagnostics.ps1（只读采集），
/// 返回报告中的 ## SUMMARY 关键信号段（完整报告落盘供用户查看）。
/// </summary>
public sealed class PowerShellDiagnosticsRunner : IAiDiagnosticsRunner
{
    private static readonly TimeSpan CollectTimeout = TimeSpan.FromSeconds(180);
    private const int MaxReturnedChars = 20000;

    private readonly AiSkillStore _skillStore;

    public PowerShellDiagnosticsRunner(AiSkillStore skillStore)
    {
        _skillStore = skillStore;
    }

    public async Task<string> CollectAsync(CancellationToken cancellationToken)
    {
        _skillStore.EnsureSeeded();
        var script = _skillStore.DiagnosticsScriptPath;
        if (!File.Exists(script))
            return "诊断脚本不存在：" + script;

        var reportDir = Path.Combine(AppPaths.LogsDir, "AiDiagnostics");
        Directory.CreateDirectory(reportDir);
        var reportPath = Path.Combine(reportDir,
            $"diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        var result = await ProcessRunner.RunAsync(
            "powershell",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -OutFile \"{reportPath}\"",
            CollectTimeout,
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(reportPath))
        {
            var err = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            return $"诊断脚本执行失败（退出码 {result.ExitCode}）：{err}";
        }

        var full = File.ReadAllText(reportPath);
        var summary = ExtractSummary(full);
        var header = $"诊断完成（完整报告：{reportPath}）。以下是关键信号摘要：\n\n";
        var body = summary.Length > MaxReturnedChars
            ? summary[..MaxReturnedChars] + "\n[摘要过长已截断，详见完整报告]"
            : summary;
        return header + body;
    }

    /// <summary>提取 ## SUMMARY 段到下一节为止；没有 SUMMARY 时返回全文。</summary>
    internal static string ExtractSummary(string fullReport)
    {
        const string marker = "## SUMMARY";
        var start = fullReport.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return fullReport;

        var after = fullReport.IndexOf("====", start + marker.Length, StringComparison.Ordinal);
        return after > start ? fullReport[start..after].Trim() : fullReport[start..].Trim();
    }
}
