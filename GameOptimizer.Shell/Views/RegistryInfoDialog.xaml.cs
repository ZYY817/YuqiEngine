using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class RegistryInfoDialog : Window
{
    private readonly string _hive;
    private readonly string _keyPath;
    private readonly string _valueName;
    private readonly string? _optimizedValue;
    private readonly string? _defaultValue;
    private readonly string _fullPath;
    private readonly IRegistryInfoReader _registryReader;

    public RegistryInfoDialog(
        string hive, 
        string path, 
        string? currentValue = null, 
        string? optimizedValue = null,
        string? title = null,
        string? description = null,
        string? valueName = null,
        string? defaultValue = null,
        string? valueType = null,
        string? whyExplanation = null,
        IRegistryInfoReader? registryReader = null)
    {
        InitializeComponent();

        _registryReader = registryReader ?? WindowsRegistryInfoReader.Instance;
        _hive = hive;
        _optimizedValue = optimizedValue;
        _defaultValue = defaultValue;
        
        // 解析路径，分离键路径和值名称
        if (!string.IsNullOrEmpty(valueName))
        {
            _keyPath = path;
            _valueName = valueName;
        }
        else if (path.Contains('\\'))
        {
            var lastSlash = path.LastIndexOf('\\');
            if (lastSlash > 0 && lastSlash < path.Length - 1)
            {
                _keyPath = path.Substring(0, lastSlash);
                _valueName = path.Substring(lastSlash + 1);
            }
            else
            {
                _keyPath = path;
                _valueName = path;
            }
        }
        else
        {
            _keyPath = path;
            _valueName = path;
        }
        
        _fullPath = $"{hive}\\{_keyPath}";
        
        // 设置 UI
        if (!string.IsNullOrEmpty(title))
        {
            DialogTitle.Text = title;
            OptimizationTitle.Text = title;
        }
        
        if (!string.IsNullOrEmpty(description))
            OptimizationDesc.Text = description;
        else
            OptimizationDesc.Visibility = Visibility.Collapsed;
        
        KeyPathText.Text = _fullPath;
        ValueNameText.Text = _valueName;
        StepValueName.Text = _valueName;
        
        var loc = LocalizationService.Instance;
        OptimizedValueText.Text = optimizedValue ?? loc.Get("Registry.Unknown");
        DefaultValueText.Text = defaultValue ?? loc.Get("Registry.NotSetDefault");
        ValueTypeText.Text = valueType ?? "DWORD (32-bit)";
        
        // Why 说明
        if (!string.IsNullOrEmpty(whyExplanation))
        {
            WhyPanel.Visibility = Visibility.Visible;
            WhyText.Text = whyExplanation;
        }
        else
        {
            WhyPanel.Visibility = Visibility.Collapsed;
        }
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        
        // 读取当前系统值
        RefreshCurrentValue();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
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
        
        // 标题
        Title = loc.Get("Registry.Title");
        DialogTitle.Text = loc.Get("Registry.Title");
        
        // 标签
        LblRegLocation.Text = Constants.IconGlyphs.Pin + " " + loc.Get("Registry.Location");
        LblWhyTitle.Text = "💡 " + loc.Get("Registry.WhyTitle");
        LblKeyPath.Text = loc.Get("Registry.KeyPath");
        LblValueName.Text = loc.Get("Registry.ValueName");
        LblValueCompare.Text = Constants.IconGlyphs.Chart + " " + loc.Get("Registry.ValueCompare");
        LblCurrentValue.Text = loc.Get("Registry.CurrentValue");
        LblOptimizedValue.Text = loc.Get("Registry.OptimizedValue");
        LblDefaultValue.Text = loc.Get("Registry.DefaultValue");
        LblValueType.Text = loc.Get("Registry.ValueType");
        LblManualMethod.Text = loc.Get("Registry.ManualMethod");
        CopyBtnText.Text = loc.Get("Registry.CopyPath");
        
        // 手动步骤
        Step1Label.Text = loc.Get("Registry.Step1");
        Step1Text.Text = loc.Get("Registry.Step1Desc");
        Step2Label.Text = loc.Get("Registry.Step2");
        Step2Text.Text = loc.Get("Registry.Step2Desc");
        Step3Label.Text = loc.Get("Registry.Step3");
        Step3Text.Text = loc.Get("Registry.Step3Desc");
        Step4Label.Text = loc.Get("Registry.Step4");
        Step4Text.Text = loc.Get("Registry.Step4Desc");
        
        // 按钮 (ControlTemplate 内部元素)
        RefreshBtn.ApplyTemplate();
        if (RefreshBtn.Template.FindName("RefreshText", RefreshBtn) is TextBlock refreshText)
            refreshText.Text = loc.Get("Registry.Refresh");
        
        OpenRegeditBtn.ApplyTemplate();
        if (OpenRegeditBtn.Template.FindName("OpenRegeditText", OpenRegeditBtn) is TextBlock openText)
            openText.Text = loc.Get("Registry.OpenRegedit");
        
        CloseBtn.ApplyTemplate();
        if (CloseBtn.Template.FindName("CloseText", CloseBtn) is TextBlock closeText)
            closeText.Text = loc.Get("Common.Close");
    }

    // 兼容旧的构造函数
    public RegistryInfoDialog(string hive, string path, string? currentValue, string? optimizedValue)
        : this(hive, path, currentValue, optimizedValue, null, null, null, null, null)
    {
    }

    private void RefreshCurrentValue()
    {
        try
        {
            var loc = LocalizationService.Instance;
            var registryValue = _registryReader.Read(_hive, _keyPath, _valueName);

            if (registryValue.Exists)
            {
                CurrentValueText.Text = registryValue.Value?.ToString() ?? loc.Get("Registry.Empty");
                
                // 更新状态指示
                var isOptimized = _optimizedValue != null && 
                                  string.Equals(registryValue.Value?.ToString(), _optimizedValue, StringComparison.OrdinalIgnoreCase);
                var isDefault = _defaultValue != null && 
                                string.Equals(registryValue.Value?.ToString(), _defaultValue, StringComparison.OrdinalIgnoreCase);
                if (isOptimized)
                {
                    StatusBorder.Background = ThemeColors.SuccessLightAlpha20Brush;
                    StatusIcon.Text = Constants.IconGlyphs.Check;
                    StatusIcon.Foreground = ThemeColors.SuccessLightBrush;
                    StatusText.Text = loc.Get("Registry.StatusOptimized");
                    StatusText.Foreground = ThemeColors.SuccessLightBrush;
                }
                else if (isDefault)
                {
                    StatusBorder.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.CardBackgroundMuted");
                    StatusIcon.Text = "○";
                    StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextMuted");
                    StatusText.Text = loc.Get("Registry.StatusDefault");
                    StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
                }
                else
                {
                    StatusBorder.Background = ThemeColors.WarningLightAlpha20Brush;
                    StatusIcon.Text = "!";
                    StatusIcon.Foreground = ThemeColors.WarningLightBrush;
                    StatusText.Text = loc.Get("Registry.StatusCustom");
                    StatusText.Foreground = ThemeColors.WarningLightBrush;
                }
                
                // 更新值类型
                ValueTypeText.Text = registryValue.Kind switch
                {
                    RegistryInfoValueKind.DWord => loc.Get("Registry.TypeDword"),
                    RegistryInfoValueKind.QWord => loc.Get("Registry.TypeQword"),
                    RegistryInfoValueKind.String => loc.Get("Registry.TypeString"),
                    RegistryInfoValueKind.ExpandString => loc.Get("Registry.TypeExpandString"),
                    RegistryInfoValueKind.MultiString => loc.Get("Registry.TypeMultiString"),
                    RegistryInfoValueKind.Binary => loc.Get("Registry.TypeBinary"),
                    _ => loc.Get("Registry.TypeUnknown")
                };
            }
            else
            {
                CurrentValueText.Text = loc.Get("Registry.NotSet");
                StatusBorder.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.CardBackgroundMuted");
                StatusIcon.Text = "○";
                StatusIcon.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextMuted");
                StatusText.Text = loc.Get("Registry.StatusNotExist");
                StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
            }
        }
        catch (Exception ex)
        {
            var loc = LocalizationService.Instance;
            CurrentValueText.Text = loc.Get("Registry.ReadFailed");
            StatusBorder.Background = ThemeColors.DangerAlpha20Brush;
            StatusIcon.Text = "✕";
            StatusIcon.Foreground = ThemeColors.DangerBrush;
            StatusText.Text = loc.Get("Registry.CannotRead", ex.Message);
            StatusText.Foreground = ThemeColors.DangerBrush;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshCurrentValue();
    }

    private void CopyPath_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var regeditPath = _fullPath.Replace("HKLM", "HKEY_LOCAL_MACHINE")
                                       .Replace("HKCU", "HKEY_CURRENT_USER");
            var textToCopy = $"Computer\\{regeditPath}";
            var loc = LocalizationService.Instance;
            
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetDataObject(textToCopy, true);
                    CopyBtnText.Text = loc.Get("Common.Copied");
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                    timer.Tick += (s, args) => { CopyBtnText.Text = loc.Get("Registry.CopyPath"); timer.Stop(); };
                    timer.Start();
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    System.Threading.Thread.Sleep(50);
                }
            }
            
            InfoDialog.Show(this, loc.Get("Dialog.Info"), loc.Get("Registry.ClipboardBusy", textToCopy), Constants.IconGlyphs.Lightbulb);
        }
        catch (Exception ex)
        {
            var loc = LocalizationService.Instance;
            InfoDialog.Show(this, loc.Get("Common.Error"), loc.Get("Registry.CopyFailed", ex.Message), Constants.IconGlyphs.Warning);
        }
    }

    private void OpenRegedit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Regedit's LastKey belongs to another application's UI state. This dialog has no
            // authenticated cross-process navigation contract, so it must not write that registry key.
            Process.Start(new ProcessStartInfo
            {
                FileName = "regedit.exe",
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex)
        {
            var loc = LocalizationService.Instance;
            InfoDialog.Show(this, loc.Get("Common.Error"), loc.Get("Registry.OpenRegeditFailed", ex.Message), Constants.IconGlyphs.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
