namespace GameOptimizer.Core.Theming;

public sealed class ThemeService : IThemeService
{
    private ThemePalette _palette;

    private static readonly ThemePalette DarkPalette = new(
        Accent: "#22D3EE",
        AccentSecondary: "#8b5dff",
        Background: "#09090B",
        Surface: "#18181B",
        SurfaceVariant: "#27272A",
        Success: "#22C55E",
        Warning: "#F59E0B",
        TextPrimary: "#FFFFFF",
        TextMuted: "#A1A1AA");

    public ThemeService()
    {
        _palette = DarkPalette;

        Timings = new AnimationTimings(
            Fast: TimeSpan.FromMilliseconds(160),
            Normal: TimeSpan.FromMilliseconds(220),
            Slow: TimeSpan.FromMilliseconds(350));
    }

    public ThemePalette Current => _palette;
    public AnimationTimings Timings { get; }

    public event EventHandler<ThemePalette>? ThemeChanged;

    public void ApplyDarkPalette()
    {
        _palette = DarkPalette;
        ThemeChanged?.Invoke(this, _palette);
    }

    public ThemePalette GetDarkPalette() => DarkPalette;
    
}
