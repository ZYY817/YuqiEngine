using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GameOptimizer.Shell.Services;

public enum GameType
{
    General,    // 通用
    Fps,        // FPS/射击
    Moba,       // MOBA/竞技
    OpenWorld,  // 开放世界/RPG
    Racing,     // 竞速
    Strategy    // 策略/模拟
}

public static class GameTypeDetector
{
    private static readonly Dictionary<GameType, string[]> GameKeywords = new()
    {
        [GameType.Fps] = new[]
        {
            "cs2", "csgo", "counter-strike", "valorant", "apex", "apexlegends",
            "overwatch", "battlefield", "cod", "callofduty", "call of duty",
            "rainbow", "r6", "siege", "pubg", "fortnite", "destiny", "halo",
            "doom", "quake", "titanfall", "warzone", "tarkov", "hunt", "insurgency",
            "squad", "arma", "dayz", "rust", "crossfire", "穿越火线", "cf",
            "csol", "反恐精英", "使命召唤", "战地", "守望先锋", "无畏契约"
        },
        [GameType.Moba] = new[]
        {
            "league", "lol", "leagueoflegends", "dota", "dota2",
            "heroes", "hots", "smite", "arena of valor", "aov",
            "王者荣耀", "英雄联盟", "刀塔", "风暴英雄", "原神", "genshin",
            "honkai", "崩坏", "永劫无间", "naraka"
        },
        [GameType.OpenWorld] = new[]
        {
            "gta", "grandtheftauto", "cyberpunk", "witcher", "巫师",
            "eldenring", "艾尔登法环", "darksouls", "黑暗之魂", "sekiro", "只狼",
            "assassin", "刺客信条", "farcry", "孤岛惊魂", "horizon", "地平线",
            "reddeadredemption", "rdr", "荒野大镖客", "skyrim", "上古卷轴",
            "fallout", "辐射", "starfield", "星空", "zelda", "塞尔达",
            "monster hunter", "怪物猎人", "mhw", "mhr", "diablo", "暗黑破坏神",
            "pathofexile", "poe", "流放之路", "lost ark", "失落的方舟",
            "黑神话", "wukong", "悟空"
        },
        [GameType.Racing] = new[]
        {
            "forza", "极限竞速", "nfs", "needforspeed", "极品飞车",
            "f1", "assetto", "iracing", "dirt", "尘埃", "rally",
            "gran turismo", "gt7", "project cars", "crew", "飙酷车神",
            "wreckfest", "beamng", "carx"
        },
        [GameType.Strategy] = new[]
        {
            "civilization", "civ", "文明", "cities", "城市天际线",
            "totalwar", "全面战争", "crusader", "十字军之王", "europa",
            "stellaris", "群星", "hearts of iron", "钢铁雄心", "hoi",
            "starcraft", "星际争霸", "warcraft", "魔兽争霸", "age of empires",
            "帝国时代", "aoe", "anno", "纪元", "factorio", "异星工厂",
            "rimworld", "环世界", "dwarf fortress", "矮人要塞",
            "football manager", "fm", "足球经理", "sims", "模拟人生"
        }
    };

    public static GameType DetectType(string gameName, string executablePath)
    {
        var searchText = $"{gameName} {Path.GetFileNameWithoutExtension(executablePath)}"
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "");

        foreach (var (type, keywords) in GameKeywords)
        {
            foreach (var keyword in keywords)
            {
                var normalizedKeyword = keyword.ToLowerInvariant().Replace(" ", "").Replace("-", "");
                if (searchText.Contains(normalizedKeyword))
                    return type;
            }
        }

        return GameType.General;
    }

    public static string GetTypeDisplayName(GameType type, bool isEnglish = false)
    {
        return type switch
        {
            GameType.Fps => isEnglish ? "FPS / Shooter" : "FPS / 射击",
            GameType.Moba => isEnglish ? "MOBA / Competitive" : "MOBA / 竞技",
            GameType.OpenWorld => isEnglish ? "Open World / RPG" : "开放世界 / RPG",
            GameType.Racing => isEnglish ? "Racing" : "竞速",
            GameType.Strategy => isEnglish ? "Strategy / Simulation" : "策略 / 模拟",
            _ => isEnglish ? "General" : "通用"
        };
    }

    public static string GetTypeDescription(GameType type, bool isEnglish = false)
    {
        return type switch
        {
            GameType.Fps => isEnglish 
                ? "Optimized for lowest input latency and network response" 
                : "针对最低输入延迟和网络响应优化",
            GameType.Moba => isEnglish 
                ? "Balanced optimization for stable gameplay" 
                : "平衡优化，确保游戏稳定流畅",
            GameType.OpenWorld => isEnglish 
                ? "Optimized for large scene loading and GPU performance" 
                : "针对大场景加载和GPU性能优化",
            GameType.Racing => isEnglish 
                ? "Optimized for stable frame rate and precise input" 
                : "针对稳定帧率和精确输入优化",
            GameType.Strategy => isEnglish 
                ? "Optimized for CPU-intensive calculations" 
                : "针对CPU密集型计算优化",
            _ => isEnglish 
                ? "General optimization for most games" 
                : "适用于大多数游戏的通用优化"
        };
    }
}
