// File: GameOptimizer.Shell/Views/DangerousOperationDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Views;

public partial class DangerousOperationDialog : Window
{
    public bool IsConfirmed { get; private set; }
    
    private readonly string? _confirmationKeyword;
    private readonly int _countdownSeconds;
    private int _remainingSeconds;
    private DispatcherTimer? _countdownTimer;

    public DangerousOperationDialog(
        string title, 
        string message, 
        IEnumerable<string>? consequences = null,
        string? confirmationKeyword = null,
        int countdownSeconds = 0)
    {
        InitializeComponent();
        
        TitleText.Text = title;
        MessageText.Text = message;
        _confirmationKeyword = confirmationKeyword;
        _countdownSeconds = countdownSeconds;
        
        if (consequences != null)
        {
            ConsequencesPanel.Visibility = Visibility.Visible;
            ConsequencesList.ItemsSource = consequences;
        }
        
        if (!string.IsNullOrEmpty(confirmationKeyword))
        {
            KeywordPanel.Visibility = Visibility.Visible;
            KeywordPrompt.Text = LocalizationService.Instance.Get("Dialog.ConfirmKeyword", confirmationKeyword);
        }
        
        if (countdownSeconds > 0)
        {
            _remainingSeconds = countdownSeconds;
            CountdownPanel.Visibility = Visibility.Visible;
            StartCountdown();
        }
        else if (string.IsNullOrEmpty(confirmationKeyword))
        {
            ConfirmBtn.IsEnabled = true;
        }
        
        Loaded += OnLoaded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        _countdownTimer?.Stop();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
        if (!string.IsNullOrEmpty(_confirmationKeyword))
        {
            KeywordInput.Focus();
        }
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        ConsequencesTitle.Text = loc.Get("Dialog.Consequences");
        
        CancelBtn.ApplyTemplate();
        if (CancelBtn.Template.FindName("CancelBtnText", CancelBtn) is System.Windows.Controls.TextBlock cancelText)
            cancelText.Text = loc.Get("Dialog.Cancel");
        
        ConfirmBtn.ApplyTemplate();
        if (ConfirmBtn.Template.FindName("ConfirmBtnText", ConfirmBtn) is System.Windows.Controls.TextBlock confirmText)
            confirmText.Text = loc.Get("Dialog.ConfirmExecute");
    }

    private void StartCountdown()
    {
        UpdateCountdownText();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (s, e) =>
        {
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                _countdownTimer.Stop();
                CountdownPanel.Visibility = Visibility.Collapsed;
                if (string.IsNullOrEmpty(_confirmationKeyword))
                {
                    ConfirmBtn.IsEnabled = true;
                }
            }
            else
            {
                UpdateCountdownText();
            }
        };
        _countdownTimer.Start();
    }

    private void UpdateCountdownText()
    {
        var loc = LocalizationService.Instance;
        CountdownText.Text = loc.Get("Dialog.CountdownWait", _remainingSeconds);
    }

    private void KeywordInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(_confirmationKeyword))
            return;
            
        var isMatch = KeywordInput.Text.Trim().Equals(_confirmationKeyword, StringComparison.OrdinalIgnoreCase);
        var countdownFinished = _countdownSeconds <= 0 || _remainingSeconds <= 0;
        
        ConfirmBtn.IsEnabled = isMatch && countdownFinished;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        _countdownTimer?.Stop();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        _countdownTimer?.Stop();
        Close();
    }
    
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsConfirmed = false;
            _countdownTimer?.Stop();
            Close();
            e.Handled = true;
        }
    }

    public static bool Show(
        Window? owner, 
        string title, 
        string message, 
        IEnumerable<string>? consequences = null,
        string? confirmationKeyword = null,
        int countdownSeconds = 0)
    {
        var dialog = new DangerousOperationDialog(title, message, consequences, confirmationKeyword, countdownSeconds);
        if (owner != null) dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.IsConfirmed;
    }
}
