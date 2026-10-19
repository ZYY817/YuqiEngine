namespace GameOptimizer.Core.Constants;

/// <summary>
/// 进程和操作超时常量
/// </summary>
public static class ProcessTimeouts
{
    /// <summary>快速命令超时 (5秒)</summary>
    public const int QuickCommand = 5000;
    
    /// <summary>标准命令超时 (10秒)</summary>
    public const int StandardCommand = 10000;
    
    /// <summary>长时间命令超时 (30秒)</summary>
    public const int LongCommand = 30000;
    
    /// <summary>非常长的命令超时 (60秒)</summary>
    public const int VeryLongCommand = 60000;
    
    /// <summary>网络请求超时 (15秒)</summary>
    public const int NetworkRequest = 15000;
    
    /// <summary>系统还原点创建超时 (120秒)</summary>
    public const int RestorePointCreation = 120000;
    
    /// <summary>重试间隔基数 (1秒)</summary>
    public const int RetryBaseDelay = 1000;
    
    /// <summary>最大重试次数</summary>
    public const int MaxRetryCount = 3;
}
