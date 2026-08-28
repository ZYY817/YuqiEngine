using System.Windows;
using System.Windows.Media;

namespace GameOptimizer.Shell.Services;

public static class ThemeHelper
{
    // 行背景
    public static Color RowBackground => GetColor("Theme.Row.Background", Color.FromRgb(0x18, 0x18, 0x1B));
    public static Color RowBorder => GetColor("Theme.Row.Border", Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
    public static Color RowBackgroundHover => GetColor("Theme.Row.BackgroundHover", Color.FromRgb(0x1F, 0x1F, 0x23));
    public static Color RowBorderHover => GetColor("Theme.Row.BorderHover", Color.FromArgb(0x40, 0x3B, 0x82, 0xF6));
    
    // 图标容器
    public static Color IconBackground => GetColor("Theme.Icon.Background", Color.FromRgb(0x27, 0x27, 0x2A));
    public static Color IconBorder => GetColor("Theme.Icon.Border", Color.FromRgb(0x3F, 0x3F, 0x46));
    
    // 按钮
    public static Color ButtonBackground => GetColor("Theme.Button.Background", Color.FromRgb(0x27, 0x27, 0x2A));
    public static Color ButtonBackgroundHover => GetColor("Theme.Button.BackgroundHover", Color.FromRgb(0x3F, 0x3F, 0x46));
    public static Color ButtonBorder => GetColor("Theme.Button.Border", Color.FromRgb(0x3F, 0x3F, 0x46));
    
    // 文字
    public static Color TextPrimary => GetColor("Theme.Text.PrimaryAlt", Color.FromRgb(0xFA, 0xFA, 0xFA));
    public static Color TextSecondary => GetColor("Theme.Text.Secondary", Color.FromRgb(0xA1, 0xA1, 0xAA));
    public static Color TextMuted => GetColor("Theme.Text.Muted", Color.FromRgb(0x71, 0x71, 0x7A));
    public static Color TextDisabled => GetColor("Theme.Text.Disabled", Color.FromRgb(0x52, 0x52, 0x5B));
    
    // 输入框
    public static Color InputBackground => GetColor("Theme.Input.Background", Color.FromRgb(0x1F, 0x1F, 0x23));
    public static Color InputBorder => GetColor("Theme.Input.Border", Color.FromRgb(0x3F, 0x3F, 0x46));
    
    // 卡片
    public static Color CardBackground => GetColor("Theme.Card.Background", Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
    public static Color CardBorder => GetColor("Theme.Card.Border", Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
    public static Color CardBackgroundHover => GetColor("Theme.Card.BackgroundHover", Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF));
    
    // 对话框
    public static Color DialogBackground => GetColor("Theme.Dialog.Background", Color.FromRgb(0x0F, 0x0F, 0x11));
    
    // 强调色（这些不随主题变化）
    public static Color AccentCyan => Color.FromRgb(0x22, 0xD3, 0xEE);
    public static Color AccentBlue => Color.FromRgb(0x3B, 0x82, 0xF6);
    public static Color AccentGreen => Color.FromRgb(0x4A, 0xDE, 0x80);
    public static Color AccentRed => Color.FromRgb(0xEF, 0x44, 0x44);
    public static Color AccentYellow => Color.FromRgb(0xFB, 0xBF, 0x24);
    
    // Why: Accent colors don't change with theme, so Frozen cached brushes avoid GC pressure
    private static readonly SolidColorBrush _accentCyanBrush = CreateFrozenBrush(AccentCyan);
    private static readonly SolidColorBrush _accentBlueBrush = CreateFrozenBrush(AccentBlue);
    
    // Brush 快捷方法 - 每次调用都重新获取当前主题颜色
    public static SolidColorBrush RowBackgroundBrush => new(RowBackground);
    public static SolidColorBrush RowBorderBrush => new(RowBorder);
    public static SolidColorBrush ButtonBackgroundBrush => new(ButtonBackground);
    public static SolidColorBrush ButtonBackgroundHoverBrush => new(ButtonBackgroundHover);
    public static SolidColorBrush TextPrimaryBrush => new(TextPrimary);
    public static SolidColorBrush TextSecondaryBrush => new(TextSecondary);
    public static SolidColorBrush TextMutedBrush => new(TextMuted);
    public static SolidColorBrush TextDisabledBrush => new(TextDisabled);
    public static SolidColorBrush InputBackgroundBrush => new(InputBackground);
    public static SolidColorBrush InputBorderBrush => new(InputBorder);
    public static SolidColorBrush AccentCyanBrush => _accentCyanBrush;
    public static SolidColorBrush AccentBlueBrush => _accentBlueBrush;
    
    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
    
    // 获取DynamicResource Brush（用于需要动态绑定的场景）
    public static Brush GetDynamicBrush(string key)
    {
        try
        {
            if (Application.Current?.Resources[key] is Brush brush)
                return brush;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper] Failed to get brush '{key}': {ex.Message}");
        }
        return new SolidColorBrush(Colors.Gray);
    }
    
    private static Color GetColor(string key, Color fallback)
    {
        try
        {
            if (Application.Current?.Resources[key] is Color color)
                return color;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThemeHelper] Failed to get color '{key}': {ex.Message}");
        }
        return fallback;
    }
    
    public static SolidColorBrush GetBrush(string key, Color fallback)
    {
        return new SolidColorBrush(GetColor(key, fallback));
    }
}
