// File: GameOptimizer.Shell/Views/CoreAcceleratorPage.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class CoreAcceleratorPage : UserControl
{
    private bool _isInitialized;
    
    public CoreAcceleratorPage(CoreAcceleratorViewModel viewModel)
    {
        PageDiagnosticService.Log("CoreAcceleratorPage", "Constructor called");
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContext = viewModel;
        _isInitialized = true;
    }
    
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            UpdateLocalizedTexts();
            
            if (!_isInitialized) throw new InvalidOperationException("CoreAcceleratorPage requires DI construction.");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("CoreAcceleratorPage", "OnLoaded failed", ex);
        }
    }
    
    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

        try
        {
            if (DataContext is CoreAcceleratorViewModel vm)
            {
                await vm.DeactivateAsync();
            }
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("CoreAcceleratorPage", "Receipt restore on unload failed", ex);
        }
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        PageTitle.Text = loc.Get("CoreAccel.Title");
        PageSubtitle.Text = loc.Get("CoreAccel.Subtitle");
        TargetProgramLabel.Text = loc.Get("CoreAccel.TargetProgram");
        RefreshBtnText.Text = loc.Get("CoreAccel.Refresh");
        CoreTopologyTitle.Text = loc.Get("CoreAccel.CoreTopology");
        ClickToSelectHint.Text = loc.Get("CoreAccel.ClickToSelect");
        SelectedLabel.Text = loc.Get("CoreAccel.Selected") + " ";
        SelectAllBtn.Text = loc.Get("CoreAccel.SelectAll");
        ClearAllBtn.Text = loc.Get("CoreAccel.ClearAll");
        InvertBtn.Text = loc.Get("CoreAccel.Invert");
        QuickPresetTitle.Text = loc.Get("CoreAccel.QuickPreset");
        SaveConfigBtn.Text = loc.Get("CoreAccel.SaveCurrentConfig");
        PreferredCoreTitle.Text = loc.Get("CoreAccel.PreferredCoreLabel");
        PreferredCoreDesc.Text = loc.Get("CoreAccel.PreferredCoreHint");
        StopBtnText.Text = loc.Get("CoreAccel.StopBtn");
        StartBtnText.Text = loc.Get("CoreAccel.StartAccel");
    }
}
