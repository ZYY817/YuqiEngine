// File: GameOptimizer.Shell/Views/BackupSelectDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Shell.Views;

public partial class BackupSelectDialog : Window
{
    public SnapshotInfo? SelectedBackup { get; private set; }
    public bool NavigateToBackupManager { get; private set; }
    
    private readonly ISnapshotService _snapshotService;

    public BackupSelectDialog(ISnapshotService snapshotService)
    {
        InitializeComponent();
        _snapshotService = snapshotService;
        Loaded += OnLoaded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    protected override void OnClosed(System.EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
        await LoadBackupsAsync();
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        TitleText.Text = loc.Get("BackupSelect.Title");
        SubtitleText.Text = loc.Get("BackupSelect.Subtitle");
        NoBackupText.Text = loc.Get("BackupSelect.NoBackup");
        NoBackupHint.Text = loc.Get("BackupSelect.NoBackupHint");
        
        ManageBackupBtn.ApplyTemplate();
        if (ManageBackupBtn.Template.FindName("ManageBackupText", ManageBackupBtn) is TextBlock manageText)
            manageText.Text = loc.Get("BackupSelect.ManageBackup");
        
        CancelBtn.ApplyTemplate();
        if (CancelBtn.Template.FindName("CancelBtnText", CancelBtn) is TextBlock cancelText)
            cancelText.Text = loc.Get("Dialog.Cancel");
        
        ConfirmBtn.ApplyTemplate();
        if (ConfirmBtn.Template.FindName("ConfirmBtnText", ConfirmBtn) is TextBlock confirmText)
            confirmText.Text = loc.Get("BackupSelect.RestoreThis");
    }

    private async Task LoadBackupsAsync()
    {
        try
        {
            var snapshots = await _snapshotService.GetSnapshotsAsync();
            var sortedSnapshots = snapshots.OrderByDescending(s => s.CreatedAt).ToList();
            
            if (sortedSnapshots.Count == 0)
            {
                BackupList.Visibility = Visibility.Collapsed;
                NoBackupPanel.Visibility = Visibility.Visible;
            }
            else
            {
                BackupList.ItemsSource = sortedSnapshots;
                BackupList.Visibility = Visibility.Visible;
                NoBackupPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BackupSelectDialog] LoadBackupsAsync failed: {ex.Message}");
            BackupList.Visibility = Visibility.Collapsed;
            NoBackupPanel.Visibility = Visibility.Visible;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ConfirmBtn.IsEnabled = BackupList.SelectedItem != null;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        SelectedBackup = BackupList.SelectedItem as SnapshotInfo;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ManageBackup_Click(object sender, RoutedEventArgs e)
    {
        NavigateToBackupManager = true;
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    public static (SnapshotInfo? Selected, bool NavigateToManager) Show(Window? owner, ISnapshotService snapshotService)
    {
        var dialog = new BackupSelectDialog(snapshotService);
        if (owner != null) dialog.Owner = owner;
        dialog.ShowDialog();
        return (dialog.SelectedBackup, dialog.NavigateToBackupManager);
    }
}
