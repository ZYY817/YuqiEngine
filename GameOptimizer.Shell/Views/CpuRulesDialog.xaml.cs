using System.Collections.Generic;
using System.Windows;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class CpuRulesDialog : Window
{
    public CpuRulesDialog()
    {
        InitializeComponent();
        UpdateLocalizedTexts();
        LoadRules();
        KeyDown += Window_KeyDown;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(() => { UpdateLocalizedTexts(); LoadRules(); });
    }
    
    protected override void OnClosed(System.EventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        base.OnClosed(e);
    }
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape || e.Key == System.Windows.Input.Key.Enter)
        {
            Close();
            e.Handled = true;
        }
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        DialogTitleText.Text = loc.Get("CpuRules.Title");
        DescText.Text = loc.Get("CpuRules.Description");
        CloseBtn.Content = loc.Get("Common.Close");
    }
    
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void LoadRules()
    {
        var loc = LocalizationService.Instance;
        var rules = new List<CpuRuleDisplay>
        {
            new(loc.Get("CpuRules.Intel14thName"), "intel_14th_gen", "🔷", 
                loc.Get("CpuRules.Intel14thDesc"), "100%", "100%", "100C"),
            new(loc.Get("CpuRules.Intel13thName"), "intel_13th_gen", "", 
                loc.Get("CpuRules.Intel13thDesc"), "100%", "100%", "100C"),
            new(loc.Get("CpuRules.Intel12thName"), "intel_12th_gen", "", 
                loc.Get("CpuRules.Intel12thDesc"), "100%", "100%", "100C"),
            new(loc.Get("CpuRules.Intel11thName"), "intel_11th_gen", "", 
                loc.Get("CpuRules.Intel11thDesc"), "100%", "100%", "95C"),
            new(loc.Get("CpuRules.Intel10thName"), "intel_10th_gen", "", 
                loc.Get("CpuRules.Intel10thDesc"), "100%", "100%", "90C"),
            new(loc.Get("CpuRules.IntelUltraName"), "intel_core_ultra", "", 
                loc.Get("CpuRules.IntelUltraDesc"), "80%", "100%", "95C"),
            new(loc.Get("CpuRules.IntelMobileName"), "intel_mobile_uy", "", 
                loc.Get("CpuRules.IntelMobileDesc"), "50%", "80%", "85C"),
            new(loc.Get("CpuRules.AmdRyzen9000Name"), "amd_ryzen_9000", "", 
                loc.Get("CpuRules.AmdRyzen9000Desc"), "100%", "100%", "95C"),
            new(loc.Get("CpuRules.AmdRyzen7000Name"), "amd_ryzen_7000", "", 
                loc.Get("CpuRules.AmdRyzen7000Desc"), "100%", "100%", "95C"),
            new(loc.Get("CpuRules.AmdRyzen5000Name"), "amd_ryzen_5000", "", 
                loc.Get("CpuRules.AmdRyzen5000Desc"), "100%", "100%", "90C"),
            new(loc.Get("CpuRules.AmdX3dName"), "amd_x3d", "", 
                loc.Get("CpuRules.AmdX3dDesc"), "100%", "100%", "89C"),
            new(loc.Get("CpuRules.AmdRyzen3000Name"), "amd_ryzen_3000", "", 
                loc.Get("CpuRules.AmdRyzen3000Desc"), "100%", "100%", "85C"),
            new(loc.Get("CpuRules.AmdApuName"), "amd_apu", "", 
                loc.Get("CpuRules.AmdApuDesc"), "80%", "100%", "85C"),
            new(loc.Get("CpuRules.LowCoreCount"), "low_core_count", "", 
                loc.Get("CpuRules.LowCoreCountDesc"), "100%", "100%", "85C"),
            new(loc.Get("CpuRules.Unknown"), "unknown_safe", "", 
                loc.Get("CpuRules.UnknownDesc"), "80%", "80%", "80C"),
        };
        
        RulesList.ItemsSource = rules;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public record CpuRuleDisplay(
    string Name, 
    string RuleId, 
    string Icon, 
    string Description, 
    string MinState, 
    string CoreParking, 
    string SafeTemp);