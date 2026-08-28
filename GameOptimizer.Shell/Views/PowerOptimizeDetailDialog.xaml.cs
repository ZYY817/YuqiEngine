// File: GameOptimizer.Shell/Views/PowerOptimizeDetailDialog.xaml.cs
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class PowerOptimizeDetailDialog : Window
{
    private HardwareInfo? _hardware;
    private OptimalPowerSettings? _settings;
    private string _cpuInfo = "";
    
    public PowerOptimizeDetailDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (s, e) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_settings != null)
            {
                UpdateLocalizedTexts();
            }
        });
    }
    
    private void UpdateLocalizedTexts()
    {
        if (_settings == null) return;
        
        var loc = LocalizationService.Instance;
        TxtSubtitle.Text = loc.Get("PowerDetail.Subtitle", _cpuInfo);
        TxtPlanReason.Text = loc.Get("PowerDetail.PlanReason", _cpuInfo, _settings.RuleDescription);
        
        BuildRiskOverview(_settings);
        BuildSettingsCompareList(_settings);
        BuildSideEffectsText(_settings);
        BuildCoolingTipsText(_settings);
        BuildPerformanceGainText(_settings);
        BuildRisksText(_settings);
        BuildFAQList(_settings);
        BuildRecommendationsText(_settings);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public void SetOptimizationDetails(OptimalPowerSettings settings, string planName, string planDesc, string cpuInfo, HardwareInfo? hardware = null)
    {
        _hardware = hardware;
        _settings = settings;
        _cpuInfo = cpuInfo;
        
        var loc = LocalizationService.Instance;
        TxtPlanName.Text = planName;
        TxtPlanDesc.Text = planDesc;
        TxtSubtitle.Text = loc.Get("PowerDetail.Subtitle", cpuInfo);
        TxtPlanReason.Text = loc.Get("PowerDetail.PlanReason", cpuInfo, settings.RuleDescription);
        
        BuildRiskOverview(settings);
        BuildSettingsCompareList(settings);
        BuildSideEffectsText(settings);
        BuildCoolingTipsText(settings);
        BuildPerformanceGainText(settings);
        BuildRisksText(settings);
        BuildFAQList(settings);
        BuildRecommendationsText(settings);
    }
    
    private void BuildRiskOverview(OptimalPowerSettings settings)
    {
        var loc = LocalizationService.Instance;
        
        // 温度风险
        if (settings.ThermalRiskLevel >= 60)
        {
            TxtThermalRiskValue.Text = loc.Get("PowerDetail.RiskHigh");
            TxtThermalRiskValue.Foreground = ThemeColors.DangerBrush;
        }
        else if (settings.ThermalRiskLevel >= 40)
        {
            TxtThermalRiskValue.Text = loc.Get("PowerDetail.RiskMedium");
            TxtThermalRiskValue.Foreground = ThemeColors.WarningBrush;
        }
        else
        {
            TxtThermalRiskValue.Text = loc.Get("PowerDetail.RiskLow");
            TxtThermalRiskValue.Foreground = ThemeColors.SuccessBrush;
        }
        
        // 噪音风险
        if (settings.NoiseRiskLevel >= 60)
        {
            TxtNoiseRiskValue.Text = loc.Get("PowerDetail.RiskHigh");
            TxtNoiseRiskValue.Foreground = ThemeColors.DangerBrush;
        }
        else if (settings.NoiseRiskLevel >= 40)
        {
            TxtNoiseRiskValue.Text = loc.Get("PowerDetail.RiskMedium");
            TxtNoiseRiskValue.Foreground = ThemeColors.WarningBrush;
        }
        else
        {
            TxtNoiseRiskValue.Text = loc.Get("PowerDetail.RiskLow");
            TxtNoiseRiskValue.Foreground = ThemeColors.SuccessBrush;
        }
        
        // 功耗增加
        TxtPowerIncreaseValue.Text = $"+{settings.PowerConsumptionIncrease}%";
        if (settings.PowerConsumptionIncrease >= 40)
            TxtPowerIncreaseValue.Foreground = ThemeColors.DangerBrush;
        else if (settings.PowerConsumptionIncrease >= 20)
            TxtPowerIncreaseValue.Foreground = ThemeColors.WarningBrush;
        else
            TxtPowerIncreaseValue.Foreground = ThemeColors.SuccessBrush;
    }
    
    private void BuildSideEffectsText(OptimalPowerSettings settings)
    {
        if (settings.SideEffects.Count > 0)
        {
            TxtSideEffects.Text = string.Join("\n\n", settings.SideEffects);
            SideEffectsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            SideEffectsPanel.Visibility = Visibility.Collapsed;
        }
    }
    
    private void BuildCoolingTipsText(OptimalPowerSettings settings)
    {
        TxtCoolerRecommendation.Text = settings.CoolerRecommendation;
        
        if (settings.CoolingTips.Count > 0)
        {
            TxtCoolingTips.Text = string.Join("\n", settings.CoolingTips);
        }
    }
    
    private void BuildFAQList(OptimalPowerSettings settings)
    {
        FAQList.Children.Clear();
        
        foreach (var (question, answer) in settings.FAQ)
        {
            var faqItem = CreateFAQItem(question, answer);
            FAQList.Children.Add(faqItem);
        }
    }
    
    private Border CreateFAQItem(string question, string answer)
    {
        var border = new Border
        {
            Background = ThemeColors.WhiteAlpha08Brush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderBrush = ThemeColors.WhiteAlpha10Brush,
            BorderThickness = new Thickness(1)
        };
        
        var expander = new Expander
        {
            IsExpanded = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White
        };
        
        // 问题标题
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
        headerStack.Children.Add(new TextBlock
        {
            Text = "Q: ",
            Foreground = ThemeColors.CyanBrush,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = question,
            Foreground = Brushes.White,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        expander.Header = headerStack;
        
        // 答案内容
        var answerText = new TextBlock
        {
            Text = answer,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
            Margin = new Thickness(20, 8, 0, 0)
        };
        answerText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
        expander.Content = answerText;
        
        border.Child = expander;
        return border;
    }

    private void BuildSettingsCompareList(OptimalPowerSettings settings)
    {
        var loc = LocalizationService.Instance;
        SettingsCompareList.Children.Clear();
        
        void AddCategoryHeader(string titleKey, string icon, Color color)
        {
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x15, color.R, color.G, color.B)),
                Padding = new Thickness(14, 8, 14, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock { Text = icon, FontSize = 12, Margin = new Thickness(0, 0, 6, 0) });
            stack.Children.Add(new TextBlock { Text = loc.Get(titleKey), Foreground = new SolidColorBrush(color), FontSize = 12, FontWeight = FontWeights.SemiBold });
            header.Child = stack;
            SettingsCompareList.Children.Add(header);
        }
        
        var isFirst = true;
        
        // ========== 低延迟设置 ==========
        AddCategoryHeader("PowerDetail.CategoryLowLatency", Constants.IconGlyphs.Power, ThemeColors.Cyan);
        isFirst = true;
        
        AddSettingRow(loc.Get("PowerDetail.PerfIncreaseThreshold"), "10%", $"{settings.PerfIncreaseThreshold}%",
            settings.PerfIncreaseThreshold <= 5 ? loc.Get("PowerDetail.PerfIncreaseThresholdDesc") : loc.Get("PowerDetail.PerfIncreaseThresholdDescStd"),
            settings.PerfIncreaseThreshold <= 5, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.IdleDemoteThreshold"), "40%", $"{settings.IdleDemoteThreshold}%",
            settings.IdleDemoteThreshold >= 100 ? loc.Get("PowerDetail.IdleDemoteThresholdDesc") : loc.Get("PowerDetail.IdleDemoteThresholdDescStd"),
            settings.IdleDemoteThreshold >= 80, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.CoreParkingMin"), "100%", $"{settings.CoreParkingMin}%",
            settings.CoreParkingMin >= 100 ? loc.Get("PowerDetail.CoreParkingMinDescActive") :
            settings.CoreParkingMin >= 50 ? loc.Get("PowerDetail.CoreParkingMinDescPartial") : loc.Get("PowerDetail.CoreParkingMinDescSleep"),
            settings.CoreParkingMin >= 80, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.PcieAspm"), loc.Get("PowerDetail.PcieAspmMedium"), settings.DisableAspm ? loc.Get("PowerDetail.PcieAspmOff") : loc.Get("PowerDetail.PcieAspmMedium"),
            settings.DisableAspm ? loc.Get("PowerDetail.PcieAspmDescOff") : loc.Get("PowerDetail.PcieAspmDescOn"),
            settings.DisableAspm, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.UsbSuspend"), loc.Get("PowerDetail.Enabled"), settings.DisableUsbSuspend ? loc.Get("PowerDetail.Disabled") : loc.Get("PowerDetail.Enabled"),
            settings.DisableUsbSuspend ? loc.Get("PowerDetail.UsbSuspendDescOff") : loc.Get("PowerDetail.UsbSuspendDescOn"),
            settings.DisableUsbSuspend, ref isFirst);
        
        // ========== 处理器设置 ==========
        AddCategoryHeader("PowerDetail.CategoryProcessor", Constants.IconGlyphs.Cpu, ThemeColors.Purple);
        isFirst = true;
        
        AddSettingRow(loc.Get("PowerDetail.MinProcessorState"), "5%", $"{settings.MinProcessorState}%",
            settings.MinProcessorState >= 100 ? loc.Get("PowerDetail.MinProcessorStateDescHigh") : 
            settings.MinProcessorState >= 50 ? loc.Get("PowerDetail.MinProcessorStateDescMed") : loc.Get("PowerDetail.MinProcessorStateDescLow"),
            settings.MinProcessorState >= 80, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.MaxProcessorState"), "100%", $"{settings.MaxProcessorState}%",
            loc.Get("PowerDetail.MaxProcessorStateDesc"), false, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.BoostMode"), loc.Get("PowerDetail.Aggressive"), GetBoostModeText(settings.BoostMode),
            settings.BoostMode >= 2 ? loc.Get("PowerDetail.BoostModeDescAggressive") : loc.Get("PowerDetail.BoostModeDescStd"),
            settings.BoostMode >= 2, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.CoreParkingMax"), "100%", $"{settings.CoreParkingMax}%",
            loc.Get("PowerDetail.CoreParkingMaxDesc"), false, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.CpuThrottle"), loc.Get("PowerDetail.Enabled"), settings.DisableThrottle ? loc.Get("PowerDetail.Disabled") : loc.Get("PowerDetail.Enabled"),
            settings.DisableThrottle ? loc.Get("PowerDetail.CpuThrottleDescOff") : loc.Get("PowerDetail.CpuThrottleDescOn"),
            settings.DisableThrottle, ref isFirst);
        
        // ========== 处理器高级设置 ==========
        AddCategoryHeader("PowerDetail.CategoryProcessorAdvanced", Constants.IconGlyphs.Settings, ThemeColors.TextMuted);
        isFirst = true;
        
        AddSettingRow(loc.Get("PowerDetail.PerfDecreaseThreshold"), "10%", $"{settings.PerfDecreaseThreshold}%",
            loc.Get("PowerDetail.PerfDecreaseThresholdDesc"), settings.PerfDecreaseThreshold <= 5, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.IdlePromoteThreshold"), "60%", $"{settings.IdlePromoteThreshold}%",
            settings.IdlePromoteThreshold >= 100 ? loc.Get("PowerDetail.IdlePromoteThresholdDescHigh") : loc.Get("PowerDetail.IdlePromoteThresholdDescStd"),
            settings.IdlePromoteThreshold >= 80, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.PerfTimeCheck"), "15ms", $"{settings.PerfTimeCheck}ms",
            settings.PerfTimeCheck >= 1000 ? loc.Get("PowerDetail.PerfTimeCheckDescLong") : loc.Get("PowerDetail.PerfTimeCheckDescShort"),
            settings.PerfTimeCheck >= 1000, ref isFirst);
        
        // ========== Intel 大小核设置 ==========
        if (_hardware?.CpuVendor == CpuVendor.Intel && _hardware.IsIntelHybrid)
        {
            AddCategoryHeader("PowerDetail.CategoryIntelHybrid", Constants.IconGlyphs.Cpu, ThemeColors.SkyBlue);
            isFirst = true;
            
            AddSettingRow(loc.Get("PowerDetail.HeteroPolicy"), loc.Get("PowerDetail.Auto"), GetHeteroPolicyText(settings.HeteroThreadPolicy),
                loc.Get("PowerDetail.HeteroPolicyDesc"), settings.HeteroThreadPolicy == 2, ref isFirst);
            
            AddSettingRow(loc.Get("PowerDetail.Class1EffThreshold"), "10%", $"{settings.Class1EffThreshold}%",
                settings.Class1EffThreshold <= 5 ? loc.Get("PowerDetail.Class1EffThresholdDescFast") : loc.Get("PowerDetail.Class1EffThresholdDescStd"),
                settings.Class1EffThreshold <= 5, ref isFirst);
            
            AddSettingRow(loc.Get("PowerDetail.Class1PerfLevel"), "100%", $"{settings.Class1PerfLevel}%",
                loc.Get("PowerDetail.Class1PerfLevelDesc"), false, ref isFirst);
            
            AddSettingRow(loc.Get("PowerDetail.Class0EffThreshold"), "10%", $"{settings.Class0EffThreshold}%",
                settings.Class0EffThreshold >= 50 ? loc.Get("PowerDetail.Class0EffThresholdDescLess") : loc.Get("PowerDetail.Class0EffThresholdDescStd"),
                settings.Class0EffThreshold >= 50, ref isFirst);
            
            AddSettingRow(loc.Get("PowerDetail.Class0PerfLevel"), "100%", $"{settings.Class0PerfLevel}%",
                loc.Get("PowerDetail.Class0PerfLevelDesc"), false, ref isFirst);
        }
        
        // ========== AMD 设置 ==========
        if (_hardware?.CpuVendor == CpuVendor.AMD)
        {
            AddCategoryHeader("PowerDetail.CategoryAmd", Constants.IconGlyphs.Cpu, ThemeColors.Danger);
            isFirst = true;
            
            AddSettingRow(loc.Get("PowerDetail.HeteroPolicy"), loc.Get("PowerDetail.Auto"), GetHeteroPolicyText(settings.HeteroThreadPolicy),
                settings.HeteroThreadPolicy == 0 ? loc.Get("PowerDetail.AmdHeteroDesc") : loc.Get("PowerDetail.AmdHeteroDescAuto"),
                settings.HeteroThreadPolicy == 0, ref isFirst);
        }
        
        // ========== 系统设置 ==========
        AddCategoryHeader("PowerDetail.CategorySystem", Constants.IconGlyphs.Desktop, ThemeColors.Success);
        isFirst = true;
        
        AddSettingRow(loc.Get("PowerDetail.DiskSleep"), loc.Get("PowerDetail.Minutes20"), loc.Get("PowerDetail.Disabled"),
            loc.Get("PowerDetail.DiskSleepDesc"), true, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.SystemSleep"), loc.Get("PowerDetail.Minutes30"), loc.Get("PowerDetail.Disabled"),
            loc.Get("PowerDetail.SystemSleepDesc"), true, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.Hibernate"), loc.Get("PowerDetail.Minutes180"), loc.Get("PowerDetail.Disabled"),
            loc.Get("PowerDetail.HibernateDesc"), true, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.HybridSleep"), loc.Get("PowerDetail.Enabled"), loc.Get("PowerDetail.Disabled"),
            loc.Get("PowerDetail.HybridSleepDesc"), false, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.WakeTimers"), loc.Get("PowerDetail.Enabled"), loc.Get("PowerDetail.Disabled"),
            loc.Get("PowerDetail.WakeTimersDesc"), false, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.CoolingPolicy"), loc.Get("PowerDetail.Active"), loc.Get("PowerDetail.Active"),
            loc.Get("PowerDetail.CoolingPolicyDesc"), false, ref isFirst);
        
        AddSettingRow(loc.Get("PowerDetail.UsbLinkPower"), loc.Get("PowerDetail.Enabled"), settings.DisableUsbLinkPower ? loc.Get("PowerDetail.Disabled") : loc.Get("PowerDetail.Enabled"),
            settings.DisableUsbLinkPower ? loc.Get("PowerDetail.UsbLinkPowerDescOff") : loc.Get("PowerDetail.UsbLinkPowerDescOn"),
            settings.DisableUsbLinkPower, ref isFirst);
        
        // ========== 显示设置 ==========
        AddCategoryHeader("PowerDetail.CategoryDisplay", Constants.IconGlyphs.Monitor, ThemeColors.Warning);
        isFirst = true;
        
        AddSettingRow(loc.Get("PowerDetail.DisplayOff"), loc.Get("PowerDetail.Minutes10"), loc.Get("PowerDetail.Disabled"),
            loc.Get("PowerDetail.DisplayOffDesc"), true, ref isFirst);
        
        // ========== 网络设置 ==========
        AddCategoryHeader("PowerDetail.CategoryNetwork", Constants.IconGlyphs.Network, ThemeColors.CyanLight);
        isFirst = true;
        
        AddSettingRow(loc.Get("PowerDetail.NetworkPowerSave"), loc.Get("PowerDetail.Enabled"), loc.Get("PowerDetail.Disabled"),
            loc.Get("PowerDetail.NetworkPowerSaveDesc"), true, ref isFirst);
        
        // ========== 其他设置 ==========
        AddCategoryHeader("PowerDetail.CategoryOther", Constants.IconGlyphs.Package, ThemeColors.TextMuted);
        isFirst = true;
        
        AddSettingRow(loc.Get("PowerDetail.GpuPowerPolicy"), loc.Get("PowerDetail.Auto"), loc.Get("PowerDetail.MaxPerformance"),
            loc.Get("PowerDetail.GpuPowerPolicyDesc"), true, ref isFirst);
    }
    
    private void AddSettingRow(string name, string defaultValue, string optimizedValue, string effect, bool isImportant, ref bool isFirst)
    {
        var row = CreateSettingRow(name, defaultValue, optimizedValue, effect, isImportant, isFirst);
        SettingsCompareList.Children.Add(row);
        isFirst = false;
    }

    private Border CreateSettingRow(string name, string defaultValue, string optimizedValue, string effect, bool isImportant, bool isFirst)
    {
        var border = new Border
        {
            Padding = new Thickness(14, 12, 14, 12),
            BorderBrush = ThemeColors.WhiteAlpha10Brush,
            BorderThickness = isFirst ? new Thickness(0) : new Thickness(0, 1, 0, 0)
        };
        
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        
        // 设置名称和效果
        var nameStack = new StackPanel();
        var nameText = new TextBlock
        {
            Text = name,
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = isImportant ? FontWeights.Medium : FontWeights.Normal
        };
        var effectText = new TextBlock
        {
            Text = effect,
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        effectText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextMuted");
        nameStack.Children.Add(nameText);
        nameStack.Children.Add(effectText);
        Grid.SetColumn(nameStack, 0);
        grid.Children.Add(nameStack);
        
        // 默认值
        var defaultText = new TextBlock
        {
            Text = defaultValue,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        defaultText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextDisabled");
        Grid.SetColumn(defaultText, 1);
        grid.Children.Add(defaultText);
        
        // 箭头
        var arrow = new TextBlock
        {
            Text = "→",
            Foreground = ThemeColors.CyanBrush,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);
        
        // 优化后值
        var optimizedText = new TextBlock
        {
            Text = optimizedValue,
            Foreground = isImportant 
                ? ThemeColors.CyanBrush
                : Brushes.White,
            FontSize = 12,
            FontWeight = isImportant ? FontWeights.SemiBold : FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(optimizedText, 3);
        grid.Children.Add(optimizedText);
        
        border.Child = grid;
        return border;
    }

    private void BuildPerformanceGainText(OptimalPowerSettings settings)
    {
        var loc = LocalizationService.Instance;
        var gains = new List<string>();
        
        if (settings.MinProcessorState >= 100)
            gains.Add(loc.Get("PowerDetail.PerfGainHighFreq"));
        if (settings.CoreParkingMin >= 100)
            gains.Add(loc.Get("PowerDetail.PerfGainAllCores"));
        if (settings.PerfIncreaseThreshold <= 5)
            gains.Add(loc.Get("PowerDetail.PerfGainFastResponse"));
        if (settings.DisableAspm)
            gains.Add(loc.Get("PowerDetail.PerfGainLowLatency"));
        if (settings.DisableUsbSuspend)
            gains.Add(loc.Get("PowerDetail.PerfGainUsbFast"));
        if (settings.BoostMode >= 2)
            gains.Add(loc.Get("PowerDetail.PerfGainBoost"));
        
        TxtPerformanceGain.Text = gains.Count > 0 
            ? string.Join("\n", gains)
            : loc.Get("PowerDetail.PerfGainBalanced");
    }

    private void BuildRisksText(OptimalPowerSettings settings)
    {
        var loc = LocalizationService.Instance;
        var risks = new List<string>();
        
        risks.Add(loc.Get("PowerDetail.SafeTempThreshold", settings.SafeTempThreshold));
        risks.Add(loc.Get("PowerDetail.CriticalTempThreshold", settings.CriticalTempThreshold));
        
        if (settings.MinProcessorState >= 100)
            risks.Add(loc.Get("PowerDetail.RiskHighFreq"));
        if (settings.DisableThrottle)
            risks.Add(loc.Get("PowerDetail.RiskNoThrottle"));
        if (settings.CoreParkingMin >= 100)
            risks.Add(loc.Get("PowerDetail.RiskAllCores"));
        
        if (settings.EstimatedTdpIncrease > 0)
            risks.Add(loc.Get("PowerDetail.RiskTdpIncrease", settings.EstimatedTdpIncrease));
        
        TxtRisks.Text = string.Join("\n", risks);
        TxtPowerSupplyNote.Text = settings.PowerSupplyNote;
    }

    private void BuildRecommendationsText(OptimalPowerSettings settings)
    {
        var loc = LocalizationService.Instance;
        var recommendations = new List<string>(settings.Recommendations);
        
        if (recommendations.Count == 0)
        {
            recommendations.Add(loc.Get("PowerDetail.RecommendDefault1"));
            recommendations.Add(loc.Get("PowerDetail.RecommendDefault2"));
            recommendations.Add(loc.Get("PowerDetail.RecommendDefault3"));
        }
        
        TxtRecommendations.Text = string.Join("\n", recommendations);
    }

    private string GetBoostModeText(int mode)
    {
        var loc = LocalizationService.Instance;
        return mode switch
        {
            0 => loc.Get("PowerDetail.BoostDisabled"),
            1 => loc.Get("PowerDetail.BoostStandard"),
            2 => loc.Get("PowerDetail.BoostAggressive"),
            3 => loc.Get("PowerDetail.BoostEfficient"),
            _ => loc.Get("PowerDetail.BoostStandard")
        };
    }

    private string GetHeteroPolicyText(int policy)
    {
        var loc = LocalizationService.Instance;
        return policy switch
        {
            0 => loc.Get("PowerDetail.HeteroAll"),
            1 => loc.Get("PowerDetail.HeteroEfficient"),
            2 => loc.Get("PowerDetail.HeteroPerformance"),
            3 => loc.Get("PowerDetail.HeteroAuto"),
            4 => loc.Get("PowerDetail.HeteroEfficientFirst"),
            5 => loc.Get("PowerDetail.HeteroAuto"),
            _ => loc.Get("PowerDetail.HeteroAuto")
        };
    }
}
