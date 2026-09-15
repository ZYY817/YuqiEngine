// File: GameOptimizer.Shell/Constants/IconGlyphs.cs
namespace GameOptimizer.Shell.Constants;

/// <summary>
/// Segoe MDL2 Assets 字体图标常量
/// 替代emoji图标，确保跨系统兼容性
/// </summary>
public static class IconGlyphs
{
    // 通用操作
    public const string Settings = "\uE713";      // ⚙️ 设置齿轮
    public const string Refresh = "\uE72C";       // 🔄 刷新
    public const string Save = "\uE74E";          // 💾 保存
    public const string Delete = "\uE74D";        // 删除
    public const string Add = "\uE710";           // 添加
    public const string Edit = "\uE70F";          // 编辑
    public const string Search = "\uE721";        // 🔍 搜索
    public const string Filter = "\uE71C";        // 筛选
    public const string Copy = "\uE8C8";          // 复制
    public const string Paste = "\uE77F";         // 粘贴
    public const string Undo = "\uE7A7";          // 撤销
    public const string Close = "\uE711";         // ✕ 关闭
    public const string Check = "\uE73E";         // ✓ 勾选
    public const string Warning = "\uE7BA";       // ⚠ 警告
    public const string Error = "\uE783";         // ❌ 错误
    public const string Info = "\uE946";          // ℹ️ 信息
    public const string Question = "\uE897";      // ❓ 问号
    public const string Help = "\uE897";          // 帮助
    
    // 硬件相关
    public const string Cpu = "\uE950";           // 🔧 处理器
    public const string Memory = "\uE964";        // 💾 内存
    public const string Disk = "\uEDA2";          // 💿 磁盘
    public const string Gpu = "\uE7F4";           // 🎮 显卡/游戏
    public const string Monitor = "\uE7F4";       // 🖥️ 显示器
    public const string Desktop = "\uE977";       // 桌面
    public const string Laptop = "\uE70A";        // 笔记本
    public const string Device = "\uE772";        // 设备
    public const string Peripheral = "\uE836";    // 🖱️ 外设
    public const string Bluetooth = "\uE702";     // 蓝牙
    public const string Power = "\uE945";         // ⚡ 电源
    public const string PowerPlug = "\uE83E";     // 🔌 电源插头
    public const string Battery = "\uE83F";       // 🔋 电池
    public const string Temperature = "\uE9CA";   // 温度
    
    // 电源计划类型图标
    public const string PowerPlanHighPerformance = "\uE945";  // ⚡ 高性能
    public const string PowerPlanBalanced = "\uE9D9";         // ⚖️ 平衡
    public const string PowerPlanPowerSaver = "\uE8A2";       // 🌿 节能
    public const string PowerPlanCustom = "\uE713";           // ⚙️ 自定义
    
    // 网络相关
    public const string Network = "\uE968";       // 🌐 网络
    public const string Wifi = "\uE701";          // WiFi
    public const string Ethernet = "\uE839";      // 以太网
    public const string Globe = "\uE774";         // 地球
    public const string Signal = "\uEC3B";        // 📶 信号
    public const string Dns = "\uE968";           // DNS
    public const string Firewall = "\uE83D";      // 🧱 防火墙
    public const string Download = "\uE896";      // 📥 下载
    public const string Upload = "\uE898";        // 上传
    public const string Sync = "\uE895";          // 同步
    
    // 安全相关
    public const string Shield = "\uE83D";        // 🛡️ 盾牌
    public const string ShieldCheck = "\uEA1A";   // 盾牌勾选
    public const string Lock = "\uE72E";          // 🔒 锁
    public const string Unlock = "\uE785";        // 解锁
    public const string Key = "\uE8D7";           // 🔐 钥匙
    public const string Privacy = "\uE727";       // 隐私
    public const string Admin = "\uE7EF";         // 管理员
    
    // 音频相关
    public const string Audio = "\uE767";         // 🔉 音频
    public const string AudioHigh = "\uE995";     // 🔊 高音量
    public const string AudioLow = "\uE993";      // 🔈 低音量
    public const string AudioMute = "\uE74F";     // 静音
    public const string Headphone = "\uE7F6";     // 🎧 耳机
    public const string Microphone = "\uE720";    // 🎤 麦克风
    public const string Music = "\uE8D6";         // 🎵 音乐
    
    // 系统相关
    public const string System = "\uE770";        // 系统
    public const string Windows = "\uE782";       // Windows
    public const string Task = "\uE9D5";          // 📋 任务
    public const string Service = "\uE912";       // 服务
    public const string Process = "\uE9D9";       // 进程
    public const string Startup = "\uE7E8";       // 🚀 启动
    public const string Shutdown = "\uE7E8";      // 关机
    public const string Restart = "\uE777";       // 重启
    public const string Update = "\uE777";        // 更新
    public const string Repair = "\uE90F";        // 修复
    public const string Cleanup = "\uE74D";       // 清理
    public const string Optimize = "\uE9D9";      // ✨ 优化
    public const string Boost = "\uE945";         // 加速
    
    // 游戏相关
    public const string Game = "\uE7FC";          // 🎮 游戏手柄
    public const string Esports = "\uE7FC";       // 电竞
    public const string Target = "\uE7B3";        // 🎯 目标
    public const string Trophy = "\uE7C1";        // 奖杯
    public const string Star = "\uE734";          // ⭐ 星星
    
    // 文件相关
    public const string Folder = "\uE8B7";        // 📁 文件夹
    public const string File = "\uE8A5";          // 📄 文件
    public const string Document = "\uE8A5";      // 文档
    public const string Package = "\uE7B8";       // 📦 包
    public const string Archive = "\uF012";       // 归档
    public const string Registry = "\uE8A5";      // 📝 注册表
    public const string Config = "\uE713";        // 配置
    public const string Log = "\uE9F9";           // 日志
    
    // 备份还原
    public const string Backup = "\uE81E";        // 备份
    public const string Restore = "\uE777";       // 还原
    public const string History = "\uE81C";       // 历史
    public const string Snapshot = "\uE722";      // 快照
    public const string RestorePoint = "\uE7C1";  // 还原点
    
    // 状态指示
    public const string Success = "\uE73E";       // ✅ 成功
    public const string Fail = "\uE711";          // ❌ 失败
    public const string Pending = "\uE916";       // ⏳ 等待
    public const string Running = "\uE768";       // ▶ 运行中
    public const string Stopped = "\uE71A";       // ⏹ 停止
    public const string Paused = "\uE769";        // ⏸ 暂停
    public const string Disabled = "\uE894";      // 🚫 禁用
    public const string Enabled = "\uE73E";       // 启用
    
    // 风险等级
    public const string RiskLow = "\uE73E";       // 低风险
    public const string RiskMedium = "\uE7BA";    // 中风险
    public const string RiskHigh = "\uE783";      // 高风险
    
    // 方向箭头
    public const string ArrowUp = "\uE74A";       // ↑
    public const string ArrowDown = "\uE74B";     // ↓
    public const string ArrowLeft = "\uE72B";     // ←
    public const string ArrowRight = "\uE72A";    // →
    public const string ChevronUp = "\uE70E";     // ∧
    public const string ChevronDown = "\uE70D";   // ∨
    public const string ChevronLeft = "\uE76B";   // <
    public const string ChevronRight = "\uE76C";  // >
    public const string Expand = "\uE70D";        // 展开
    public const string Collapse = "\uE70E";      // 折叠
    
    // 其他
    public const string Lightbulb = "\uEA80";     // 💡 灯泡
    public const string Chart = "\uE9D2";         // 📊 图表
    public const string Statistics = "\uE9D2";    // 统计
    public const string Clock = "\uE823";         // ⏰ 时钟
    public const string Calendar = "\uE787";      // 📅 日历
    public const string Phone = "\uE717";         // 📞 电话
    public const string Mail = "\uE715";          // 📧 邮件
    public const string Link = "\uE71B";          // 🔗 链接
    public const string Pin = "\uE718";           // 📌 图钉
    public const string Flag = "\uE7C1";          // 🚩 旗帜
    public const string Heart = "\uEB51";         // ❤ 心
    public const string Home = "\uE80F";          // 🏠 主页
    public const string User = "\uE77B";          // 👤 用户
    public const string People = "\uE716";        // 👥 多人
    public const string Comment = "\uE8BD";       // 💬 评论
    public const string Notification = "\uE7E7";  // 🔔 通知
    public const string More = "\uE712";          // ⋯ 更多
    public const string Menu = "\uE700";          // ☰ 菜单
    
    /// <summary>
    /// 根据语义关键词获取对应图标
    /// </summary>
    public static string GetByKeyword(string keyword)
    {
        var lower = keyword.ToLowerInvariant();
        
        // 音频相关
        if (lower.Contains("音频增强") || lower.Contains("audio enhancement")) return AudioHigh;
        if (lower.Contains("独占模式") || lower.Contains("exclusive")) return Headphone;
        if (lower.Contains("空间音效") || lower.Contains("spatial")) return AudioLow;
        if (lower.Contains("缓冲") || lower.Contains("buffer")) return Signal;
        if (lower.Contains("音频服务") || lower.Contains("audio service")) return Refresh;
        if (lower.Contains("采样率") || lower.Contains("sample")) return Music;
        if (lower.Contains("通信") || lower.Contains("ducking") || lower.Contains("communication")) return Phone;
        if (lower.Contains("音频") || lower.Contains("audio") || lower.Contains("声音")) return Audio;
        
        // 网络相关
        if (lower.Contains("网络") || lower.Contains("network")) return Network;
        if (lower.Contains("延迟") || lower.Contains("latency")) return Power;
        if (lower.Contains("dns")) return Link;
        if (lower.Contains("tcp") || lower.Contains("udp")) return Signal;
        if (lower.Contains("防火墙") || lower.Contains("firewall")) return Firewall;
        
        // 安全/隐私
        if (lower.Contains("安全") || lower.Contains("security")) return Shield;
        if (lower.Contains("隐私") || lower.Contains("privacy")) return Lock;
        if (lower.Contains("遥测") || lower.Contains("telemetry")) return Chart;
        
        // 硬件相关
        if (lower.Contains("cpu") || lower.Contains("处理器")) return Cpu;
        if (lower.Contains("gpu") || lower.Contains("显卡") || lower.Contains("图形")) return Gpu;
        if (lower.Contains("内存") || lower.Contains("memory")) return Memory;
        if (lower.Contains("电源") || lower.Contains("power")) return Power;
        if (lower.Contains("磁盘") || lower.Contains("disk") || lower.Contains("存储")) return Disk;
        
        // 系统相关
        if (lower.Contains("服务") || lower.Contains("service")) return Service;
        if (lower.Contains("任务") || lower.Contains("task")) return Task;
        if (lower.Contains("启动") || lower.Contains("startup") || lower.Contains("boot")) return Startup;
        if (lower.Contains("禁用") || lower.Contains("disable")) return Disabled;
        if (lower.Contains("优化") || lower.Contains("optimize")) return Optimize;
        
        return Settings;
    }
    
    /// <summary>
    /// 根据分类获取图标
    /// </summary>
    public static string GetByCategory(string category) => category switch
    {
        "系统安全" => Shield,
        "游戏优化" => Game,
        "网络优化" => Network,
        "电源管理" => Power,
        "内存优化" => Memory,
        "CPU优化" => Cpu,
        _ => Settings
    };
    
    /// <summary>
    /// 根据电源计划名称获取图标
    /// </summary>
    public static string GetPowerPlanIcon(string planName)
    {
        var lower = planName.ToLowerInvariant();
        
        if (lower.Contains("high performance") || lower.Contains("高性能") || lower.Contains("ultimate"))
            return PowerPlanHighPerformance;
        
        if (lower.Contains("power saver") || lower.Contains("节能") || lower.Contains("省电"))
            return PowerPlanPowerSaver;
        
        if (lower.Contains("balanced") || lower.Contains("平衡"))
            return PowerPlanBalanced;
        
        return PowerPlanCustom;
    }
    
    /// <summary>
    /// 根据风险等级获取图标
    /// </summary>
    public static string GetByRiskLevel(string risk) => risk switch
    {
        "Extreme" or "高风险" => Shield,
        "Balanced" or "中等" => Settings,
        _ => Success
    };
}
