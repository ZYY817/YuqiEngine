// File: GameOptimizer.Core/Services/ProcessPriority/IRegistryPriorityService.cs
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Services.ProcessPriority;

/// <summary>
/// 注册表进程优先级服务接口
/// 通过 IFEO (Image File Execution Options) 设置进程启动优先级
/// </summary>
public interface IRegistryPriorityService
{
    /// <summary>
    /// 设置进程优先级（写入注册表）
    /// </summary>
    /// <param name="executableName">可执行文件名（如 "game.exe"）</param>
    /// <param name="priority">优先级级别</param>
    /// <returns>操作结果</returns>
    OperationResult SetProcessPriority(string executableName, CpuPriorityLevel priority);
    
    /// <summary>
    /// 移除进程优先级配置（删除注册表项）
    /// </summary>
    /// <param name="executableName">可执行文件名</param>
    /// <returns>操作结果</returns>
    OperationResult RemoveProcessPriority(string executableName);
    
    /// <summary>
    /// 检查配置是否已存在于注册表
    /// </summary>
    /// <param name="executableName">可执行文件名</param>
    /// <returns>是否已配置</returns>
    bool IsConfigured(string executableName);
    
    /// <summary>
    /// 获取当前配置的优先级
    /// </summary>
    /// <param name="executableName">可执行文件名</param>
    /// <returns>优先级级别，如果未配置则返回 null</returns>
    CpuPriorityLevel? GetCurrentPriority(string executableName);
    
    /// <summary>
    /// 检查是否具有管理员权限
    /// </summary>
    /// <returns>是否为管理员</returns>
    bool HasAdminPrivileges();
}
