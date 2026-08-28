using System.Text.RegularExpressions;

namespace GameOptimizer.Core.Services;

/// <summary>
/// P1-8: 命令参数清理工具 - 防止命令注入攻击
/// </summary>
public static class CommandSanitizer
{
    private static readonly Regex DangerousCharsRegex = new(@"['""`$;|&<>(){}[\]\\!^%]", RegexOptions.Compiled);
    private static readonly Regex PathTraversalRegex = new(@"\.\.[\\/]", RegexOptions.Compiled);
    private static readonly Regex ValidPathRegex = new(@"^[a-zA-Z]:\\[^<>:""|?*]+$", RegexOptions.Compiled);
    private static readonly Regex ValidDomainRegex = new(@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z0-9-]{1,63})*$", RegexOptions.Compiled);

    /// <summary>
    /// 清理用于命令行参数的字符串，移除危险字符
    /// </summary>
    public static string SanitizeArgument(string? input, int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sanitized = DangerousCharsRegex.Replace(input, "");
        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }

    /// <summary>
    /// 清理还原点描述文本
    /// </summary>
    public static string SanitizeDescription(string? description, int maxLength = 64)
    {
        if (string.IsNullOrWhiteSpace(description)) return "手动创建";
        var sanitized = DangerousCharsRegex.Replace(description, "");
        sanitized = sanitized.Trim();
        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }

    /// <summary>
    /// 验证并清理文件路径，防止路径遍历攻击
    /// </summary>
    public static (bool IsValid, string SafePath) SanitizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) 
            return (false, string.Empty);

        // 检查路径遍历
        if (PathTraversalRegex.IsMatch(path))
            return (false, string.Empty);

        // 规范化路径
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!ValidPathRegex.IsMatch(fullPath))
                return (false, string.Empty);
            return (true, fullPath);
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    /// <summary>
    /// 验证主机名或IP地址格式
    /// </summary>
    public static bool IsValidHostOrIp(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        if (input.Length > 255) return false;
        if (System.Net.IPAddress.TryParse(input, out _)) return true;
        return ValidDomainRegex.IsMatch(input);
    }

    /// <summary>
    /// 脱敏路径中的用户名
    /// </summary>
    public static string SanitizePathForLogging(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        
        try
        {
            var userName = Environment.UserName;
            if (!string.IsNullOrEmpty(userName))
            {
                path = path.Replace($"\\{userName}\\", "\\[USER]\\", StringComparison.OrdinalIgnoreCase);
                path = path.Replace($"/{userName}/", "/[USER]/", StringComparison.OrdinalIgnoreCase);
            }
            
            // 脱敏 Users 目录下的用户名
            path = Regex.Replace(path, @"(?i)(C:\\Users\\)[^\\]+", "$1[USER]");
            path = Regex.Replace(path, @"(?i)(/home/)[^/]+", "$1[USER]");
            
            return path;
        }
        catch
        {
            return "[PATH_REDACTED]";
        }
    }

    /// <summary>
    /// 为 PowerShell 命令转义参数
    /// </summary>
    public static string EscapeForPowerShell(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "''";
        var escaped = input.Replace("'", "''");
        return $"'{escaped}'";
    }

    /// <summary>
    /// 为 CMD 命令转义参数
    /// </summary>
    public static string EscapeForCmd(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "\"\"";
        var escaped = input.Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }
}
