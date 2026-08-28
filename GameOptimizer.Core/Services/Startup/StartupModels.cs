using System;

namespace GameOptimizer.Core.Services.Startup
{
    public enum StartupItemType
    {
        Registry,       // 注册表启动项
        StartupFolder,  // 启动文件夹
        ScheduledTask,  // 计划任务
        Service         // 服务
    }

    public enum StartupImpact
    {
        Low,
        Medium,
        High,
        Unknown
    }

    public class StartupItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public StartupItemType Type { get; set; }
        public bool IsEnabled { get; set; } = true;
        public StartupImpact Impact { get; set; } = StartupImpact.Unknown;
        public string Publisher { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RegistryKey { get; set; } = string.Empty;
        public string RegistryValue { get; set; } = string.Empty;
    }

    public class StartupOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
