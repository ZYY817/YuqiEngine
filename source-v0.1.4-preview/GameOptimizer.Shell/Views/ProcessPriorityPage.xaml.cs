// File: GameOptimizer.Shell/Views/ProcessPriorityPage.xaml.cs
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.ProcessPriority;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class ProcessPriorityPage : UserControl
{
    private const string IfeoBlockedReason = "IFEO 持久优先级尚无独立 Owner、durable restore 与受控 Windows 验证，当前已零写入阻断。";
    private static bool IsIfeoBlocked => true;
    private bool _synchronizingEnableToggle;
    private readonly ProcessPriorityViewModel _viewModel;
    private readonly IRegistryPriorityService _registryService;
    private readonly LocalizationService _loc;

    public ProcessPriorityPage(IRegistryPriorityService registryService)
    {
        InitializeComponent();
        
        _registryService = registryService;
        _loc = LocalizationService.Instance;
        _viewModel = new ProcessPriorityViewModel(registryService);
        
        DataContext = _viewModel;
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loc.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
        CheckAdminPrivileges();
        _viewModel.LoadConfigs();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loc.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }

    private void UpdateLocalizedTexts()
    {
        PageTitle.Text = _loc.Get("ProcessPriority.PageTitle");
        PageSubtitle.Text = _loc.Get("ProcessPriority.PageSubtitle");
        AddGameBtnText.Text = _loc.Get("ProcessPriority.AddGame");
        TotalConfigsLabel.Text = _loc.Get("ProcessPriority.TotalConfigs");
        EnabledConfigsLabel.Text = _loc.Get("ProcessPriority.EnabledConfigs");
        LastModifiedLabel.Text = _loc.Get("ProcessPriority.LastModified");
        EmptyTitle.Text = _loc.Get("ProcessPriority.EmptyTitle");
        EmptySubtitle.Text = _loc.Get("ProcessPriority.EmptySubtitle");
        
        UpdateListItemTexts();
    }

    private void UpdateListItemTexts()
    {
        if (ConfigsListContainer.ItemsSource == null) return;

        foreach (var item in ConfigsListContainer.Items)
        {
            var container = ConfigsListContainer.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            if (container == null) continue;

            var editBtn = FindVisualChild<Button>(container, "EditButton");
            if (editBtn != null) editBtn.Content = _loc.Get("ProcessPriority.Edit");

            var deleteBtn = FindVisualChild<Button>(container, "DeleteButton");
            if (deleteBtn != null) deleteBtn.Content = _loc.Get("ProcessPriority.Delete");

            var enabledBadgeText = FindVisualChild<TextBlock>(container, "EnabledBadgeText");
            if (enabledBadgeText != null) enabledBadgeText.Text = _loc.Get("ProcessPriority.Enabled");
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T element && element.Name == name)
                return element;

            var result = FindVisualChild<T>(child, name);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// 本页阻断的真实原因是"IFEO 实现未就绪"，与提权无关。这里读进程真实提权状态，只为把话说准：
    /// 已提权时不得再让用户去"以管理员身份运行"，未提权时也要说明提权不会解除这项阻断。
    /// </summary>
    private void CheckAdminPrivileges()
    {
        ToastService.Instance.Warning(
            _loc.Get("Common.Warning"),
            BuildBlockingMessage(GameOptimizer.Core.Services.WindowsElevation.IsElevated));
    }

    /// <summary>阻断提示文案（含真实提权状态），加载提示与各操作入口共用。</summary>
    public static string BuildBlockingMessage(bool isElevated)
    {
        var elevationLine = isElevated
            ? "当前进程令牌已提权（管理员模式）：这项阻断不是权限不足，重复以管理员身份运行不会解除。"
            : "当前进程未提权，但提权也不会解除这项阻断：原因是实现未就绪，不是权限问题。";

        return $"{IfeoBlockedReason}\n\n{elevationLine}";
    }

    private void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsIfeoBlocked)
        {
            ToastService.Instance.Error(_loc.Get("Common.Error"), BuildBlockingMessage(GameOptimizer.Core.Services.WindowsElevation.IsElevated));
            return;
        }

        var dialog = new ProcessPriorityEditDialog();
        if (dialog.ShowDialog() == true && dialog.Config != null)
        {
            try
            {
                _viewModel.AddConfig(dialog.Config);
                
                ToastService.Instance.Success(
                    _loc.Get("Common.Success"),
                    _loc.Get("ProcessPriority.AddSuccess"));
            }
            catch (InvalidOperationException ex)
            {
                ToastService.Instance.Error(
                    _loc.Get("Common.Error"),
                    ex.Message);
            }
            catch (Exception)
            {
                ToastService.Instance.Error(
                    _loc.Get("Common.Error"),
                    _loc.Get("ProcessPriority.AddFailed"));
            }
        }
    }

    private void EditConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsIfeoBlocked)
        {
            ToastService.Instance.Error(_loc.Get("Common.Error"), BuildBlockingMessage(GameOptimizer.Core.Services.WindowsElevation.IsElevated));
            return;
        }

        if (sender is not Button btn || btn.Tag is not string configId)
            return;

        var config = _viewModel.Configs.FirstOrDefault(c => c.Id == configId);
        if (config == null) return;

        var editConfig = new ProcessPriorityConfig
        {
            Id = config.Id,
            GameName = config.GameName,
            ExecutableName = config.ExecutableName,
            ExecutablePath = config.ExecutablePath,
            Priority = config.Priority,
            IsEnabled = config.IsEnabled
        };

        var dialog = new ProcessPriorityEditDialog(editConfig);
        if (dialog.ShowDialog() == true && dialog.Config != null)
        {
            try
            {
                _viewModel.UpdateConfig(dialog.Config);
                
                ToastService.Instance.Success(
                    _loc.Get("Common.Success"),
                    _loc.Get("ProcessPriority.UpdateSuccess"));
            }
            catch (Exception)
            {
                ToastService.Instance.Error(
                    _loc.Get("Common.Error"),
                    _loc.Get("ProcessPriority.UpdateFailed"));
            }
        }
    }

    private void DeleteConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsIfeoBlocked)
        {
            ToastService.Instance.Error(_loc.Get("Common.Error"), BuildBlockingMessage(GameOptimizer.Core.Services.WindowsElevation.IsElevated));
            return;
        }

        if (sender is not Button btn || btn.Tag is not string configId)
            return;

        var result = MessageBox.Show(
            _loc.Get("ProcessPriority.DeleteConfirm"),
            _loc.Get("Dialog.Confirm"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                _viewModel.RemoveConfig(configId);
                
                ToastService.Instance.Success(
                    _loc.Get("Common.Success"),
                    _loc.Get("ProcessPriority.DeleteSuccess"));
            }
            catch (Exception)
            {
                ToastService.Instance.Error(
                    _loc.Get("Common.Error"),
                    _loc.Get("ProcessPriority.DeleteFailed"));
            }
        }
    }

    private void EnableToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_synchronizingEnableToggle)
            return;

        if (sender is not CheckBox checkBox || checkBox.Tag is not string configId)
            return;

        var isEnabled = checkBox.IsChecked == true;
        if (IsIfeoBlocked)
        {
            RevertEnableToggle(checkBox, !isEnabled);
            ToastService.Instance.Error(_loc.Get("Common.Error"), BuildBlockingMessage(GameOptimizer.Core.Services.WindowsElevation.IsElevated));
            return;
        }

        try
        {
            _viewModel.ToggleEnabled(configId, isEnabled);
        }
        catch (Exception ex)
        {
            RevertEnableToggle(checkBox, !isEnabled);
            ToastService.Instance.Error(
                _loc.Get("Common.Error"),
                ex.Message);
        }
    }

    private void RevertEnableToggle(CheckBox checkBox, bool previous)
    {
        _synchronizingEnableToggle = true;
        try
        {
            checkBox.IsChecked = previous;
        }
        finally
        {
            _synchronizingEnableToggle = false;
        }
    }
}
