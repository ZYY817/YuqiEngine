// File: GameOptimizer.Shell/Views/EsportsModeApplyConfirmDialog.xaml.cs
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Shell.Models;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Views;

public partial class EsportsModeApplyConfirmDialog : Window
{
    private readonly ApplyConfirmInfo _confirmInfo;
    private bool _result;
    
    public EsportsModeApplyConfirmDialog(ApplyConfirmInfo confirmInfo)
    {
        InitializeComponent();
        _confirmInfo = confirmInfo;
        InitializeUI();
        UpdateLocalizedTexts();
        
        // 订阅语言变化事件
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Unloaded += (s, e) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string newLanguage)
    {
        Dispatcher.Invoke(UpdateLocalizedTexts);
    }
    
    public static bool? Show(Window owner, ApplyConfirmInfo confirmInfo)
    {
        var dialog = new EsportsModeApplyConfirmDialog(confirmInfo)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog._result;
    }
    
    private void InitializeUI()
    {
        var loc = LocalizationService.Instance;
        
        StrategyText.Text = _confirmInfo.StrategyName;
        TotalCountText.Text = _confirmInfo.TotalCount.ToString();
        
        SafeCountText.Text = $"{_confirmInfo.SafeCount} {loc.Get("Common.Items") ?? "项"}";
        BalancedCountText.Text = $"{_confirmInfo.BalancedCount} {loc.Get("Common.Items") ?? "项"}";
        ExtremeCountText.Text = $"{_confirmInfo.ExtremeCount} {loc.Get("Common.Items") ?? "项"}";
        
        if (_confirmInfo.ExtremeCount > 0)
        {
            WarningPanel.Visibility = Visibility.Visible;
        }
        
        var categoryItems = _confirmInfo.CategoryCounts
            .Select(kvp => new CategoryCountItem
            {
                CategoryName = GetCategoryDisplayName(kvp.Key),
                Icon = Services.EsportsModeStateService.GetCategoryIcon(kvp.Key),
                Count = kvp.Value
            })
            .OrderByDescending(c => c.Count)
            .ToList();
        
        CategoryList.ItemsSource = categoryItems;
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        // 对话框标题和副标题
        TitleText.Text = loc.Get("Esports.ConfirmApply.Title") ?? "确认应用优化";
        SubtitleText.Text = loc.Get("Esports.ConfirmApply.Subtitle") ?? "请确认以下优化项";
        
        // 策略标签
        if (FindName("StrategyLabel") is TextBlock strategyLabel)
        {
            strategyLabel.Text = loc.Get("Esports.ConfirmApply.CurrentStrategy") ?? "当前策略：";
        }
        
        // 总数标签
        if (FindName("TotalCountLabel") is TextBlock totalLabel)
        {
            totalLabel.Text = loc.Get("Esports.ConfirmApply.WillApply") ?? "即将应用";
        }
        
        if (FindName("ItemsCountLabel") is TextBlock itemsLabel)
        {
            itemsLabel.Text = loc.Get("Esports.ConfirmApply.ItemsCount") ?? "项优化";
        }
        
        // 风险分组标签
        if (FindName("RiskGroupLabel") is TextBlock riskLabel)
        {
            riskLabel.Text = loc.Get("Esports.ConfirmApply.RiskGrouping") ?? "按风险等级分组";
        }
        
        if (FindName("SafeLabel") is TextBlock safeLabel)
        {
            safeLabel.Text = loc.Get("Esports.ConfirmApply.SafeOptimizations") ?? "安全优化";
        }
        
        if (FindName("BalancedLabel") is TextBlock balancedLabel)
        {
            balancedLabel.Text = loc.Get("Esports.ConfirmApply.BalancedOptimizations") ?? "平衡优化";
        }
        
        if (FindName("ExtremeLabel") is TextBlock extremeLabel)
        {
            extremeLabel.Text = loc.Get("Esports.ConfirmApply.ExtremeOptimizations") ?? "极限优化";
        }
        
        // 类别分组标签
        if (FindName("CategoryGroupLabel") is TextBlock categoryLabel)
        {
            categoryLabel.Text = loc.Get("Esports.ConfirmApply.CategoryGrouping") ?? "按功能类别分组";
        }
        
        // 警告文本
        if (FindName("WarningText") is TextBlock warningText)
        {
            warningText.Text = loc.Get("Esports.ConfirmApply.ExtremeWarning") ?? "注意：极限优化可能影响系统稳定性，建议在了解风险后使用";
        }
        
        // 按钮文本
        CancelBtn.Content = loc.Get("Common.Cancel") ?? "取消";
        ConfirmBtn.Content = loc.Get("Esports.ConfirmApply.ConfirmButton") ?? "确定应用";
        
        // 更新计数文本（保持数字不变，只更新单位）
        SafeCountText.Text = $"{_confirmInfo.SafeCount} {loc.Get("Common.Items") ?? "项"}";
        BalancedCountText.Text = $"{_confirmInfo.BalancedCount} {loc.Get("Common.Items") ?? "项"}";
        ExtremeCountText.Text = $"{_confirmInfo.ExtremeCount} {loc.Get("Common.Items") ?? "项"}";
        
        // 刷新类别列表的显示名称
        if (CategoryList.ItemsSource is List<CategoryCountItem> items)
        {
            foreach (var item in items)
            {
                var originalCategory = _confirmInfo.CategoryCounts.FirstOrDefault(kvp => kvp.Value == item.Count).Key;
                if (!string.IsNullOrEmpty(originalCategory))
                {
                    item.CategoryName = GetCategoryDisplayName(originalCategory);
                }
            }
            CategoryList.Items.Refresh();
        }
    }
    
    private string GetCategoryDisplayName(string category)
    {
        var loc = LocalizationService.Instance;
        return category switch
        {
            "FPS" => loc.Get("Esports.Category.FPS"),
            "Input" => loc.Get("Esports.Category.Input"),
            "Network" => loc.Get("Esports.Category.Network"),
            "Storage" => loc.Get("Esports.Category.Storage"),
            "Windows" => loc.Get("Esports.Category.Windows"),
            "Security" => loc.Get("Esports.Category.Security"),
            "CPU" => loc.Get("Esports.Category.CPU"),
            "GPU" => loc.Get("Esports.Category.GPU"),
            _ => category
        };
    }
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
    
    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }
    
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
    
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}

public class CategoryCountItem
{
    public string CategoryName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int Count { get; set; }
}
