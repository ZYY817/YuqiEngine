// File: GameOptimizer.Core/Services/PermissionChecker.cs
using System;
using System.Diagnostics;
using System.Security.Principal;

namespace GameOptimizer.Core.Services;

/// <summary>
/// 权限检查助手
/// 统一权限检查和提示
/// </summary>
public static class PermissionChecker
{
    /// <summary>
    /// 检查是否以管理员身份运行
    /// </summary>
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PermissionChecker] Failed to check admin status: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 检查操作权限
    /// </summary>
    public static (bool HasPermission, string? ErrorMessage) CheckOperationPermission(string operationName)
    {
        if (!IsRunningAsAdministrator())
        {
            return (false, GetAdminRequiredMessage(operationName));
        }
        return (true, null);
    }
    
    /// <summary>
    /// 获取管理员权限提示消息
    /// </summary>
    public static string GetAdminRequiredMessage(string operationName)
    {
        return $"【需要管理员权限】\n\n" +
               $"操作 \"{operationName}\" 需要管理员权限才能执行。\n\n" +
               $"💡 解决方案：\n" +
               $"1. 关闭程序\n" +
               $"2. 右键程序图标\n" +
               $"3. 选择\"以管理员身份运行\"";
    }
    
    /// <summary>
    /// 检查是否可以以管理员身份重启
    /// </summary>
    public static bool CanRestartAsAdmin()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            return currentProcess.MainModule != null;
        }
        catch
        {
            return false;
        }
    }
}
