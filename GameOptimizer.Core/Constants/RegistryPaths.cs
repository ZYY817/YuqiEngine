namespace GameOptimizer.Core.Constants;

public static class RegistryPaths
{
    // 电源计划基础路径
    public const string PowerSchemesBase = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
    
    // 服务路径
    public const string ServicesBase = @"SYSTEM\CurrentControlSet\Services";
    public const string SysMainService = @"SYSTEM\CurrentControlSet\Services\SysMain";
    
    // 优先级控制
    public const string PriorityControl = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    
    // 输入设备
    public const string MouseClassParameters = @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
    public const string KeyboardClassParameters = @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";
    
    // 游戏配置
    public const string GameConfigStore = @"System\GameConfigStore";
    
    // 多媒体系统配置
    public const string MultimediaSystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    
    // 系统还原
    public const string SystemRestore = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    
    // 系统版本信息
    public const string WindowsVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    
    // 启动项
    public const string StartupRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    
    // AMD 显卡
    public const string AmdDisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    
    // 键盘设备类
    public const string KeyboardDeviceClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-e325-11ce-bfc1-08002be10318}";
}
