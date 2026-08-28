// File: GameOptimizer.Shell/Views/WelcomeGuideDialog.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Views;

public partial class WelcomeGuideDialog : Window
{
    public bool DontShowAgain { get; private set; }
    
    public WelcomeGuideDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
        CheckScrollPosition();
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        // Title
        TitleLabel.Text = loc.Get("Welcome.Title");
        
        // Step 1
        Step1Title.Text = loc.Get("Welcome.Step1.Title");
        Step1Content.Text = loc.Get("Welcome.Step1.Content");
        ContactLabel.Text = loc.Get("Welcome.Step1.ContactLabel");
        QQNumber.Text = loc.Get("Welcome.QQNumber");
        DouyinNumber.Text = loc.Get("Welcome.DouyinNumber");
        BilibiliAccount.Text = loc.Get("Welcome.BilibiliAccount");
        
        // Step 2
        Step2Title.Text = loc.Get("Welcome.Step2.Title");
        Step2Content.Text = loc.Get("Welcome.Step2.Content");
        BackupMethodLabel.Text = loc.Get("Welcome.Step2.BackupMethodLabel");
        BackupSteps.Text = loc.Get("Welcome.Step2.BackupSteps");
        
        // Step 3
        Step3Title.Text = loc.Get("Welcome.Step3.Title");
        Step3Content.Text = loc.Get("Welcome.Step3.Content");
        
        // Step 4
        Step4Title.Text = loc.Get("Welcome.Step4.Title");
        Step4Content.Text = loc.Get("Welcome.Step4.Content");
        QRCodeLabel.Text = loc.Get("Welcome.Step4.QRCodeLabel");
        
        // Footer
        DontShowAgainLabel.Text = loc.Get("Welcome.DontShowAgain");
        StartButtonText.Text = loc.Get("Welcome.StartButton");
    }
    
    private void ContentScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        CheckScrollPosition();
    }
    
    private void CheckScrollPosition()
    {
        if (ContentScrollViewer.VerticalOffset + ContentScrollViewer.ViewportHeight >= ContentScrollViewer.ExtentHeight - 10)
        {
            StartButton.IsEnabled = true;
            StartButtonBorder.Opacity = 1.0;
        }
        else
        {
            StartButton.IsEnabled = false;
            StartButtonBorder.Opacity = 0.5;
        }
    }
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
    
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    
    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        DontShowAgain = DontShowAgainCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
