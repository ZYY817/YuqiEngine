namespace GameOptimizer.Core.Theming;

public interface IThemeService
{
    ThemePalette Current { get; }
    AnimationTimings Timings { get; }
    event EventHandler<ThemePalette>? ThemeChanged;
    void ApplyDarkPalette();
    ThemePalette GetDarkPalette();
}
