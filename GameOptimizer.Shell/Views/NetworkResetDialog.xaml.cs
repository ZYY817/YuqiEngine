using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Services;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Shell.Views;

public partial class NetworkResetDialog : Window
{
    public bool IsConfirmed { get; private set; }
    private readonly ChangeConfirmationPrompt? _prompt;

    public NetworkResetDialog() : this(null)
    {
    }

    public NetworkResetDialog(ChangeConfirmationPrompt? prompt)
    {
        _prompt = prompt;
        InitializeComponent();
        Loaded += OnLoaded;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedTexts();
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        DialogTitleText.Text = loc.Get("NetworkReset.Title");
        WarningTitleText.Text = loc.Get("NetworkReset.WarningTitle");
        WarningDescText.Text = loc.Get("NetworkReset.WarningDesc");
        LblOperations.Text = loc.Get("NetworkReset.Operations");
        Op1Text.Text = loc.Get("NetworkReset.Op1");
        Op2Text.Text = loc.Get("NetworkReset.Op2");
        Op3Text.Text = loc.Get("NetworkReset.Op3");
        LblEffects.Text = loc.Get("NetworkReset.Effects");
        Eff1Text.Text = loc.Get("NetworkReset.Eff1");
        Eff2Text.Text = loc.Get("NetworkReset.Eff2");
        Eff3Text.Text = loc.Get("NetworkReset.Eff3");
        BackupTitleText.Text = loc.Get("NetworkReset.BackupTitle");
        BackupDescText.Text = loc.Get("NetworkReset.BackupDesc");
        BackupCheckBox.IsChecked = true;
        BackupCheckBox.IsEnabled = false;
        BackupTitleText.Text = "网络设置已备份";
        BackupDescText.Text = "只有当前网络状态未变化且权限正常时，软件才会继续重置。条件不满足时不会修改网络设置。";
        if (_prompt is not null)
            WarningDescText.Text = _prompt.Summary;
        TipText.Text = IconGlyphs.Lightbulb + " " + loc.Get("NetworkReset.NeedRestart");
        
        CancelBtn.ApplyTemplate();
        if (CancelBtn.Template.FindName("CancelBtnText", CancelBtn) is TextBlock cancelText)
            cancelText.Text = loc.Get("Dialog.Cancel");
        
        ConfirmBtn.ApplyTemplate();
        if (ConfirmBtn.Template.FindName("ConfirmBtnText", ConfirmBtn) is TextBlock confirmText)
            confirmText.Text = loc.Get("NetworkReset.ConfirmReset");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        try
        {
            IsConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetworkResetDialog][Confirm_Click] {ex.Message}");
            InfoDialog.Show(this, loc.Get("Common.Error"), ex.Message, IconGlyphs.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    private void ViewBackups_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var backupFolder = NetworkBackupService.GetBackupFolder();
        if (Directory.Exists(backupFolder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = backupFolder,
                UseShellExecute = true
            });
        }
        else
        {
            ToastService.Instance.Info(loc.Get("Network.BackupConfig"), loc.Get("NetworkReset.NoBackups"));
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    public static bool Show(Window? owner, ChangeConfirmationPrompt prompt)
    {
        var dialog = new NetworkResetDialog(prompt);
        if (owner != null) dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.IsConfirmed;
    }
}

public sealed class NetworkResetConfirmationPrompt : IChangeConfirmationPrompt
{
    private readonly Func<Window?> _ownerProvider;

    public NetworkResetConfirmationPrompt(Func<Window?> ownerProvider) =>
        _ownerProvider = ownerProvider ?? throw new ArgumentNullException(nameof(ownerProvider));

    public async Task<bool> ConfirmAsync(ChangeConfirmationPrompt prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Application.Current.Dispatcher.InvokeAsync(() =>
            NetworkResetDialog.Show(_ownerProvider(), prompt));
    }
}
