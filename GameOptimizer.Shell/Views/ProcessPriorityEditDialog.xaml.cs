// File: GameOptimizer.Shell/Views/ProcessPriorityEditDialog.xaml.cs
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.ProcessPriority;
using Microsoft.Win32;

namespace GameOptimizer.Shell.Views;

public partial class ProcessPriorityEditDialog : Window
{
    private readonly LocalizationService _loc;
    private readonly bool _isEditMode;
    
    public ProcessPriorityConfig? Config { get; private set; }

    public ProcessPriorityEditDialog(ProcessPriorityConfig? existingConfig = null)
    {
        InitializeComponent();
        
        _loc = LocalizationService.Instance;
        _isEditMode = existingConfig != null;
        
        if (existingConfig != null)
        {
            Config = existingConfig;
            LoadConfig(existingConfig);
        }
        
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
    }

    private void UpdateLocalizedTexts()
    {
        Title = _isEditMode 
            ? _loc.Get("ProcessPriority.EditDialogTitle") 
            : _loc.Get("ProcessPriority.AddDialogTitle");
        
        DialogTitle.Text = Title;
        GameNameLabel.Text = _loc.Get("ProcessPriority.GameName");
        ExecutablePathLabel.Text = _loc.Get("ProcessPriority.ExecutablePath");
        BrowseButton.Content = _loc.Get("Common.Browse");
        PriorityLabel.Text = _loc.Get("ProcessPriority.PriorityLevel");
        
        PriorityIdle.Content = $"○ {_loc.Get("ProcessPriority.PriorityIdle")}";
        PriorityBelowNormal.Content = $"○ {_loc.Get("ProcessPriority.PriorityBelowNormal")}";
        PriorityNormal.Content = $"○ {_loc.Get("ProcessPriority.PriorityNormal")}";
        PriorityAboveNormal.Content = $"● {_loc.Get("ProcessPriority.PriorityAboveNormal")} ⭐ {_loc.Get("ProcessPriority.Recommended")}";
        PriorityHigh.Content = $"○ {_loc.Get("ProcessPriority.PriorityHigh")}";
        PriorityRealtime.Content = $"○ {_loc.Get("ProcessPriority.PriorityRealtime")} ⚠️ {_loc.Get("ProcessPriority.Dangerous")}";
        
        RealtimeWarningText.Text = _loc.Get("ProcessPriority.RealtimeWarning");
        EnableCheckBox.Content = _loc.Get("ProcessPriority.EnableImmediately");
        CancelButton.Content = _loc.Get("Common.Cancel");
        ((TextBlock)SaveButton.Content).Text = _loc.Get("Common.Save");
    }

    private void LoadConfig(ProcessPriorityConfig config)
    {
        GameNameTextBox.Text = config.GameName;
        ExecutablePathTextBox.Text = config.ExecutablePath;
        EnableCheckBox.IsChecked = config.IsEnabled;
        
        switch (config.Priority)
        {
            case CpuPriorityLevel.Idle:
                PriorityIdle.IsChecked = true;
                break;
            case CpuPriorityLevel.BelowNormal:
                PriorityBelowNormal.IsChecked = true;
                break;
            case CpuPriorityLevel.Normal:
                PriorityNormal.IsChecked = true;
                break;
            case CpuPriorityLevel.AboveNormal:
                PriorityAboveNormal.IsChecked = true;
                break;
            case CpuPriorityLevel.High:
                PriorityHigh.IsChecked = true;
                break;
            case CpuPriorityLevel.Realtime:
                PriorityRealtime.IsChecked = true;
                RealtimeWarning.Visibility = Visibility.Visible;
                break;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            Title = _loc.Get("ProcessPriority.SelectExecutable")
        };

        if (dialog.ShowDialog() == true)
        {
            ExecutablePathTextBox.Text = dialog.FileName;
            
            if (string.IsNullOrWhiteSpace(GameNameTextBox.Text))
            {
                var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
                GameNameTextBox.Text = fileName;
            }
        }
    }

    private void PriorityRealtime_Checked(object sender, RoutedEventArgs e)
    {
        if (RealtimeWarning != null)
        {
            RealtimeWarning.Visibility = Visibility.Visible;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInput())
        {
            return;
        }

        if (PriorityRealtime.IsChecked == true)
        {
            var result = MessageBox.Show(
                _loc.Get("ProcessPriority.RealtimeConfirm"),
                _loc.Get("Dialog.Warning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var config = Config ?? new ProcessPriorityConfig();
        config.GameName = GameNameTextBox.Text.Trim();
        config.ExecutablePath = ExecutablePathTextBox.Text.Trim();
        config.ExecutableName = Path.GetFileName(config.ExecutablePath);
        config.Priority = GetSelectedPriority();
        config.IsEnabled = EnableCheckBox.IsChecked == true;

        Config = config;
        DialogResult = true;
        Close();
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(GameNameTextBox.Text))
        {
            MessageBox.Show(
                _loc.Get("ProcessPriority.GameNameRequired"),
                _loc.Get("Dialog.ValidationError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            GameNameTextBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(ExecutablePathTextBox.Text))
        {
            MessageBox.Show(
                _loc.Get("ProcessPriority.ExecutablePathRequired"),
                _loc.Get("Dialog.ValidationError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!File.Exists(ExecutablePathTextBox.Text))
        {
            MessageBox.Show(
                _loc.Get("ProcessPriority.ExecutableNotFound"),
                _loc.Get("Dialog.ValidationError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var extension = Path.GetExtension(ExecutablePathTextBox.Text);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                _loc.Get("ProcessPriority.MustBeExe"),
                _loc.Get("Dialog.ValidationError"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private CpuPriorityLevel GetSelectedPriority()
    {
        if (PriorityIdle.IsChecked == true) return CpuPriorityLevel.Idle;
        if (PriorityBelowNormal.IsChecked == true) return CpuPriorityLevel.BelowNormal;
        if (PriorityNormal.IsChecked == true) return CpuPriorityLevel.Normal;
        if (PriorityAboveNormal.IsChecked == true) return CpuPriorityLevel.AboveNormal;
        if (PriorityHigh.IsChecked == true) return CpuPriorityLevel.High;
        if (PriorityRealtime.IsChecked == true) return CpuPriorityLevel.Realtime;
        
        return CpuPriorityLevel.AboveNormal;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
