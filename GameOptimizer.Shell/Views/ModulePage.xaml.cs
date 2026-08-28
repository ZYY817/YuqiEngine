using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class ModulePage : UserControl
{
    public ModulePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
        
        PageDiagnosticService.Log("ModulePage", $"Page Loaded, DataContext type: {DataContext?.GetType().Name ?? "null"}");
        PageDiagnosticService.Log("ModulePage", $"Visibility: {Visibility}");
        CheckDataContext();
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
        
        // 使用 VisualTreeHelper 遍历更新文本
        var textMappings = new Dictionary<string, string>
        {
            ["模块加载中..."] = loc.Get("ModulePage.Loading"),
            ["请稍候，正在加载优化模块数据"] = loc.Get("ModulePage.LoadingHint"),
            ["全部应用"] = loc.Get("ModulePage.ApplyAll"),
            ["全部恢复"] = loc.Get("ModulePage.RestoreAll"),
            ["优化项"] = loc.Get("ModulePage.Optimizations"),
            ["该模块暂无优化项"] = loc.Get("ModulePage.NoItems"),
            ["已应用"] = loc.Get("ModulePage.Applied"),
            ["项已应用"] = loc.Get("ModulePage.ItemsApplied"),
            // 英文映射
            ["Loading module..."] = loc.Get("ModulePage.Loading"),
            ["Please wait, loading optimization module data"] = loc.Get("ModulePage.LoadingHint"),
            ["Apply All"] = loc.Get("ModulePage.ApplyAll"),
            ["Restore All"] = loc.Get("ModulePage.RestoreAll"),
            ["Optimizations"] = loc.Get("ModulePage.Optimizations"),
            ["No optimization items in this module"] = loc.Get("ModulePage.NoItems"),
            ["Applied"] = loc.Get("ModulePage.Applied"),
            ["items applied"] = loc.Get("ModulePage.ItemsApplied"),
        };
        
        UpdateTextBlocksRecursive(this, textMappings);
        UpdateButtonsRecursive(this, textMappings);
    }
    
    private void UpdateTextBlocksRecursive(DependencyObject parent, Dictionary<string, string> mappings)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
            {
                if (mappings.TryGetValue(tb.Text, out var newText))
                {
                    tb.Text = newText;
                }
            }
            
            UpdateTextBlocksRecursive(child, mappings);
        }
    }
    
    private void UpdateButtonsRecursive(DependencyObject parent, Dictionary<string, string> mappings)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is Button btn && btn.Content is string content)
            {
                if (mappings.TryGetValue(content, out var newText))
                {
                    btn.Content = newText;
                }
            }
            
            UpdateButtonsRecursive(child, mappings);
        }
    }
    
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        PageDiagnosticService.Log("ModulePage", $"DataContext changed: {e.NewValue?.GetType().Name ?? "null"}");
        
        if (e.OldValue is DashboardViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        
        if (e.NewValue is DashboardViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            PageDiagnosticService.Log("ModulePage", $"Subscribed to PropertyChanged, HasModuleSelected: {newVm.HasModuleSelected}");
        }
        
        CheckDataContext();
    }
    
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.SelectedOptimizationModule) ||
            e.PropertyName == nameof(DashboardViewModel.HasModuleSelected))
        {
            if (sender is DashboardViewModel vm)
            {
                PageDiagnosticService.Log("ModulePage", $"Property changed: {e.PropertyName}, HasModuleSelected: {vm.HasModuleSelected}, Visibility: {Visibility}");
                if (vm.SelectedOptimizationModule != null)
                {
                    PageDiagnosticService.Log("ModulePage", $"SelectedModule: {vm.SelectedOptimizationModule.Key} - {vm.SelectedOptimizationModule.Toggles.Count} toggles");
                }
            }
        }
    }
    
    private void CheckDataContext()
    {
        if (DataContext is DashboardViewModel vm)
        {
            PageDiagnosticService.Log("ModulePage", $"ViewModel found, HasModuleSelected: {vm.HasModuleSelected}, Visibility: {Visibility}");
            if (vm.SelectedOptimizationModule != null)
            {
                PageDiagnosticService.Log("ModulePage", $"SelectedModule: {vm.SelectedOptimizationModule.Key} - {vm.SelectedOptimizationModule.Toggles.Count} toggles");
            }
            else
            {
                PageDiagnosticService.Log("ModulePage", "SelectedOptimizationModule is NULL");
            }
        }
        else
        {
            PageDiagnosticService.Log("ModulePage", $"DataContext is NOT DashboardViewModel, actual: {DataContext?.GetType().Name ?? "null"}");
        }
    }

    /// <summary>
    /// 撤销操作前的确认弹窗（通过命令拦截实现）
    /// </summary>
    public static bool ConfirmRestore(string moduleName, int count)
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        return ConfirmDialog.Show(
            System.Windows.Application.Current.MainWindow,
            loc.Get("ModulePage.ConfirmRestoreTitle"),
            loc.Get("ModulePage.ConfirmRestore", moduleName, count),
            Constants.IconGlyphs.Question);
    }

    public static bool ConfirmRestoreBaseline(string moduleName)
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        return ConfirmDialog.Show(
            System.Windows.Application.Current.MainWindow,
            loc.Get("ModulePage.ConfirmRestoreBaselineTitle"),
            loc.Get("ModulePage.ConfirmRestoreBaseline", moduleName),
            Constants.IconGlyphs.Question);
    }

    public static bool ConfirmRestoreWindowsDefault(string moduleName)
    {
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        return ConfirmDialog.Show(
            System.Windows.Application.Current.MainWindow,
            loc.Get("ModulePage.ConfirmRestoreWindowsTitle"),
            loc.Get("ModulePage.ConfirmRestoreWindows", moduleName),
            Constants.IconGlyphs.Warning);
    }
}
