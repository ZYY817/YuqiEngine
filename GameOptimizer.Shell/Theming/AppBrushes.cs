using System.Windows.Media;

namespace GameOptimizer.Shell.Theming;

public static class AppBrushes
{
    // 主色调
    public static SolidColorBrush Primary { get; } = new(Color.FromRgb(99, 102, 241));
    public static SolidColorBrush PrimaryHover { get; } = new(Color.FromRgb(129, 140, 248));
    
    // 状态颜色
    public static SolidColorBrush Success { get; } = new(Color.FromRgb(34, 197, 94));
    public static SolidColorBrush Warning { get; } = new(Color.FromRgb(234, 179, 8));
    public static SolidColorBrush Error { get; } = new(Color.FromRgb(239, 68, 68));
    public static SolidColorBrush Info { get; } = new(Color.FromRgb(59, 130, 246));
    
    // 文本颜色
    public static SolidColorBrush TextPrimary { get; } = new(Color.FromRgb(255, 255, 255));
    public static SolidColorBrush TextSecondary { get; } = new(Color.FromRgb(156, 163, 175));
    public static SolidColorBrush TextMuted { get; } = new(Color.FromRgb(107, 114, 128));
    
    // 卡片和边框 - Tactical Command Grid 设计系统
    public static SolidColorBrush CardBackground { get; } = new(Color.FromRgb(26, 26, 30));       // #1A1A1E
    public static SolidColorBrush CardBackgroundHover { get; } = new(Color.FromRgb(37, 37, 41));  // #252529
    public static SolidColorBrush CardBackgroundActive { get; } = new(Color.FromRgb(37, 37, 41)); // #252529
    public static SolidColorBrush CardBackgroundDisabled { get; } = new(Color.FromRgb(17, 17, 20));
    public static SolidColorBrush Border { get; } = new(Color.FromArgb(20, 255, 255, 255));       // 8% white
    public static SolidColorBrush BorderHover { get; } = new(Color.FromArgb(40, 255, 255, 255));  // 15% white
    public static SolidColorBrush BorderSelected { get; } = new(Color.FromArgb(60, 255, 255, 255)); // 24% white
    public static SolidColorBrush BorderDisabled { get; } = new(Color.FromArgb(10, 255, 255, 255));
    
    // Ghost Tag 样式
    public static SolidColorBrush GhostTagBackground { get; } = new(Color.FromRgb(26, 26, 30));   // #1A1A1E
    public static SolidColorBrush GhostTagSafe { get; } = new(Color.FromRgb(52, 199, 89));        // #34C759
    public static SolidColorBrush GhostTagBalanced { get; } = new(Color.FromRgb(255, 149, 0));    // #FF9500
    public static SolidColorBrush GhostTagExtreme { get; } = new(Color.FromRgb(255, 69, 58));     // #FF453A
    
    // 勾选图标
    public static SolidColorBrush CheckmarkGreen { get; } = new(Color.FromRgb(52, 199, 89));      // #34C759
    public static SolidColorBrush CheckmarkBlue { get; } = new(Color.FromRgb(59, 130, 246));      // #3B82F6
    
    // 风险等级
    public static SolidColorBrush RiskSafe { get; } = new(Color.FromRgb(34, 197, 94));
    public static SolidColorBrush RiskBalanced { get; } = new(Color.FromRgb(234, 179, 8));
    public static SolidColorBrush RiskExtreme { get; } = new(Color.FromRgb(239, 68, 68));
    
    // 电源状态
    public static SolidColorBrush PowerAC { get; } = new(Color.FromRgb(34, 197, 94));
    public static SolidColorBrush PowerBattery { get; } = new(Color.FromRgb(234, 179, 8));
    
    // 硬件厂商
    public static SolidColorBrush Intel { get; } = new(Color.FromRgb(0, 125, 197));
    public static SolidColorBrush AMD { get; } = new(Color.FromRgb(237, 28, 36));
    public static SolidColorBrush NVIDIA { get; } = new(Color.FromRgb(118, 185, 0));
    
    static AppBrushes()
    {
        // 冻结所有画刷以提升性能
        Primary.Freeze();
        PrimaryHover.Freeze();
        Success.Freeze();
        Warning.Freeze();
        Error.Freeze();
        Info.Freeze();
        TextPrimary.Freeze();
        TextSecondary.Freeze();
        TextMuted.Freeze();
        CardBackground.Freeze();
        CardBackgroundHover.Freeze();
        CardBackgroundActive.Freeze();
        CardBackgroundDisabled.Freeze();
        Border.Freeze();
        BorderHover.Freeze();
        BorderSelected.Freeze();
        BorderDisabled.Freeze();
        GhostTagBackground.Freeze();
        GhostTagSafe.Freeze();
        GhostTagBalanced.Freeze();
        GhostTagExtreme.Freeze();
        CheckmarkGreen.Freeze();
        CheckmarkBlue.Freeze();
        RiskSafe.Freeze();
        RiskBalanced.Freeze();
        RiskExtreme.Freeze();
        PowerAC.Freeze();
        PowerBattery.Freeze();
        Intel.Freeze();
        AMD.Freeze();
        NVIDIA.Freeze();
    }
}
