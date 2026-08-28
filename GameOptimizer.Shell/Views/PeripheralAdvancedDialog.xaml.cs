// File: GameOptimizer.Shell/Views/PeripheralAdvancedDialog.xaml.cs
using System;
using GameOptimizer.Core.Localization;
using System.Windows;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class PeripheralAdvancedDialog : Window
{
    public PeripheralAdvancedSettings Settings { get; private set; }
    public bool IsConfirmed { get; private set; }
    private PeripheralAdvancedSettings? _originalSettings;

    public PeripheralAdvancedDialog()
    {
        InitializeComponent();
        Settings = LoadCurrentSettings();
        _originalSettings = CloneSettings(Settings);
        DataContext = Settings;
        BindControls();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
    }

    private void BindControls()
    {
        MouseSpeedTextBox.Text = Settings.MouseSpeed.ToString();
        MouseThreshold1TextBox.Text = Settings.MouseThreshold1.ToString();
        MouseThreshold2TextBox.Text = Settings.MouseThreshold2.ToString();
        KeyboardDelayTextBox.Text = Settings.KeyboardDelay;
        KeyboardSpeedTextBox.Text = Settings.KeyboardSpeed;
        MouseQueueTextBox.Text = Settings.MouseDataQueueSize.ToString();
        KeyboardQueueTextBox.Text = Settings.KeyboardDataQueueSize.ToString();
    }

    private static PeripheralAdvancedSettings CloneSettings(PeripheralAdvancedSettings source)
    {
        return new PeripheralAdvancedSettings
        {
            MouseSpeed = source.MouseSpeed,
            MouseThreshold1 = source.MouseThreshold1,
            MouseThreshold2 = source.MouseThreshold2,
            KeyboardDelay = source.KeyboardDelay,
            KeyboardSpeed = source.KeyboardSpeed,
            MouseDataQueueSize = source.MouseDataQueueSize,
            KeyboardDataQueueSize = source.KeyboardDataQueueSize
        };
    }

    private PeripheralAdvancedSettings LoadCurrentSettings()
    {
        var settings = new PeripheralAdvancedSettings();

        try
        {
            var registryReader = WindowsRegistryInfoReader.Instance;
            var mousePath = @"Control Panel\Mouse";
            var keyboardPath = @"Control Panel\Keyboard";
            var mouseSpeed = registryReader.Read("HKCU", mousePath, "MouseSpeed").Value;
            var mouseThreshold1 = registryReader.Read("HKCU", mousePath, "MouseThreshold1").Value;
            var mouseThreshold2 = registryReader.Read("HKCU", mousePath, "MouseThreshold2").Value;
            settings.MouseSpeed = int.TryParse(mouseSpeed?.ToString(), out var speed) ? speed : 0;
            settings.MouseThreshold1 = int.TryParse(mouseThreshold1?.ToString(), out var t1) ? t1 : 0;
            settings.MouseThreshold2 = int.TryParse(mouseThreshold2?.ToString(), out var t2) ? t2 : 0;
            settings.KeyboardDelay = registryReader.Read("HKCU", keyboardPath, "KeyboardDelay").Value?.ToString() ?? "0";
            settings.KeyboardSpeed = registryReader.Read("HKCU", keyboardPath, "KeyboardSpeed").Value?.ToString() ?? "31";
            var mouseQueue = registryReader.Read(
                "HKLM",
                @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
                "MouseDataQueueSize").Value;
            settings.MouseDataQueueSize = Convert.ToInt32(mouseQueue ?? 100);
            var keyboardQueue = registryReader.Read(
                "HKLM",
                @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters",
                "KeyboardDataQueueSize").Value;
            settings.KeyboardDataQueueSize = Convert.ToInt32(keyboardQueue ?? 100);
        }
        catch
        {
            return PeripheralAdvancedSettings.GetDefault();
        }

        return settings;
    }
    protected override void OnClosed(EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        Title = loc.Get("Peripheral.UI.WindowTitle");
    }


    private void RestoreDefault_Click(object sender, RoutedEventArgs e)
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        var confirmed = ConfirmDialog.Show(this,
            loc.Get("Peripheral.RestoreDefaultConfirm"),
            loc.Get("Peripheral.RestoreDefaultConfirmMsg"),
            Constants.IconGlyphs.Question);

        if (confirmed)
        {
            Settings = PeripheralAdvancedSettings.GetDefault();
            BindControls();
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_originalSettings == null) return;

        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        var confirmed = ConfirmDialog.Show(this,
            loc.Get("Peripheral.UndoChangesConfirm"),
            loc.Get("Peripheral.UndoChangesConfirmMsg"),
            Constants.IconGlyphs.Question);

        if (confirmed)
        {
            Settings = CloneSettings(_originalSettings);
            BindControls();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        var validationResult = ValidateInputs();
        if (!validationResult.IsValid)
        {
            InfoDialog.Show(this, loc.Get("Peripheral.ValidationError"),
                loc.Get("Peripheral.ValidationErrorMsg", validationResult.ErrorMessage),
                Constants.IconGlyphs.Warning);
            return;
        }

        try
        {
            Settings.MouseSpeed = int.Parse(MouseSpeedTextBox.Text);
            Settings.MouseThreshold1 = int.Parse(MouseThreshold1TextBox.Text);
            Settings.MouseThreshold2 = int.Parse(MouseThreshold2TextBox.Text);
            Settings.KeyboardDelay = KeyboardDelayTextBox.Text;
            Settings.KeyboardSpeed = KeyboardSpeedTextBox.Text;
            Settings.MouseDataQueueSize = int.Parse(MouseQueueTextBox.Text);
            Settings.KeyboardDataQueueSize = int.Parse(KeyboardQueueTextBox.Text);

            IsConfirmed = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            InfoDialog.Show(this, loc.Get("Peripheral.SaveError"),
                loc.Get("Peripheral.SaveErrorMsg", ex.Message),
                Constants.IconGlyphs.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        DialogResult = false;
        Close();
    }

    private (bool IsValid, string ErrorMessage) ValidateInputs()
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;

        if (!int.TryParse(MouseSpeedTextBox.Text, out var mouseSpeed) || mouseSpeed < 0 || mouseSpeed > 2)
            return (false, loc.Get("Peripheral.Validate.MouseSpeed"));

        if (!int.TryParse(MouseThreshold1TextBox.Text, out var t1) || t1 < 0 || t1 > 100)
            return (false, loc.Get("Peripheral.Validate.MouseThreshold1"));

        if (!int.TryParse(MouseThreshold2TextBox.Text, out var t2) || t2 < 0 || t2 > 100)
            return (false, loc.Get("Peripheral.Validate.MouseThreshold2"));

        if (mouseSpeed > 0 && (t1 == 0 || t2 == 0))
            return (false, loc.Get("Peripheral.Validate.ThresholdRequired"));

        if (!int.TryParse(KeyboardDelayTextBox.Text, out var kbDelay) || kbDelay < 0 || kbDelay > 3)
            return (false, loc.Get("Peripheral.Validate.KeyboardDelay"));

        if (!int.TryParse(KeyboardSpeedTextBox.Text, out var kbSpeed) || kbSpeed < 0 || kbSpeed > 31)
            return (false, loc.Get("Peripheral.Validate.KeyboardSpeed"));

        if (!int.TryParse(MouseQueueTextBox.Text, out var mouseQueue) || mouseQueue < 50 || mouseQueue > 500)
            return (false, loc.Get("Peripheral.Validate.MouseQueue"));

        if (!int.TryParse(KeyboardQueueTextBox.Text, out var kbQueue) || kbQueue < 50 || kbQueue > 500)
            return (false, loc.Get("Peripheral.Validate.KeyboardQueue"));

        return (true, string.Empty);
    }
}
