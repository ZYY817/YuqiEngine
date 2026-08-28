using System.Collections.Generic;

namespace GameOptimizer.Shell.Services;

public static class GameOptimizationPresets
{
    public static IReadOnlyList<string> GetRecommendedOperations(GameType gameType)
    {
        return gameType switch
        {
            GameType.Fps => FpsPreset,
            GameType.Moba => MobaPreset,
            GameType.OpenWorld => OpenWorldPreset,
            GameType.Racing => RacingPreset,
            GameType.Strategy => StrategyPreset,
            _ => GeneralPreset
        };
    }

    // FPS/射击游戏：最低延迟优先
    private static readonly string[] FpsPreset = new[]
    {
        "network.disable_nagle",           // 禁用Nagle算法，减少网络延迟
        "network.disable_auto_tuning",     // 禁用TCP自动调优
        "network.enable_ctcp",             // 启用复合TCP
        "network.enable_rss",              // 多核网络处理
        "cpu.high_precision_timer",        // 高精度计时器
        "gaming.disable_game_dvr",         // 禁用后台录制
        "gaming.game_mode",                // Windows游戏模式
        "services.enable_high_performance_mode", // 按平台能力匹配 Windows 性能模式
        "gpu.enable_hags"                  // 硬件加速GPU调度
    };

    // MOBA/竞技游戏：稳定性优先
    private static readonly string[] MobaPreset = new[]
    {
        "network.disable_nagle",           // 网络延迟优化
        "network.enable_ctcp",             // 网络稳定性
        "gaming.game_mode",                // Windows游戏模式
        "gaming.disable_game_dvr",         // 禁用后台录制
        "services.enable_high_performance_mode", // 按平台能力匹配 Windows 性能模式
        "system.disable_superfetch"        // 禁用预读取，减少后台干扰
    };

    // 开放世界/RPG：加载和GPU性能优先
    private static readonly string[] OpenWorldPreset = new[]
    {
        "services.enable_high_performance_mode", // 按平台能力匹配 Windows 性能模式
        "gpu.enable_hags",                 // 硬件加速GPU调度
        "gpu.force_high_performance_gpu",  // 强制独显
        "gpu.set_sfio_priority",           // 游戏IO优先
        "gaming.game_mode",                // Windows游戏模式
        "gaming.disable_game_dvr",         // 禁用后台录制
        "memory.large_system_cache",       // 大系统缓存
        "system.disable_superfetch"        // 禁用预读取
    };

    // 竞速游戏：帧率稳定和输入精度
    private static readonly string[] RacingPreset = new[]
    {
        "cpu.high_precision_timer",        // 高精度计时器
        "services.enable_high_performance_mode", // 按平台能力匹配 Windows 性能模式
        "gpu.enable_hags",                 // 硬件加速GPU调度
        "gaming.game_mode",                // Windows游戏模式
        "gaming.disable_game_dvr",         // 禁用后台录制
        "network.disable_nagle"            // 网络优化（在线竞速）
    };

    // 策略/模拟游戏：CPU和内存优先
    private static readonly string[] StrategyPreset = new[]
    {
        "services.enable_high_performance_mode", // 按平台能力匹配 Windows 性能模式
        "cpu.high_precision_timer",        // 高精度计时器
        "memory.large_system_cache",       // 大系统缓存
        "system.disable_superfetch",       // 禁用预读取
        "gaming.game_mode",                // Windows游戏模式
        "gaming.disable_game_dvr"          // 禁用后台录制
    };

    // 通用预设
    private static readonly string[] GeneralPreset = new[]
    {
        "gaming.game_mode",                // Windows游戏模式
        "gaming.disable_game_dvr",         // 禁用后台录制
        "services.enable_high_performance_mode"  // 按平台能力匹配 Windows 性能模式
    };

    public static string GetPresetDescription(GameType gameType, bool isEnglish = false)
    {
        return gameType switch
        {
            GameType.Fps => isEnglish
                ? "Network latency optimization + High precision timer + Input optimization"
                : "网络延迟优化 + 高精度计时器 + 输入优化",
            GameType.Moba => isEnglish
                ? "Network stability + Game mode + Background optimization"
                : "网络稳定性 + 游戏模式 + 后台优化",
            GameType.OpenWorld => isEnglish
                ? "GPU performance + Loading optimization + Memory management"
                : "GPU性能 + 加载优化 + 内存管理",
            GameType.Racing => isEnglish
                ? "Frame rate stability + Input precision + High precision timer"
                : "帧率稳定 + 输入精度 + 高精度计时器",
            GameType.Strategy => isEnglish
                ? "CPU optimization + Memory management + Background control"
                : "CPU优化 + 内存管理 + 后台控制",
            _ => isEnglish
                ? "Basic game mode + Power optimization"
                : "基础游戏模式 + 电源优化"
        };
    }
}
