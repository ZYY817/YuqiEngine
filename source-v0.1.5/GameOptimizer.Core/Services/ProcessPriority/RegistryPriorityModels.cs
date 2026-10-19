// File: GameOptimizer.Core/Services/ProcessPriority/RegistryPriorityModels.cs
using System;

namespace GameOptimizer.Core.Services.ProcessPriority;

/// <summary>
/// CPU优先级级别（对应注册表DWORD值）
/// </summary>
public enum CpuPriorityLevel
{
    /// <summary>空闲 - 仅在系统空闲时运行</summary>
    Idle = 1,
    
    /// <summary>低于标准 - 适合后台任务</summary>
    BelowNormal = 2,
    
    /// <summary>标准 - 系统默认</summary>
    Normal = 3,
    
    /// <summary>高于标准 - 推荐用于游戏</summary>
    AboveNormal = 4,
    
    /// <summary>高 - 高性能需求</summary>
    High = 5,
    
    /// <summary>实时 - 危险，可能导致系统不稳定</summary>
    Realtime = 6
}

/// <summary>
/// 进程优先级配置模型
/// </summary>
public class ProcessPriorityConfig
{
    /// <summary>唯一标识符</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>游戏名称（用户友好显示）</summary>
    public string GameName { get; set; } = string.Empty;
    
    /// <summary>可执行文件名（仅文件名，如 "game.exe"）</summary>
    public string ExecutableName { get; set; } = string.Empty;
    
    /// <summary>可执行文件完整路径</summary>
    public string ExecutablePath { get; set; } = string.Empty;
    
    /// <summary>优先级设置</summary>
    public CpuPriorityLevel Priority { get; set; } = CpuPriorityLevel.AboveNormal;
    
    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>最后修改时间</summary>
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    
    /// <summary>备注信息</summary>
    public string Notes { get; set; } = string.Empty;
}
