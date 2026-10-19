// File: GameOptimizer.Shell/Controls/GuideTooltip.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Controls;

public partial class GuideTooltip : UserControl
{
    public event EventHandler? NextClicked;
    public event EventHandler? PrevClicked;
    public event EventHandler? SkipClicked;
    public event EventHandler? CloseClicked;

    public GuideTooltip()
    {
        InitializeComponent();
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto);
        RenderOptions.SetClearTypeHint(this, ClearTypeHint.Enabled);
        Opacity = 0;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Resources["FadeInStoryboard"] is Storyboard storyboard)
        {
            storyboard.Stop(this);
            storyboard.Begin(this, true);
        }
    }

    public void UpdateContent(string title, string content, int currentStep, int totalSteps, bool isLastStep)
    {
        var loc = LocalizationService.Instance;

        TitleText.Text = title;
        ContentText.Text = content;
        ProgressText.Text = loc.Get("Guide.Progress", currentStep, totalSteps);

        PrevButton.IsEnabled = currentStep > 1;
        PrevButton.Opacity = currentStep > 1 ? 1.0 : 0.5;

        if (isLastStep)
        {
            NextButtonText.Text = loc.Get("Guide.Finish");
            NextButtonIcon.Text = "\uE73E";
        }
        else
        {
            NextButtonText.Text = loc.Get("Guide.Next");
            NextButtonIcon.Text = "\uE76C";
        }

        PrevButtonText.Text = loc.Get("Guide.Previous");
        SkipButton.Content = loc.Get("Guide.Skip");
    }

    private void NextButton_Click(object sender, RoutedEventArgs e) => NextClicked?.Invoke(this, EventArgs.Empty);
    private void PrevButton_Click(object sender, RoutedEventArgs e) => PrevClicked?.Invoke(this, EventArgs.Empty);
    private void SkipButton_Click(object sender, RoutedEventArgs e) => SkipClicked?.Invoke(this, EventArgs.Empty);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseClicked?.Invoke(this, EventArgs.Empty);
}
