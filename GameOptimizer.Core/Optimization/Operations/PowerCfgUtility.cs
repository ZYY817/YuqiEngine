using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Optimization.Operations;

internal static class PowerCfgUtility
{
    private static readonly Regex ActiveSchemeRegex =
        new(@"(Power Scheme|电源方案)\s*GUID:\s*([0-9a-fA-F-]+)", RegexOptions.Compiled);
    
    private static readonly Regex SchemeListRegex =
        new(@"(Power Scheme|电源方案)\s*GUID:\s*([0-9a-fA-F-]+)\s+\(([^)]+)\)", RegexOptions.Compiled);
    
    // 可选的日志记录器（通过静态属性注入）
    public static ILogger? Logger { get; set; }
    
    // 动态获取系统编码（修复中文乱码问题）
    private static Encoding GetSystemEncoding()
    {
        try
        {
            // 优先使用控制台编码
            var consoleEncoding = Console.OutputEncoding;
            if (consoleEncoding != null && consoleEncoding.CodePage != 65001) // 不是UTF8
            {
                return consoleEncoding;
            }

            // 动态获取系统 OEM 代码页（中文=936, 英文=437, 日文=932 等）
            var oemCp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oemCp);
        }
        catch
        {
            try
            {
                var oemCp = System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                return Encoding.GetEncoding(oemCp);
            }
            catch
            {
                // Why: .NET 8 的 Encoding.Default 是 UTF-8，此处显式声明意图
                return Encoding.UTF8;
            }
        }
    }

    public static async Task<Guid?> GetActiveSchemeAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync("/GETACTIVESCHEME", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            Logger?.LogError("[PowerCfg] GetActiveScheme failed with ExitCode={ExitCode}, StdErr={StdErr}", result.ExitCode, result.StdErr);
            return null;
        }

        var match = ActiveSchemeRegex.Match(result.StdOut);
        if (!match.Success)
        {
            Logger?.LogError("[PowerCfg] GetActiveScheme regex match failed. StdOut={StdOut}", result.StdOut);
            return null;
        }
        
        if (!Guid.TryParse(match.Groups[2].Value, out var guid))
        {
            Logger?.LogError("[PowerCfg] GetActiveScheme GUID parse failed. Value={Value}", match.Groups[2].Value);
            return null;
        }

        Logger?.LogInformation("[PowerCfg] GetActiveScheme success: {Guid}", guid);
        return guid;
    }

    // Why: powercfg /QUERY 会合并全局定义层、计划默认值层、用户覆盖层三层数据，
    // 直接读注册表 User\PowerSchemes 只能读到用户覆盖层，非标准电源计划（如卓越性能）可能没有用户覆盖层数据
    private static readonly Regex QueryAcValueRegex =
        new(@"(?:Current AC Power Setting Index|当前交流电源设置索引)\s*[:：]\s*0x([0-9a-fA-F]+)", RegexOptions.Compiled);
    private static readonly Regex QueryDcValueRegex =
        new(@"(?:Current DC Power Setting Index|当前直流电源设置索引)\s*[:：]\s*0x([0-9a-fA-F]+)", RegexOptions.Compiled);

    public static async Task<(int? Ac, int? Dc)> QuerySettingValueAsync(
        Guid scheme, Guid subGroup, Guid setting, CancellationToken cancellationToken)
    {
        var schemeStr = scheme.ToString("D").ToUpperInvariant();
        var subGroupStr = subGroup.ToString("D").ToUpperInvariant();
        var settingStr = setting.ToString("D").ToUpperInvariant();

        var result = await RunAsync($"/QUERY {schemeStr} {subGroupStr} {settingStr}", cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            Logger?.LogWarning("[PowerCfg] QuerySettingValue failed: ExitCode={ExitCode}, StdErr={StdErr}", result.ExitCode, result.StdErr);
            return (null, null);
        }

        int? ac = null;
        int? dc = null;

        var acMatch = QueryAcValueRegex.Match(result.StdOut);
        if (acMatch.Success)
        {
            ac = Convert.ToInt32(acMatch.Groups[1].Value, 16);
        }

        var dcMatch = QueryDcValueRegex.Match(result.StdOut);
        if (dcMatch.Success)
        {
            dc = Convert.ToInt32(dcMatch.Groups[1].Value, 16);
        }

        Logger?.LogInformation("[PowerCfg] QuerySettingValue: Scheme={Scheme}, Setting={Setting} => AC={Ac}, DC={Dc}",
            schemeStr, settingStr, ac, dc);

        return (ac, dc);
    }

    public static async Task<Guid?> GetActiveSchemeWithFallbackAsync(CancellationToken cancellationToken)
    {
        var activeScheme = await GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (activeScheme.HasValue)
        {
            return activeScheme;
        }

        var warningMsg = "[PowerCfg] 无法获取激活的电源计划，尝试从电源计划列表中获取";
        Debug.WriteLine(warningMsg);
        Logger?.LogWarning(warningMsg);
        
        var listResult = await RunAsync("/LIST", cancellationToken).ConfigureAwait(false);
        if (listResult.ExitCode == 0)
        {
            var matches = SchemeListRegex.Matches(listResult.StdOut);
            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    if (Guid.TryParse(match.Groups[2].Value, out var schemeGuid))
                    {
                        var schemeName = match.Groups[3].Value;
                        Debug.WriteLine($"[PowerCfg] 找到电源计划: {schemeName} ({schemeGuid})");
                        
                        // Why: powercfg 命令统一使用无大括号 GUID 格式
                        var setResult = await RunAsync($"/SETACTIVE {schemeGuid:D}", cancellationToken).ConfigureAwait(false);
                        if (setResult.ExitCode == 0)
                        {
                            Logger?.LogInformation("[PowerCfg] 成功激活电源计划: {SchemeName}", schemeName);
                            return schemeGuid;
                        }
                    }
                }
            }
        }

        var errorMsg = "[PowerCfg] 无法获取或激活任何现有电源计划，建议运行诊断";
        Debug.WriteLine(errorMsg);
        Logger?.LogError(errorMsg);
        return null;
    }
    
    public static async Task<(Guid? Scheme, string DiagnosticInfo)> GetActiveSchemeWithDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var scheme = await GetActiveSchemeWithFallbackAsync(cancellationToken).ConfigureAwait(false);
        if (scheme.HasValue)
        {
            Logger?.LogInformation("[PowerCfg] 电源计划获取成功: {SchemeGuid}", scheme.Value);
            return (scheme, "电源计划正常");
        }
        
        Logger?.LogWarning("[PowerCfg] 无法获取电源计划，开始诊断");
        var diagnostics = await Services.PowerServiceDiagnostics.DiagnoseAsync(cancellationToken).ConfigureAwait(false);
        
        var info = $"诊断结果:\n" +
                   $"• 管理员权限: {(diagnostics.IsAdministrator ? "是" : "否 ⚠️")}\n" +
                   $"• Power服务状态: {(diagnostics.IsPowerServiceRunning ? "运行中" : "未运行 ⚠️")}\n" +
                   $"• 可用电源计划: {diagnostics.AvailablePlans.Count} 个\n" +
                   $"• 建议: {GetSuggestion(diagnostics)}";
        
        Debug.WriteLine($"[PowerCfg] {info}");
        Logger?.LogError("[PowerCfg] 电源计划诊断失败:\n{DiagnosticInfo}", info);
        
        return (null, diagnostics.DetailedDiagnostic ?? info);
    }
    
    private static string GetSuggestion(Services.PowerServiceStatus diagnostics)
    {
        if (!diagnostics.IsAdministrator)
            return Services.ElevationGate.DescribeWriteDenial("读取电源计划");
        if (!diagnostics.IsPowerServiceRunning)
            return "请启动 Power 服务 (services.msc)";
        if (diagnostics.AvailablePlans.Count == 0)
            return "系统电源计划异常，请运行 powercfg /restoredefaultschemes";
        return "请检查系统电源设置";
    }

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        Logger?.LogInformation("[PowerCfg] 执行命令: powercfg {Arguments}", arguments);
        
        // Why: powercfg 是系统命令，输出使用 OEM 编码，UTF-8 会导致中文乱码
        var systemEncoding = GetSystemEncoding();
        
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = systemEncoding,
                StandardErrorEncoding = systemEncoding
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!process.Start())
        {
            Logger?.LogError("[PowerCfg] 无法启动 powercfg.exe");
            return (-1, string.Empty, "无法启动 powercfg.exe");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProcessTimeouts.StandardCommand);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, stdout.ToString(), "powercfg 命令执行超时");
        }
        
        var exitCode = process.ExitCode;
        var stdoutStr = stdout.ToString();
        var stderrStr = stderr.ToString();
        
        if (exitCode != 0)
        {
            Logger?.LogWarning("[PowerCfg] 命令执行失败 (ExitCode={ExitCode}): powercfg {Arguments}\nStdErr: {StdErr}", exitCode, arguments, stderrStr);
        }
        
        return (exitCode, stdoutStr, stderrStr);
    }


}
