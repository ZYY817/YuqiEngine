using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Shell.Models;

/// <summary>
/// 应用程序设置模型
/// </summary>
public class AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private static string SettingsPath => AppPaths.SettingsFilePath;

    /// <summary>
    /// 配置文件版本号，用于升级时迁移配置
    /// </summary>
    public int ConfigVersion { get; set; } = 3;
    
    private const int CurrentConfigVersion = 3;

    /// <summary>
    /// 语言：zh-CN, en-US
    /// </summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>
    /// 是否显示高风险操作警告
    /// </summary>
    public bool ShowRiskWarnings { get; set; } = true;

    /// <summary>
    /// 是否启用实时监控
    /// </summary>
    public bool EnableRealTimeMonitoring { get; set; } = true;

    /// <summary>
    /// 监控刷新间隔（秒）
    /// </summary>
    public int MonitoringIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// 是否开机自启动
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// 是否最小化到托盘
    /// </summary>
    public bool MinimizeToTray { get; set; } = false;

    /// <summary>
    /// 日志保留天数
    /// </summary>
    public int LogRetentionDays { get; set; } = 30;

    /// <summary>
    /// 应用诊断日志和诊断包输出目录（为空时使用应用数据目录）。
    /// </summary>
    public string? LogBackupDirectory { get; set; }

    /// <summary>
    /// 游戏启动通知
    /// </summary>
    public bool NotifyOnGameLaunch { get; set; } = true;

    /// <summary>
    /// 声音提示
    /// </summary>
    public bool EnableSoundNotification { get; set; } = false;

    // OPT-003: 激进模式开关
    // P0-1: 激进模式状态变更事件
    public event EventHandler<bool>? AggressiveModeChanged;

    private bool _enableAggressiveMode = false;

    /// <summary>
    /// 是否启用激进模式（CPU 100%最小频率，最大化性能）
    /// </summary>
    public bool EnableAggressiveMode
    {
        get => _enableAggressiveMode;
        set
        {
            if (_enableAggressiveMode != value)
            {
                _enableAggressiveMode = value;
                AggressiveModeChanged?.Invoke(this, value);
            }
        }
    }

    // P2-001: 首次使用引导
    /// <summary>
    /// 是否已显示欢迎引导
    /// </summary>
    public bool HasShownWelcomeGuide { get; set; } = false;

    /// <summary>
    /// 引导优化风险提示：用户选择“不再提示”后置为 true。
    /// </summary>

    // P2-005: 窗口状态持久化
    /// <summary>
    /// 窗口左边距
    /// </summary>
    public double WindowLeft { get; set; } = double.NaN;

    /// <summary>
    /// 窗口顶边距
    /// </summary>
    public double WindowTop { get; set; } = double.NaN;

    /// <summary>
    /// 窗口宽度
    /// </summary>
    public double WindowWidth { get; set; } = 1280;

    /// <summary>
    /// 窗口高度
    /// </summary>
    public double WindowHeight { get; set; } = 800;

    /// <summary>
    /// 窗口是否最大化
    /// </summary>
    public bool WindowMaximized { get; set; } = false;

    /// <summary>
    /// 外设高级设置（鼠标/键盘/USB自定义参数）
    /// </summary>
    public PeripheralAdvancedSettings? PeripheralSettings { get; set; }

    // P0-001: 配置持久化 - 内存优化配置
    /// <summary>
    /// 内存优化配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? MemoryOptimizationSettings { get; set; }
    
    // P0-002: 配置持久化 - 系统概览优化模式选择
    /// <summary>
    /// 选中的优化模式（performance/balanced/safe）
    /// </summary>
    public string GitHubRepo { get; set; } = "ZYY817/YuqiEngine";
    public DateTime LastAutoUpdateCheckUtc { get; set; } = DateTime.MinValue;

    // P0-001: 配置持久化 - 性能加速配置
    /// <summary>
    /// 性能加速配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? PerformanceBoostSettings { get; set; }

    /// <summary>
    /// 电竞模式页面配置持久化。
    /// </summary>
    public EsportsModeConfig? EsportsModeSettings { get; set; }

    // P0-001: 配置持久化 - 模板驱动模块配置（10个模块）
    /// <summary>
    /// 音频优化配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? audioOptimizationSettings { get; set; }
    
    /// <summary>
    /// 处理器专项配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? cpuOptimizationSettings { get; set; }
    
    /// <summary>
    /// 图形渲染配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? graphicsOptimizationSettings { get; set; }
    
    /// <summary>
    /// 网络稳定配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? networkOptimizationSettings { get; set; }
    
    /// <summary>
    /// 隐私护盾配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? privacyOptimizationSettings { get; set; }
    
    /// <summary>
    /// 系统调校配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? tuningOptimizationSettings { get; set; }
    
    /// <summary>
    /// 系统服务配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? servicesOptimizationSettings { get; set; }
    
    /// <summary>
    /// 外设优化配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? peripheralOptimizationSettings { get; set; }
    
    /// <summary>
    /// 桌面体验配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? explorerOptimizationSettings { get; set; }
    
    /// <summary>
    /// 任务调度配置（开关状态）
    /// </summary>
    public ModuleOptimizationConfig? schedulerOptimizationSettings { get; set; }

    // P0-003: 配置持久化 - 电源计划编辑器配置
    /// <summary>
    /// 电源计划编辑器配置（所有电源设置）
    /// </summary>
    public PowerPlanEditorSettings? PowerPlanEditorSettings { get; set; }

    /// <summary>
    /// 最近一次已验证电源变更的恢复凭据。仅保存 receipt 标识，不保存或猜测系统值。
    /// </summary>
    public string? LastVerifiedPowerReceiptId { get; set; }

    // 优化守护服务配置
    /// <summary>
    /// 是否启用优化守护服务（防止系统自动恢复优化）
    /// </summary>
    public bool OptimizationGuardEnabled { get; set; } = false;

    /// <summary>
    /// 守护服务监控间隔（秒），默认30秒
    /// </summary>
    public int GuardMonitorIntervalSeconds { get; set; } = 30;
    
    /// <summary>
    /// 是否显示守护服务通知
    /// </summary>
    public bool GuardShowNotifications { get; set; } = true;
    
    /// <summary>
    /// 守护服务保护的优化项列表
    /// </summary>
    public List<string> GuardProtectedOperations { get; set; } = new();
    
    /// <summary>
    /// 是否首次启用守护服务（用于显示引导）
    /// </summary>
    public bool GuardFirstTimeEnabled { get; set; } = true;

    /// <summary>
    /// 待办高风险模块列表（用于"稍后提醒"功能）
    /// </summary>
    public List<string> PendingHighRiskModules { get; set; } = new();

    public string? BaselineMirrorDirectory { get; set; }

    private static readonly object _loadLock = new();
    private static bool _lastLoadWasRecovered;
    private static string? _lastLoadError;
    
    public static bool LastLoadWasRecovered 
    { 
        get { lock (_loadLock) return _lastLoadWasRecovered; }
        private set { lock (_loadLock) _lastLoadWasRecovered = value; }
    }
    
    public static string? LastLoadError 
    { 
        get { lock (_loadLock) return _lastLoadError; }
        private set { lock (_loadLock) _lastLoadError = value; }
    }

    public static AppSettings Load() => LoadFromPath(AppPaths.ResolveReadPath(SettingsPath));

    public static AppSettings LoadFromPath(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var durableBackupPath = settingsPath + ".recovery.bak";
        var legacyBackupPath = settingsPath + ".bak";
        LastLoadWasRecovered = false;
        LastLoadError = null;
        
        try
        {
            if (File.Exists(settingsPath))
            {
                var (content, error) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(settingsPath);
                if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(content))
                {
                    System.Diagnostics.Debug.WriteLine($"[AppSettings] 主配置读取失败: {error ?? "empty content"}");
                    LastLoadError = error ?? "Config file is empty";
                }
                else
                {
                    var settings = JsonSerializer.Deserialize<AppSettings>(content, SerializerOptions);
                    if (settings != null)
                    {
                        // P1-003/P1-028: 配置版本迁移
                        if (settings.ConfigVersion < CurrentConfigVersion)
                        {
                            settings = MigrateSettings(settings);
                            settings.SaveToPath(settingsPath);
                        }
                        return settings;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 主配置加载失败: {ex.Message}");
            LastLoadError = ex.Message;
        }
        
        // 尝试从备份恢复
        try
        {
            var backupPath = File.Exists(durableBackupPath) ? durableBackupPath : legacyBackupPath;
            if (File.Exists(backupPath))
            {
                var (backupContent, backupError) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(backupPath);
                if (string.IsNullOrEmpty(backupError) && !string.IsNullOrEmpty(backupContent))
                {
                    var backupSettings = JsonSerializer.Deserialize<AppSettings>(backupContent, SerializerOptions);
                    if (backupSettings != null)
                    {
                        LastLoadWasRecovered = true;
                        System.Diagnostics.Debug.WriteLine("[AppSettings] 已从备份恢复配置");
                        return backupSettings;
                    }
                }
            }
        }
        catch (Exception backupEx)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 备份恢复也失败: {backupEx.Message}");
        }
        return new AppSettings();
    }
    
    private static AppSettings MigrateSettings(AppSettings oldSettings)
    {
        System.Diagnostics.Debug.WriteLine($"[AppSettings] Migrating from v{oldSettings.ConfigVersion} to v{CurrentConfigVersion}");
        
        // v3 移除了无实际系统控制能力的 VisualMode 设置。重写配置会自然丢弃旧字段。
        
        oldSettings.ConfigVersion = CurrentConfigVersion;
        return oldSettings;
    }

    public bool Save() => SaveToPath(SettingsPath);

    public bool SaveToPath(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var durableBackupPath = settingsPath + ".recovery.bak";
        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(directory, out var dirError))
                {
                    System.Diagnostics.Debug.WriteLine($"[AppSettings] 创建目录失败: {dirError}");
                    return false;
                }
            }

            // 先备份现有配置
            if (File.Exists(settingsPath))
            {
                var (backupSuccess, backupError) = SafeFileHelper.CopyFile(settingsPath, durableBackupPath, overwrite: true);
                if (!backupSuccess)
                {
                    System.Diagnostics.Debug.WriteLine($"[AppSettings] 持久备份配置失败: {backupError}");
                    return false;
                }
            }

            var json = JsonSerializer.Serialize(this, SerializerOptions);
            
            var writeSuccess = SafeFileWriter.TryWriteAllText(settingsPath, json);
            if (!writeSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] 保存设置失败");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 保存设置失败: {ex.Message}");
            return false;
        }
    }
}

// P0-001: 模块优化配置类（通用）
public class ModuleOptimizationConfig
{
    /// <summary>
    /// 已启用的开关（OperationId -> IsEnabled）
    /// </summary>
    public Dictionary<string, bool> EnabledToggles { get; set; } = new();
}

// P0-003: 电源计划编辑器配置类
public class PowerPlanEditorSettings
{
    // 低延迟设置
    public int PerfIncreaseThreshold { get; set; } = 10;
    public int IdleDemoteThreshold { get; set; } = 10;
    public int CoreParkingMin { get; set; } = 0;
    public bool DisableAspm { get; set; } = false;
    public bool DisableUsbSuspend { get; set; } = false;
    
    // 处理器设置
    public int ProcessorMinState { get; set; } = 5;
    public int ProcessorMaxState { get; set; } = 100;
    public int BoostMode { get; set; } = 2;
    public bool DisableThrottle { get; set; } = false;
    
    // Intel 混合架构设置
    public int HeteroPolicy { get; set; } = 0;
    public int Class1EffThreshold { get; set; } = 55;
    public int Class1PerfLevel { get; set; } = 100;
    
    // AMD 设置
    public int AmdHeteroPolicy { get; set; } = 0;
    
    // 系统电源设置
    public int DiskTimeout { get; set; } = 0;
    public int DisplayTimeout { get; set; } = 10;
    public int SleepTimeout { get; set; } = 0;
}
