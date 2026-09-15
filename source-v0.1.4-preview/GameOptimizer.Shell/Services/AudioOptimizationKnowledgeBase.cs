using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// 音频设备类型
/// </summary>
public enum AudioDeviceType
{
    Unknown,
    Onboard,        // 板载声卡
    UsbDac,         // USB DAC/声卡
    Professional,   // 专业声卡
    Bluetooth,      // 蓝牙音频
    Hdmi            // HDMI 音频
}

/// <summary>
/// 音频使用场景
/// </summary>
public enum AudioScenario
{
    Gaming,         // 游戏（低延迟优先）
    Music,          // 音乐欣赏（音质优先）
    Production,     // 音乐制作（专业级）
    Communication,  // 通讯（语音优先）
    General         // 通用
}

/// <summary>
/// 音频优化规则
/// </summary>
public class AudioOptimizationRule
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public int Priority { get; init; }
    
    // 条件判断
    public AudioDeviceType? RequireDeviceType { get; init; }
    public AudioScenario? RequireScenario { get; init; }
    
    // 推荐设置
    public bool DisableEnhancements { get; init; }
    public bool EnableExclusiveMode { get; init; }
    public bool DisableSpatialSound { get; init; }
    public bool OptimizeBuffer { get; init; }
    public bool DisableServiceRestart { get; init; }
    public bool OptimizeSampleRate { get; init; }
    public bool DisableCommDucking { get; init; }
    
    // 推荐采样率
    public int RecommendedSampleRate { get; init; } = 48000;
    public int RecommendedBitDepth { get; init; } = 24;
    
    // 说明
    public List<string> Warnings { get; init; } = new();
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// 计算出的最优音频设置
/// </summary>
public class OptimalAudioSettings
{
    public string RuleId { get; set; } = "";
    public string RuleDescription { get; set; } = "";
    
    // 优化开关
    public bool DisableEnhancements { get; set; }
    public bool EnableExclusiveMode { get; set; }
    public bool DisableSpatialSound { get; set; }
    public bool OptimizeBuffer { get; set; }
    public bool DisableServiceRestart { get; set; }
    public bool OptimizeSampleRate { get; set; }
    public bool DisableCommDucking { get; set; }
    
    // 采样率设置
    public int RecommendedSampleRate { get; set; } = 48000;
    public int RecommendedBitDepth { get; set; } = 24;
    
    // 预计延迟降低
    public int EstimatedLatencyReduction { get; set; }  // 毫秒
    
    // 说明
    public List<string> EnabledOperations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// 音频优化知识库 - 基于设备和场景动态计算最优设置
/// </summary>
public static class AudioOptimizationKnowledgeBase
{
    private static readonly List<AudioOptimizationRule> _rules = new();
    
    static AudioOptimizationKnowledgeBase()
    {
        InitializeRules();
    }

    private static void InitializeRules()
    {
        // ========== 游戏场景 - 板载声卡 ==========
        _rules.Add(new AudioOptimizationRule
        {
            Id = "gaming_onboard",
            Description = "游戏优化 (板载声卡)",
            Priority = 100,
            RequireDeviceType = AudioDeviceType.Onboard,
            RequireScenario = AudioScenario.Gaming,
            
            DisableEnhancements = true,     // 禁用所有增强，降低延迟
            EnableExclusiveMode = true,     // 独占模式
            DisableSpatialSound = true,     // 禁用空间音效
            OptimizeBuffer = true,          // 优化缓冲区
            DisableServiceRestart = false,  // 保留服务重启
            OptimizeSampleRate = true,
            DisableCommDucking = true,
            
            RecommendedSampleRate = 48000,
            RecommendedBitDepth = 16,       // 游戏16bit足够
            
            Recommendations = new() 
            { 
                "💡 板载声卡游戏优化：禁用所有增强效果可降低约 5-10ms 延迟",
                "💡 如果使用耳机，建议在游戏内启用 HRTF 而非 Windows 空间音效"
            }
        });

        // ========== 游戏场景 - USB DAC ==========
        _rules.Add(new AudioOptimizationRule
        {
            Id = "gaming_usb_dac",
            Description = "游戏优化 (USB DAC)",
            Priority = 95,
            RequireDeviceType = AudioDeviceType.UsbDac,
            RequireScenario = AudioScenario.Gaming,
            
            DisableEnhancements = true,
            EnableExclusiveMode = true,
            DisableSpatialSound = true,
            OptimizeBuffer = true,
            DisableServiceRestart = false,
            OptimizeSampleRate = true,
            DisableCommDucking = true,
            
            RecommendedSampleRate = 48000,
            RecommendedBitDepth = 24,
            
            Recommendations = new() 
            { 
                "💡 USB DAC 建议使用 ASIO 驱动以获得最低延迟",
                "💡 部分 USB DAC 有专用驱动，建议安装厂商驱动"
            }
        });

        // ========== 游戏场景 - 蓝牙 ==========
        _rules.Add(new AudioOptimizationRule
        {
            Id = "gaming_bluetooth",
            Description = "游戏优化 (蓝牙音频)",
            Priority = 90,
            RequireDeviceType = AudioDeviceType.Bluetooth,
            RequireScenario = AudioScenario.Gaming,
            
            DisableEnhancements = true,
            EnableExclusiveMode = false,    // 蓝牙不建议独占
            DisableSpatialSound = true,
            OptimizeBuffer = false,         // 蓝牙缓冲区不可调
            DisableServiceRestart = false,
            OptimizeSampleRate = false,     // 蓝牙采样率固定
            DisableCommDucking = true,
            
            Warnings = new() 
            { 
                "⚠️ 蓝牙音频天生有 40-200ms 延迟，不适合竞技游戏",
                "⚠️ 建议使用有线耳机或 2.4G 无线耳机"
            },
            Recommendations = new() 
            { 
                "💡 如果必须使用蓝牙，选择支持 aptX Low Latency 的设备"
            }
        });

        // ========== 音乐欣赏场景 ==========
        _rules.Add(new AudioOptimizationRule
        {
            Id = "music_listening",
            Description = "音乐欣赏优化",
            Priority = 85,
            RequireScenario = AudioScenario.Music,
            
            DisableEnhancements = false,    // 保留增强效果
            EnableExclusiveMode = true,     // 独占模式获得最佳音质
            DisableSpatialSound = false,    // 保留空间音效
            OptimizeBuffer = false,         // 不需要低延迟
            DisableServiceRestart = false,
            OptimizeSampleRate = true,
            DisableCommDucking = true,
            
            RecommendedSampleRate = 48000,
            RecommendedBitDepth = 24,
            
            Recommendations = new() 
            { 
                "💡 音乐欣赏建议保留音频增强效果",
                "💡 如果有高品质音源，可设置更高采样率"
            }
        });

        // ========== 专业音频制作 ==========
        _rules.Add(new AudioOptimizationRule
        {
            Id = "audio_production",
            Description = "专业音频制作",
            Priority = 80,
            RequireDeviceType = AudioDeviceType.Professional,
            RequireScenario = AudioScenario.Production,
            
            DisableEnhancements = true,     // 专业制作禁用所有增强
            EnableExclusiveMode = true,
            DisableSpatialSound = true,
            OptimizeBuffer = true,
            DisableServiceRestart = true,   // 防止服务中断录音
            OptimizeSampleRate = true,
            DisableCommDucking = true,
            
            RecommendedSampleRate = 96000,  // 专业级采样率
            RecommendedBitDepth = 24,
            
            Recommendations = new() 
            { 
                "💡 专业音频制作建议使用 ASIO 驱动",
                "💡 建议设置 96kHz/24bit 以获得最佳录音质量"
            }
        });

        // ========== 通讯场景 ==========
        _rules.Add(new AudioOptimizationRule
        {
            Id = "communication",
            Description = "语音通讯优化",
            Priority = 75,
            RequireScenario = AudioScenario.Communication,
            
            DisableEnhancements = false,    // 保留降噪等增强
            EnableExclusiveMode = false,    // 不独占，允许多应用
            DisableSpatialSound = true,
            OptimizeBuffer = false,
            DisableServiceRestart = false,
            OptimizeSampleRate = true,
            DisableCommDucking = false,     // 保留通讯音量调节
            
            RecommendedSampleRate = 48000,
            RecommendedBitDepth = 16,
            
            Recommendations = new() 
            { 
                "💡 语音通讯建议保留降噪和回声消除功能"
            }
        });

        // ========== 通用游戏优化（默认） ==========
        _rules.Add(new AudioOptimizationRule
        {
            Id = "gaming_generic",
            Description = "通用游戏优化",
            Priority = 50,
            RequireScenario = AudioScenario.Gaming,
            
            DisableEnhancements = true,
            EnableExclusiveMode = true,
            DisableSpatialSound = true,
            OptimizeBuffer = true,
            DisableServiceRestart = false,
            OptimizeSampleRate = true,
            DisableCommDucking = true,
            
            RecommendedSampleRate = 48000,
            RecommendedBitDepth = 24,
            
            Recommendations = new() 
            { 
                "💡 通用游戏优化：禁用增强效果，启用独占模式"
            }
        });

        // ========== 安全模式 ==========
        _rules.Add(new AudioOptimizationRule
        {
            Id = "safe_mode",
            Description = "安全模式",
            Priority = 0,
            
            DisableEnhancements = false,
            EnableExclusiveMode = false,
            DisableSpatialSound = false,
            OptimizeBuffer = false,
            DisableServiceRestart = false,
            OptimizeSampleRate = false,
            DisableCommDucking = true,
            
            Warnings = new() { "⚠️ 使用保守的安全设置" }
        });
    }

    /// <summary>
    /// 检测音频设备类型
    /// </summary>
    public static AudioDeviceType DetectAudioDeviceType(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return AudioDeviceType.Unknown;

        var name = deviceName.ToUpperInvariant();

        // USB DAC 关键词
        if (name.Contains("USB") || name.Contains("DAC") || name.Contains("FIIO") || 
            name.Contains("TOPPING") || name.Contains("SMSL") || name.Contains("SCHIIT"))
            return AudioDeviceType.UsbDac;

        // 专业声卡关键词
        if (name.Contains("FOCUSRITE") || name.Contains("SCARLETT") || name.Contains("STEINBERG") ||
            name.Contains("RME") || name.Contains("MOTU") || name.Contains("UNIVERSAL AUDIO") ||
            name.Contains("PRESONUS") || name.Contains("BEHRINGER"))
            return AudioDeviceType.Professional;

        // 蓝牙关键词
        if (name.Contains("BLUETOOTH") || name.Contains("AIRPODS") || name.Contains("GALAXY BUDS") ||
            name.Contains("WH-1000") || name.Contains("WF-1000") || name.Contains("JABRA"))
            return AudioDeviceType.Bluetooth;

        // HDMI 关键词
        if (name.Contains("HDMI") || name.Contains("DISPLAYPORT") || name.Contains("DP"))
            return AudioDeviceType.Hdmi;

        // 板载声卡关键词
        if (name.Contains("REALTEK") || name.Contains("HIGH DEFINITION AUDIO") || 
            name.Contains("INTEL") || name.Contains("AMD"))
            return AudioDeviceType.Onboard;

        return AudioDeviceType.Unknown;
    }

    /// <summary>
    /// 根据设备和场景匹配最佳规则
    /// </summary>
    public static AudioOptimizationRule? MatchRule(AudioDeviceType deviceType, AudioScenario scenario)
    {
        var matchedRules = _rules
            .Where(r => !r.RequireDeviceType.HasValue || r.RequireDeviceType.Value == deviceType)
            .Where(r => !r.RequireScenario.HasValue || r.RequireScenario.Value == scenario)
            .OrderByDescending(r => r.Priority)
            .ToList();

        var matched = matchedRules.FirstOrDefault();
        if (matched != null)
        {
            Debug.WriteLine($"[AudioKnowledgeBase] Matched rule: {matched.Id} ({matched.Description}) for {deviceType} {scenario}");
        }
        
        return matched;
    }

    /// <summary>
    /// 计算最优音频设置（默认游戏场景）
    /// </summary>
    public static OptimalAudioSettings CalculateOptimalSettings(string? audioDeviceName = null, AudioScenario scenario = AudioScenario.Gaming)
    {
        var deviceType = DetectAudioDeviceType(audioDeviceName ?? "");
        var rule = MatchRule(deviceType, scenario);
        var settings = new OptimalAudioSettings();

        if (rule == null)
        {
            settings.RuleId = "fallback";
            settings.RuleDescription = "未匹配到规则，使用默认设置";
            settings.Warnings.Add("未能识别音频设备，使用保守的默认设置");
            return settings;
        }

        settings.RuleId = rule.Id;
        settings.RuleDescription = rule.Description;

        // 应用规则设置
        settings.DisableEnhancements = rule.DisableEnhancements;
        settings.EnableExclusiveMode = rule.EnableExclusiveMode;
        settings.DisableSpatialSound = rule.DisableSpatialSound;
        settings.OptimizeBuffer = rule.OptimizeBuffer;
        settings.DisableServiceRestart = rule.DisableServiceRestart;
        settings.OptimizeSampleRate = rule.OptimizeSampleRate;
        settings.DisableCommDucking = rule.DisableCommDucking;
        settings.RecommendedSampleRate = rule.RecommendedSampleRate;
        settings.RecommendedBitDepth = rule.RecommendedBitDepth;

        // 生成启用的操作列表
        if (settings.DisableEnhancements) settings.EnabledOperations.Add("audio.disable_enhancements");
        if (settings.EnableExclusiveMode) settings.EnabledOperations.Add("audio.exclusive_mode_priority");
        if (settings.DisableSpatialSound) settings.EnabledOperations.Add("audio.disable_spatial_sound");
        if (settings.OptimizeBuffer) settings.EnabledOperations.Add("audio.optimize_buffer");
        if (settings.DisableServiceRestart) settings.EnabledOperations.Add("audio.disable_service_restart");
        if (settings.OptimizeSampleRate) settings.EnabledOperations.Add("audio.optimize_sample_rate");
        if (settings.DisableCommDucking) settings.EnabledOperations.Add("audio.disable_comm_ducking");

        // 计算预计延迟降低
        settings.EstimatedLatencyReduction = CalculateLatencyReduction(settings, deviceType);

        // 添加规则的警告和建议
        settings.Warnings.AddRange(rule.Warnings);
        settings.Recommendations.AddRange(rule.Recommendations);

        Debug.WriteLine($"[AudioKnowledgeBase] Calculated settings: Enhancements={!settings.DisableEnhancements}, " +
            $"Exclusive={settings.EnableExclusiveMode}, LatencyReduction={settings.EstimatedLatencyReduction}ms");

        return settings;
    }

    private static int CalculateLatencyReduction(OptimalAudioSettings settings, AudioDeviceType deviceType)
    {
        var reduction = 0;

        if (settings.DisableEnhancements) reduction += 5;
        if (settings.EnableExclusiveMode) reduction += 3;
        if (settings.DisableSpatialSound) reduction += 2;
        if (settings.OptimizeBuffer) reduction += 2;

        // 蓝牙设备优化效果有限
        if (deviceType == AudioDeviceType.Bluetooth)
            reduction = (int)(reduction * 0.3);

        return reduction;
    }

    /// <summary>
    /// 获取所有规则（用于调试）
    /// </summary>
    public static IReadOnlyList<AudioOptimizationRule> GetAllRules() => _rules.AsReadOnly();
}
