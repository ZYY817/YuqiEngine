// File: GameOptimizer.Shell/Views/PowerPlanEditorPage.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Infrastructure.Windows.PowerManagement;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

/// <summary>
/// Power settings deliberately expose only workflows backed by the typed power owner.
/// Plan mutations and unmigrated setting families remain read-only instead of failing
/// after users have already made a choice.
/// </summary>
public partial class PowerPlanEditorPage : UserControl
{
    private readonly IPowerPlanEditorService _powerService;
    private readonly IPowerManagementOwner _powerOwner;
    private readonly IManagedPowerPlanService _managedPower;
    private ManagedPowerPlanOverview? _managedOverview;
    private Guid? _selectedManagedPlanId;
    private HashSet<string> _supportedManagedSettings = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRefreshing;

    public PowerPlanEditorPage(
        IPowerPlanEditorService powerService,
        IPowerManagementOwner powerOwner,
        IManagedPowerPlanService managedPower)
    {
        _powerService = powerService ?? throw new ArgumentNullException(nameof(powerService));
        _powerOwner = powerOwner ?? throw new ArgumentNullException(nameof(powerOwner));
        _managedPower = managedPower ?? throw new ArgumentNullException(nameof(managedPower));

        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        await RefreshPageAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, string language) =>
        _ = Dispatcher.InvokeAsync(RefreshPageAsync);

    private async Task RefreshPageAsync()
    {
        if (_isRefreshing)
            return;

        _isRefreshing = true;
        SetBusy(true);
        try
        {
            await LoadProfilePreflightAsync();
            await LoadPlansAndStateAsync();
            ApplyCapabilityState();
        }
        catch (Exception ex)
        {
            TxtPowerStatus.Text = "无法读取电源状态";
            TxtDisplayTimeoutState.Text = "读取失败；未执行任何电源写入。";
            TxtPlansEmpty.Text = DescribePowerException(ex);
            TxtPlansEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            SetBusy(false);
            _isRefreshing = false;
        }
    }

    private async Task LoadPlansAndStateAsync()
    {
        var plans = await Task.Run(() => _powerService.GetAllPowerPlans());
        var managedIds = _managedOverview?.Plans.Select(item => item.PlanId).ToHashSet() ?? [];
        var entries = plans.Select(plan => new PowerPlanInfo
        {
            Guid = plan.Guid,
            Name = plan.Name,
            IsActive = plan.IsActive,
            IsManaged = Guid.TryParse(plan.Guid, out var id) && managedIds.Contains(id),
            Description = Guid.TryParse(plan.Guid, out var planId) && managedIds.Contains(planId)
                ? "宇奇受管副本 · 可备份、编辑、激活与恢复"
                : GetPlanDescription(plan.Name)
        }).ToList();

        PowerPlansList.ItemsSource = entries;
        TxtPlansEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtPlansEmpty.Text = entries.Count == 0
            ? _powerService.LastBlockReason ?? "未读取到可用的 Windows 电源计划。"
            : string.Empty;

        var active = entries.FirstOrDefault(entry => entry.IsActive);
        TxtCurrentPlanName.Text = active?.Name ?? "未读取到活动计划";

        var state = await _powerOwner.InspectAsync(null, CancellationToken.None);
        TxtCurrentPlanName.Text = state.Schemes.FirstOrDefault(plan => plan.Id == state.ActiveSchemeId)?.Name
            ?? TxtCurrentPlanName.Text;
        TxtPowerStatus.Text = state.Environment.IsPolicyManaged
            ? "受策略管理"
            : state.Environment.IsAdministrator ? "管理员权限已就绪" : "将请求管理员确认";
        TxtDeviceTypeStatus.Text = state.Environment.HasBattery ? "笔记本 · AC/DC 同步" : "台式机 · AC 设置";

        TxtDisplayTimeoutState.Text = _managedOverview?.Plans.Count > 0
            ? "请选择一个宇奇受管计划读取高级参数；原 Windows / OEM 计划始终只读。"
            : "请先从内置策略或 Windows 计划创建受管副本。";
    }

    private async Task LoadProfilePreflightAsync()
    {
        _managedOverview = await _managedPower.InspectAsync(CancellationToken.None);
        ProfileCards.ItemsSource = _managedOverview.Profiles.Select(candidate => new PowerProfileCard(candidate)).ToArray();
        ManagedPlanSelector.ItemsSource = _managedOverview.Plans;
        AdvancedEditorPanel.Visibility = _managedOverview.Plans.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtNoManagedPlan.Visibility = _managedOverview.Plans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_selectedManagedPlanId.HasValue)
            ManagedPlanSelector.SelectedItem = _managedOverview.Plans.FirstOrDefault(item => item.PlanId == _selectedManagedPlanId.Value);
        HeteroPolicyPanel.Visibility = _managedOverview.CpuArchitecture == GameOptimizer.Core.Models.CpuArchitecture.IntelHybrid
            ? Visibility.Visible : Visibility.Collapsed;
        TxtProfileStatus.Text = $"{_managedOverview.Status}。CPU：{_managedOverview.CpuVendor} / {_managedOverview.CpuArchitecture}。";
        var hasAutomaticProfile = _managedOverview.Profiles.Any(item =>
            item.IsAvailable && item.Profile.Id is ManagedPowerProfileId.IntelGaming or ManagedPowerProfileId.AmdGaming);
        BtnOpenIntelligentCreate.IsEnabled = hasAutomaticProfile;
        BtnOpenIntelligentCreate.ToolTip = hasAutomaticProfile
            ? "先预览 CPU 匹配、计划名称与将写入的真实参数；确认后才创建独立副本。"
            : "当前 CPU 或供电条件没有可用的游戏性能策略。请刷新并检查管理员权限。";
    }

    private void ApplyCapabilityState()
    {
        var canEditManagedPlan = _managedOverview is { CanMutate: true } && _selectedManagedPlanId.HasValue;
        SliderCpuMinimum.IsEnabled = canEditManagedPlan && _supportedManagedSettings.Contains("cpu.minimum");
        SliderCpuMaximum.IsEnabled = canEditManagedPlan && _supportedManagedSettings.Contains("cpu.maximum");
        SliderCpuEpp.IsEnabled = canEditManagedPlan && _supportedManagedSettings.Contains("cpu.epp");
        BoostModeSelector.IsEnabled = canEditManagedPlan && _supportedManagedSettings.Contains("cpu.boost");
        HeteroPolicySelector.IsEnabled = canEditManagedPlan && _supportedManagedSettings.Contains("cpu.hetero-policy");
        BtnApplyAll.IsEnabled = canEditManagedPlan;
        BtnRestoreDefault.IsEnabled = _managedPower is not null && _managedOverview?.Backups.Count > 0;

        if (!canEditManagedPlan)
        {
            var reason = _managedOverview?.Plans.Count > 0 ? "请先选择一个宇奇受管计划。" : _managedOverview?.Status ?? "受管电源安全链未就绪。";
            SliderCpuMinimum.ToolTip = reason;
            BtnApplyAll.ToolTip = reason;
            TxtDisplayTimeoutState.Text = reason;
        }

        TxtPowerCapabilityState.Text =
            "Windows / OEM 原计划禁止写入。预设不会关闭 C-State、P-State、核心停放或强制 P/E 核调度；手动异构调度仅在 Intel 混合架构且设置可读时开放。";
    }

    private async void BtnRefreshPlans_Click(object sender, RoutedEventArgs e) => await RefreshPageAsync();

    private async void BtnOpenIntelligentCreate_Click(object sender, RoutedEventArgs e) =>
        await OpenIntelligentCreateAsync(sourcePlanId: null);

    private async Task OpenIntelligentCreateAsync(Guid? sourcePlanId)
    {
        if (_managedPower is null)
            return;
        if (_managedOverview is null)
            await LoadProfilePreflightAsync();
        if (_managedOverview is null)
            throw new InvalidOperationException("无法读取当前设备的电源能力。");

        var dialog = new ManagedPowerPlanCreateDialog(_managedOverview)
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true || dialog.Selection is null)
            return;

        try
        {
            SetBusy(true);
            var sourceId = sourcePlanId ?? GetCurrentActivePlanId();
            var draft = await _managedPower.PrepareCreateAsync(
                sourceId, dialog.Selection.ProfileId, dialog.Selection.CustomName, CancellationToken.None);
            await ExecuteConfirmedDraftAsync(draft, "确认创建受管电源计划");
        }
        catch (InvalidDataException ex)
        {
            TxtProfileStatus.Text = ex.Message;
        }
        catch (OperationCanceledException)
        {
            TxtProfileStatus.Text = "准备已取消；未执行任何写入。";
        }
        catch (Exception ex)
        {
            TxtProfileStatus.Text = DescribePowerException(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Guid GetCurrentActivePlanId()
    {
        var active = _powerService.GetActivePowerPlan();
        if (active is null || !Guid.TryParse(active.Guid, out var sourceId))
            throw new InvalidOperationException("无法读取当前活动电源计划。");
        return sourceId;
    }

    private async void BtnApplyAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_managedPower is null || !_selectedManagedPlanId.HasValue)
                throw new InvalidOperationException("请先选择一个宇奇受管计划。");
            var values = new List<ManagedPowerSettingValue>();
            if (_supportedManagedSettings.Contains("cpu.minimum")) values.Add(new("cpu.minimum", (int)Math.Round(SliderCpuMinimum.Value), null));
            if (_supportedManagedSettings.Contains("cpu.maximum")) values.Add(new("cpu.maximum", (int)Math.Round(SliderCpuMaximum.Value), null));
            if (_supportedManagedSettings.Contains("cpu.epp")) values.Add(new("cpu.epp", (int)Math.Round(SliderCpuEpp.Value), null));
            if (_supportedManagedSettings.Contains("cpu.boost") && int.TryParse(BoostModeSelector.SelectedValue?.ToString(), out var boost))
                values.Add(new("cpu.boost", boost, null));
            if (_supportedManagedSettings.Contains("cpu.hetero-policy") && int.TryParse(HeteroPolicySelector.SelectedValue?.ToString(), out var hetero))
                values.Add(new("cpu.hetero-policy", hetero, null));
            var draft = await _managedPower.PrepareUpdateAsync(_selectedManagedPlanId.Value, values, CancellationToken.None);
            await ExecuteConfirmedDraftAsync(draft, "确认保存受管高级参数");
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), "电源更改失败", DescribePowerException(ex), IconGlyphs.Warning);
        }
    }

    private async void BtnClonePlan_Click(object sender, RoutedEventArgs e)
    {
        if (_managedPower is null || sender is not Button { Tag: string value } || !Guid.TryParse(value, out var sourceId))
            return;
        try
        {
            await OpenIntelligentCreateAsync(sourceId);
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), "创建受管副本失败", DescribePowerException(ex), IconGlyphs.Warning);
        }
    }

    private async void BtnSelectManagedPlan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } || !Guid.TryParse(value, out var planId))
            return;
        _selectedManagedPlanId = planId;
        if (_managedOverview is not null)
            ManagedPlanSelector.SelectedItem = _managedOverview.Plans.FirstOrDefault(item => item.PlanId == planId);
        await LoadSelectedManagedSettingsAsync();
    }

    private async void ManagedPlanSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ManagedPlanSelector.SelectedItem is ManagedPowerPlanRecord plan)
        {
            _selectedManagedPlanId = plan.PlanId;
            await LoadSelectedManagedSettingsAsync();
        }
    }

    private async void BtnActivateManagedPlan_Click(object sender, RoutedEventArgs e)
    {
        if (_managedPower is null || sender is not Button { Tag: string value } || !Guid.TryParse(value, out var planId))
            return;
        try
        {
            var draft = await _managedPower.PrepareActivateAsync(planId, CancellationToken.None);
            await ExecuteConfirmedDraftAsync(draft, "确认激活受管电源计划");
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), "激活失败", DescribePowerException(ex), IconGlyphs.Warning);
        }
    }

    private async void BtnDeleteManagedPlan_Click(object sender, RoutedEventArgs e)
    {
        if (_managedPower is null || sender is not Button { Tag: string value } || !Guid.TryParse(value, out var planId))
            return;
        try
        {
            var draft = await _managedPower.PrepareDeleteAsync(planId, CancellationToken.None);
            await ExecuteConfirmedDraftAsync(draft, "确认删除受管电源计划");
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), "删除受管计划失败", DescribePowerException(ex), IconGlyphs.Warning);
        }
    }

    private async Task LoadSelectedManagedSettingsAsync()
    {
        if (_managedPower is null || !_selectedManagedPlanId.HasValue)
            return;
        try
        {
            var values = await _managedPower.ReadSettingsAsync(_selectedManagedPlanId.Value, CancellationToken.None);
            _supportedManagedSettings = values.Select(item => item.SettingId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SetSliderValue(SliderCpuMinimum, values, "cpu.minimum");
            SetSliderValue(SliderCpuMaximum, values, "cpu.maximum");
            SetSliderValue(SliderCpuEpp, values, "cpu.epp");
            SetComboValue(BoostModeSelector, values, "cpu.boost");
            SetComboValue(HeteroPolicySelector, values, "cpu.hetero-policy");
            SliderCpuMinimum.IsEnabled = _supportedManagedSettings.Contains("cpu.minimum");
            SliderCpuMaximum.IsEnabled = _supportedManagedSettings.Contains("cpu.maximum");
            SliderCpuEpp.IsEnabled = _supportedManagedSettings.Contains("cpu.epp");
            BoostModeSelector.IsEnabled = _supportedManagedSettings.Contains("cpu.boost");
            HeteroPolicyPanel.Visibility = _supportedManagedSettings.Contains("cpu.hetero-policy") ? Visibility.Visible : Visibility.Collapsed;
            TxtDisplayTimeoutState.Text = $"已从受管计划读取 {_supportedManagedSettings.Count} 项支持参数；保存只更新 AC，DC 保持当前值。";
            ApplyCapabilityState();
        }
        catch (Exception ex)
        {
            TxtDisplayTimeoutState.Text = DescribePowerException(ex);
        }
    }

    private async void BtnRestoreDefault_Click(object sender, RoutedEventArgs e)
    {
        if (_managedPower is null || _managedOverview?.Backups.Count is not > 0)
        {
            InfoDialog.Show(Window.GetWindow(this), "恢复不可用", "没有可校验的受管电源计划备份。", IconGlyphs.Warning);
            return;
        }

        try
        {
            var backup = _managedOverview.Backups.OrderByDescending(item => item.CreatedAtUtc).First();
            var draft = await _managedPower.PrepareRestoreAsync(backup.BackupId, CancellationToken.None);
            await ExecuteConfirmedDraftAsync(draft, "确认恢复最近完整备份");
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), "电源恢复失败", DescribePowerException(ex), IconGlyphs.Warning);
        }
    }

    private async Task ExecuteConfirmedDraftAsync(ManagedPowerPlanDraft draft, string title)
    {
        if (_managedPower is null)
            throw new InvalidOperationException("受管电源安全链未连接。");
        var boundary = draft.Kind == ManagedPowerOperationKind.Delete
            ? "操作只会切回源计划并删除宇奇受管副本；Windows/OEM 原计划不会被修改，删除前会创建并校验完整备份。是否继续？"
            : "操作只允许写入宇奇受管副本，执行前会创建并校验完整备份。是否继续？";
        var parameterPreview = draft.Settings.Count == 0
            ? string.Empty
            : $"\n\n已通过当前源计划能力检测、将实际写入的参数：\n{string.Join("\n", draft.Settings.Select(DescribeManagedSetting))}";
        var message = $"{draft.Summary}{parameterPreview}\n\n风险：{draft.Risk}\n\n{boundary}";
        if (!ConfirmDialog.Show(Window.GetWindow(this), title, message, IconGlyphs.Warning))
            return;
        var result = await _managedPower.ExecuteAsync(draft, CancellationToken.None);
        if (!result.Success)
        {
            InfoDialog.Show(Window.GetWindow(this), "电源操作未完成", result.Message, IconGlyphs.Warning);
            await RefreshPageAsync();
            return;
        }
        _selectedManagedPlanId = draft.Kind == ManagedPowerOperationKind.Delete ? null : result.Plan?.PlanId ?? _selectedManagedPlanId;
        await RefreshPageAsync();
        InfoDialog.Show(Window.GetWindow(this), "电源操作已验证", result.Message, IconGlyphs.Check);
    }

    private void SetBusy(bool isBusy)
    {
        if (isBusy)
            TxtPowerStatus.Text = "正在读取真实系统状态";
    }

    private static void SetSliderValue(GameOptimizer.Shell.Controls.EnhancedSlider slider, IReadOnlyList<ManagedPowerSettingValue> values, string id)
    {
        var value = values.FirstOrDefault(item => string.Equals(item.SettingId, id, StringComparison.OrdinalIgnoreCase));
        if (value is null)
            return;
        slider.Value = value.AcValue;
        slider.SetOriginalValue(value.AcValue);
    }

    private static void SetComboValue(ComboBox selector, IReadOnlyList<ManagedPowerSettingValue> values, string id)
    {
        var value = values.FirstOrDefault(item => string.Equals(item.SettingId, id, StringComparison.OrdinalIgnoreCase));
        if (value is not null)
            selector.SelectedValue = value.AcValue.ToString();
    }

    private static string GetPlanDescription(string name)
    {
        var normalized = name.ToLowerInvariant();
        if (normalized.Contains("高性能") || normalized.Contains("high performance")) return "系统计划 · 优先性能";
        if (normalized.Contains("平衡") || normalized.Contains("balanced")) return "系统计划 · 平衡性能与能耗";
        if (normalized.Contains("节能") || normalized.Contains("power saver")) return "系统计划 · 优先节能";
        if (normalized.Contains("卓越") || normalized.Contains("ultimate")) return "系统计划 · 高性能模式";
        return "Windows 已注册的电源计划";
    }

    private static string DescribeResult(PowerWorkflowResult result) =>
        $"阶段：{result.Phase}\n状态：{result.Status}\n{result.Message}";

    private static string DescribeManagedSetting(ManagedPowerSettingValue setting)
    {
        var definition = ManagedPowerPlanCatalog.GetSetting(setting.SettingId);
        var dc = setting.DcValue.HasValue ? $"；电池 {setting.DcValue.Value}{definition.Unit}" : string.Empty;
        return $"• {definition.Name}：插电 {setting.AcValue}{definition.Unit}{dc}";
    }

    private static string DescribePowerException(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "没有足够权限读取或修改电源设置；未执行任何写入。",
        OperationCanceledException => "操作已取消；未执行或未完成任何电源写入。",
        InvalidOperationException => "当前 Windows 电源状态不满足执行条件；请刷新后重试。",
        _ => "电源服务返回异常；未能确认最终状态。请刷新页面后重试，详细信息已记录到本地日志。"
    };

    private static string FormatSeconds(uint? seconds) => seconds.HasValue
        ? $"{seconds.Value / 60d:0.#} 分钟"
        : "不适用";

    private sealed class PowerProfileCard(ManagedPowerProfileAvailability availability)
    {
        public string Name => availability.Profile.Name;
        public string Description => availability.Profile.Description;
        public string Preconditions => availability.Profile.Vendor == GameOptimizer.Core.Models.CpuVendor.Unknown
            ? "台式机 · 交流电 · 管理员权限"
            : $"{availability.Profile.Vendor} CPU · 交流电 · 设置需实际可读";
        public string ParameterSummary => $"计划调整：{string.Join(" · ", availability.Profile.Settings.Select(DescribeSetting))}";
        public string Details => availability.IsAvailable ? availability.Profile.Tradeoff : availability.Status;
        public string Status => availability.IsAvailable ? "可创建" : "已阻止";

        private static string DescribeSetting(ManagedPowerSettingValue setting)
        {
            var definition = ManagedPowerPlanCatalog.GetSetting(setting.SettingId);
            var dc = setting.DcValue.HasValue ? $" / 电池 {setting.DcValue.Value}{definition.Unit}" : string.Empty;
            return $"{definition.Name}：插电 {setting.AcValue}{definition.Unit}{dc}";
        }
    }
}
