// File: GameOptimizer.Shell/Constants/ThemeColors.cs
using System.Windows.Media;

namespace GameOptimizer.Shell.Constants;

// 集中管理所有语义颜色常量，避免硬编码散落各处
// Why: 统一设计令牌(Design Tokens)，改一处全局生效
public static class ThemeColors
{
    // ── 状态语义色 ──
    public static readonly Color Success = Color.FromRgb(0x22, 0xC5, 0x5E);
    public static readonly Color SuccessLight = Color.FromRgb(0x4A, 0xDE, 0x80);
    public static readonly Color SuccessDark = Color.FromRgb(0x16, 0xA3, 0x4A);
    public static readonly Color SuccessBg = Color.FromRgb(0x05, 0x20, 0x12);

    public static readonly Color Warning = Color.FromRgb(0xF5, 0x9E, 0x0B);
    public static readonly Color WarningLight = Color.FromRgb(0xFB, 0xBF, 0x24);
    public static readonly Color WarningBg = Color.FromRgb(0x2D, 0x1A, 0x05);

    public static readonly Color Danger = Color.FromRgb(0xEF, 0x44, 0x44);
    public static readonly Color DangerMuted = Color.FromRgb(0xDC, 0x6B, 0x6B);
    public static readonly Color DangerBg = Color.FromRgb(0x2D, 0x0A, 0x0A);
    public static readonly Color DangerDark = Color.FromRgb(0x45, 0x0A, 0x0A);

    public static readonly Color Info = Color.FromRgb(0x3B, 0x82, 0xF6);
    public static readonly Color InfoLight = Color.FromRgb(0x60, 0xA5, 0xFA);
    public static readonly Color Cyan = Color.FromRgb(0x22, 0xD3, 0xEE);
    public static readonly Color CyanLight = Color.FromRgb(0x06, 0xB6, 0xD4);

    // ── 风险等级色 ──
    public static readonly Color RiskSafeFg = Color.FromRgb(0x5A, 0xC4, 0x7D);
    public static readonly Color RiskSafeBg = Color.FromRgb(0x05, 0x20, 0x12);
    public static readonly Color RiskSafeBorder = Color.FromRgb(0x14, 0x50, 0x2A);

    public static readonly Color RiskBalancedFg = Color.FromRgb(0xD9, 0xA5, 0x30);
    public static readonly Color RiskBalancedBg = Color.FromRgb(0x2D, 0x1A, 0x05);
    public static readonly Color RiskBalancedBorder = Color.FromRgb(0x5C, 0x35, 0x10);
    public static readonly Color RiskBalancedDark = Color.FromRgb(0x45, 0x1A, 0x03);

    public static readonly Color RiskExtremeFg = Color.FromRgb(0xDC, 0x6B, 0x6B);
    public static readonly Color RiskExtremeBg = Color.FromRgb(0x2D, 0x0A, 0x0A);
    public static readonly Color RiskExtremeBorder = Color.FromRgb(0x5C, 0x1A, 0x1A);

    // ── Obsidian 主题基础色 ──
    public static readonly Color Accent = Color.FromRgb(0x63, 0x66, 0xF1);
    public static readonly Color TextPrimary = Color.FromRgb(0xF1, 0xF5, 0xF9);
    public static readonly Color TextSecondary = Color.FromRgb(0x94, 0xA3, 0xB8);
    public static readonly Color TextDisabled = Color.FromRgb(0x64, 0x74, 0x8B);
    public static readonly Color TextMuted = Color.FromRgb(0x71, 0x71, 0x7A);
    public static readonly Color SurfaceHover = Color.FromRgb(0x25, 0x25, 0x28);
    public static readonly SolidColorBrush SurfaceHoverBrush = Freeze(new SolidColorBrush(SurfaceHover));

    // ── 预冻结 Brush（高频使用，避免重复创建） ──
    public static readonly SolidColorBrush SuccessBrush = Freeze(new SolidColorBrush(Success));
    public static readonly SolidColorBrush SuccessLightBrush = Freeze(new SolidColorBrush(SuccessLight));
    public static readonly SolidColorBrush WarningBrush = Freeze(new SolidColorBrush(Warning));
    public static readonly SolidColorBrush WarningLightBrush = Freeze(new SolidColorBrush(WarningLight));
    public static readonly SolidColorBrush DangerBrush = Freeze(new SolidColorBrush(Danger));
    public static readonly SolidColorBrush DangerMutedBrush = Freeze(new SolidColorBrush(DangerMuted));
    public static readonly SolidColorBrush InfoBrush = Freeze(new SolidColorBrush(Info));
    public static readonly SolidColorBrush InfoLightBrush = Freeze(new SolidColorBrush(InfoLight));
    public static readonly SolidColorBrush CyanBrush = Freeze(new SolidColorBrush(Cyan));
    public static readonly SolidColorBrush AccentBrush = Freeze(new SolidColorBrush(Accent));
    public static readonly SolidColorBrush TextPrimaryBrush = Freeze(new SolidColorBrush(TextPrimary));
    public static readonly SolidColorBrush TextSecondaryBrush = Freeze(new SolidColorBrush(TextSecondary));
    public static readonly SolidColorBrush TextDisabledBrush = Freeze(new SolidColorBrush(TextDisabled));
    public static readonly SolidColorBrush TextMutedBrush = Freeze(new SolidColorBrush(TextMuted));
    public static readonly SolidColorBrush SuccessDarkBrush = Freeze(new SolidColorBrush(SuccessDark));
    public static readonly SolidColorBrush CyanLightBrush = Freeze(new SolidColorBrush(CyanLight));

    // ── 风险等级预冻结 Brush（避免 GetRiskColors 每次 new） ──
    public static readonly SolidColorBrush RiskSafeFgBrush = Freeze(new SolidColorBrush(RiskSafeFg));
    public static readonly SolidColorBrush RiskSafeBgBrush = Freeze(new SolidColorBrush(RiskSafeBg));
    public static readonly SolidColorBrush RiskSafeBorderBrush = Freeze(new SolidColorBrush(RiskSafeBorder));
    public static readonly SolidColorBrush RiskBalancedFgBrush = Freeze(new SolidColorBrush(RiskBalancedFg));
    public static readonly SolidColorBrush RiskBalancedBgBrush = Freeze(new SolidColorBrush(RiskBalancedBg));
    public static readonly SolidColorBrush RiskBalancedBorderBrush = Freeze(new SolidColorBrush(RiskBalancedBorder));
    public static readonly SolidColorBrush RiskBalancedDarkBrush = Freeze(new SolidColorBrush(RiskBalancedDark));
    public static readonly SolidColorBrush RiskExtremeFgBrush = Freeze(new SolidColorBrush(RiskExtremeFg));
    public static readonly SolidColorBrush RiskExtremeBgBrush = Freeze(new SolidColorBrush(RiskExtremeBg));
    public static readonly SolidColorBrush RiskExtremeBorderBrush = Freeze(new SolidColorBrush(RiskExtremeBorder));
    public static readonly SolidColorBrush DangerDarkBrush = Freeze(new SolidColorBrush(DangerDark));

    // ── 额外语义色 ──
    public static readonly Color SuccessBgSafe = Color.FromRgb(0x05, 0x2E, 0x16); // 安全图标深绿底
    public static readonly SolidColorBrush SuccessBgSafeBrush = Freeze(new SolidColorBrush(SuccessBgSafe));
    public static readonly Color Zinc400 = Color.FromRgb(0xA1, 0xA1, 0xAA); // 日志/分隔线灰
    public static readonly SolidColorBrush Zinc400Brush = Freeze(new SolidColorBrush(Zinc400));
    public static readonly Color WarningDark = Color.FromRgb(0xD9, 0x77, 0x06); // 橙色深色
    public static readonly SolidColorBrush WarningDarkBrush = Freeze(new SolidColorBrush(WarningDark));
    public static readonly Color Purple = Color.FromRgb(0xA7, 0x8B, 0xFA); // 处理器分类
    public static readonly SolidColorBrush PurpleBrush = Freeze(new SolidColorBrush(Purple));
    public static readonly Color SkyBlue = Color.FromRgb(0x0E, 0xA5, 0xE9); // Intel 大小核分类
    public static readonly Color Emerald = Color.FromRgb(0x10, 0xB9, 0x81); // 安全标签前景
    public static readonly SolidColorBrush EmeraldBrush = Freeze(new SolidColorBrush(Emerald));
    public static readonly Color MutedPurple = Color.FromRgb(0x8B, 0x94, 0xC6); // 默认/未知状态灰紫
    public static readonly SolidColorBrush MutedPurpleBrush = Freeze(new SolidColorBrush(MutedPurple));
    public static readonly SolidColorBrush MutedPurpleAlpha10Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0x8B, 0x94, 0xC6)));
    public static readonly SolidColorBrush MutedPurpleAlpha20Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0x8B, 0x94, 0xC6)));

    // ── 带透明度的预冻结 Brush ──
    public static readonly SolidColorBrush SuccessAlpha20Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0x22, 0xC5, 0x5E)));
    public static readonly SolidColorBrush SuccessAlpha15Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x15, 0x22, 0xC5, 0x5E)));
    public static readonly SolidColorBrush WarningAlpha20Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0xF5, 0x9E, 0x0B)));
    public static readonly SolidColorBrush WarningAlpha15Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x15, 0xF5, 0x9E, 0x0B)));
    public static readonly SolidColorBrush WarningAlpha10Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0xF5, 0x9E, 0x0B)));
    public static readonly SolidColorBrush DangerAlpha20Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0xEF, 0x44, 0x44)));
    public static readonly SolidColorBrush DangerAlpha10Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0xEF, 0x44, 0x44)));
    public static readonly SolidColorBrush WhiteAlpha08Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)));
    public static readonly SolidColorBrush WhiteAlpha10Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
    public static readonly SolidColorBrush WhiteAlpha15Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF)));
    public static readonly SolidColorBrush WhiteAlpha25Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF)));

    // ── OptimizationListDialog 风险行 alpha Brush ──
    public static readonly SolidColorBrush EmeraldAlpha10Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0x10, 0xB9, 0x81)));
    public static readonly SolidColorBrush EmeraldAlpha20Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0x10, 0xB9, 0x81)));
    public static readonly SolidColorBrush EmeraldAlpha30Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0x10, 0xB9, 0x81)));
    public static readonly SolidColorBrush DangerAlpha30Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xEF, 0x44, 0x44)));
    public static readonly SolidColorBrush WarningAlpha30Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xF5, 0x9E, 0x0B)));
    public static readonly SolidColorBrush SuccessLightAlpha20Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0x4A, 0xDE, 0x80)));
    public static readonly SolidColorBrush SuccessLightAlpha33Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0x4A, 0xDE, 0x80)));
    public static readonly SolidColorBrush WarningLightAlpha20Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0xFB, 0xBF, 0x24)));
    public static readonly SolidColorBrush WarningLightAlpha33Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0xFB, 0xBF, 0x24)));

    // ── Controls 风险边框 alpha Brush ──
    public static readonly SolidColorBrush DangerAlpha25Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x25, 0xEF, 0x44, 0x44)));
    public static readonly SolidColorBrush DangerAlpha15Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x15, 0xEF, 0x44, 0x44)));
    public static readonly SolidColorBrush DangerAlpha40Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xEF, 0x44, 0x44)));
    public static readonly SolidColorBrush WarningAlpha25Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x25, 0xF5, 0x9E, 0x0B)));
    public static readonly SolidColorBrush WarningAlpha40Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xF5, 0x9E, 0x0B)));
    public static readonly SolidColorBrush EmeraldAlpha33Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0x10, 0xB9, 0x81)));
    public static readonly SolidColorBrush EmeraldAlpha40Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x10, 0xB9, 0x81)));

    // ── SettingsPage Info alpha Brush ──
    public static readonly SolidColorBrush InfoLightAlpha20Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x20, 0x60, 0xA5, 0xFA)));
    public static readonly SolidColorBrush InfoAlpha15Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x15, 0x3B, 0x82, 0xF6)));
    public static readonly SolidColorBrush InfoAlpha30Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0x3B, 0x82, 0xF6)));
    public static readonly SolidColorBrush InfoAlpha50Brush = Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0x3B, 0x82, 0xF6)));

    // ── MemoryClean 进程分类色 ──
    public static readonly Color MemSafe = Color.FromRgb(0x34, 0xC7, 0x59);
    public static readonly Color MemCaution = Color.FromRgb(0xFF, 0x95, 0x00);
    public static readonly Color MemKeep = Color.FromRgb(0xA0, 0xA0, 0xA5);
    public static readonly Color MemCritical = Color.FromRgb(0xFF, 0x45, 0x3A);
    public static readonly Color MemWhitelist = Color.FromRgb(0x2E, 0x88, 0xF2);
    public static readonly SolidColorBrush MemSafeBrush = Freeze(new SolidColorBrush(MemSafe));
    public static readonly SolidColorBrush MemCautionBrush = Freeze(new SolidColorBrush(MemCaution));
    public static readonly SolidColorBrush MemKeepBrush = Freeze(new SolidColorBrush(MemKeep));
    public static readonly SolidColorBrush MemCriticalBrush = Freeze(new SolidColorBrush(MemCritical));
    public static readonly SolidColorBrush MemWhitelistBrush = Freeze(new SolidColorBrush(MemWhitelist));
    public static readonly SolidColorBrush MemSafeBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0x34, 0xC7, 0x59)));
    public static readonly SolidColorBrush MemCautionBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0x95, 0x00)));
    public static readonly SolidColorBrush MemKeepBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0xA0, 0xA0, 0xA5)));
    public static readonly SolidColorBrush MemCriticalBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0x45, 0x3A)));
    public static readonly SolidColorBrush MemWhitelistBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x18, 0x2E, 0x88, 0xF2)));

    // ── OperationType badge 色（预冻结，避免 GetTypeInfo 每次 new） ──
    public static readonly Color OpServiceBg = Color.FromRgb(0x1E, 0x3A, 0x5F);
    public static readonly Color OpServiceFg = Color.FromRgb(0x60, 0xA5, 0xFA);
    public static readonly SolidColorBrush OpServiceBgBrush = Freeze(new SolidColorBrush(OpServiceBg));
    public static readonly SolidColorBrush OpServiceFgBrush = Freeze(new SolidColorBrush(OpServiceFg));

    public static readonly Color OpTaskBg = Color.FromRgb(0x3D, 0x1E, 0x5F);
    public static readonly Color OpTaskFg = Color.FromRgb(0xA7, 0x8B, 0xFA);
    public static readonly SolidColorBrush OpTaskBgBrush = Freeze(new SolidColorBrush(OpTaskBg));
    public static readonly SolidColorBrush OpTaskFgBrush = Freeze(new SolidColorBrush(OpTaskFg));

    public static readonly Color OpPowerBg = Color.FromRgb(0x5F, 0x4A, 0x1E);
    public static readonly Color OpPowerFg = Color.FromRgb(0xFB, 0xBF, 0x24);
    public static readonly SolidColorBrush OpPowerBgBrush = Freeze(new SolidColorBrush(OpPowerBg));
    public static readonly SolidColorBrush OpPowerFgBrush = Freeze(new SolidColorBrush(OpPowerFg));

    public static readonly Color OpCommandBg = Color.FromRgb(0x1E, 0x40, 0x3A);
    public static readonly Color OpCommandFg = Color.FromRgb(0x4A, 0xDE, 0x80);
    public static readonly SolidColorBrush OpCommandBgBrush = Freeze(new SolidColorBrush(OpCommandBg));
    public static readonly SolidColorBrush OpCommandFgBrush = Freeze(new SolidColorBrush(OpCommandFg));

    public static readonly Color OpApiBg = Color.FromRgb(0x3A, 0x1E, 0x1E);
    public static readonly Color OpApiFg = Color.FromRgb(0xF8, 0x71, 0x71);
    public static readonly SolidColorBrush OpApiBgBrush = Freeze(new SolidColorBrush(OpApiBg));
    public static readonly SolidColorBrush OpApiFgBrush = Freeze(new SolidColorBrush(OpApiFg));

    public static readonly Color OpRegistryBg = Color.FromRgb(0x1E, 0x3A, 0x3A);
    public static readonly Color OpRegistryFg = Color.FromRgb(0x5E, 0xEA, 0xD4);
    public static readonly SolidColorBrush OpRegistryBgBrush = Freeze(new SolidColorBrush(OpRegistryBg));
    public static readonly SolidColorBrush OpRegistryFgBrush = Freeze(new SolidColorBrush(OpRegistryFg));

    public static readonly Color OpDefaultBg = Color.FromRgb(0x27, 0x27, 0x2A);
    public static readonly Color OpDefaultFg = Color.FromRgb(0xA1, 0xA1, 0xAA);
    public static readonly SolidColorBrush OpDefaultBgBrush = Freeze(new SolidColorBrush(OpDefaultBg));
    public static readonly SolidColorBrush OpDefaultFgBrush = Freeze(new SolidColorBrush(OpDefaultFg));

    // ── 颜色到 Brush 的快速映射（用于替代 ColorConverter.ConvertFromString） ──
    public static SolidColorBrush BrushFromColor(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
