using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Shell.Views;

public sealed record ManagedPowerPlanCreateSelection(ManagedPowerProfileId ProfileId, string? CustomName);

public partial class ManagedPowerPlanCreateDialog : Window
{
    private readonly ManagedPowerPlanOverview _overview;
    private readonly ManagedPowerProfileAvailability? _automaticProfile;
    private readonly ManagedPowerProfileAvailability? _extremeProfile;

    public ManagedPowerPlanCreateSelection? Selection { get; private set; }

    public ManagedPowerPlanCreateDialog(ManagedPowerPlanOverview overview)
    {
        _overview = overview ?? throw new ArgumentNullException(nameof(overview));
        _automaticProfile = overview.Profiles.FirstOrDefault(item =>
            item.IsAvailable && item.Profile.Id is ManagedPowerProfileId.IntelGaming or ManagedPowerProfileId.AmdGaming);
        _extremeProfile = overview.Profiles.FirstOrDefault(item => item.Profile.Id == ManagedPowerProfileId.MaximumPerformance);
        InitializeComponent();
        Loaded += (_, _) => LoadPreview();
    }

    private void LoadPreview()
    {
        DetectedEnvironmentText.Text = $"已识别：{_overview.CpuVendor} / {_overview.CpuArchitecture}";
        AvailabilityText.Text = _overview.Status;
        ExtremeModeCheckBox.IsEnabled = _extremeProfile?.IsAvailable == true;
        ExtremeModeHintText.Text = _extremeProfile?.IsAvailable == true
            ? "将改为使用极限性能受管副本；会提高待机功耗、温度和风扇噪声，不保证提升帧率。"
            : _extremeProfile?.Status ?? "当前设备未提供极限模式。";
        RefreshPreview();
    }

    private ManagedPowerProfileAvailability? ActiveProfile =>
        ExtremeModeCheckBox.IsChecked == true ? _extremeProfile : _automaticProfile;

    private void ExtremeModeChanged(object sender, RoutedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        var profile = ActiveProfile;
        if (profile is null)
        {
            MatchedProfileNameText.Text = "当前设备没有可创建的受管策略";
            MatchedProfileDescriptionText.Text = "未读取到与当前 CPU 和供电条件兼容的策略；不会创建任何计划。";
            ProfileStatusText.Text = "不可用";
            NameHintText.Text = "请刷新电源状态或检查管理员权限后重试。";
            ParameterList.ItemsSource = Array.Empty<PowerSettingPreview>();
            SafetyBoundaryText.Text = "Windows / OEM 原计划保持只读；只有通过能力检测的受管副本可被创建。";
            CreateButton.IsEnabled = false;
            return;
        }

        MatchedProfileNameText.Text = profile.Profile.Name;
        MatchedProfileDescriptionText.Text = profile.Profile.Description;
        ProfileStatusText.Text = profile.IsAvailable ? "可创建" : "不可用";
        NameHintText.Text = $"留空将自动命名为“宇奇 · {profile.Profile.Name} · 时间”；只创建副本，不改写当前 Windows / OEM 计划。";
        ParameterList.ItemsSource = profile.Profile.Settings.Select(PowerSettingPreview.From).ToArray();
        SafetyBoundaryText.Text = $"风险提示：{profile.Profile.Tradeoff}\n实际创建前会再次检测源计划支持的字段；不受支持的字段不会写入，且不会创建不完整策略。";
        CreateButton.IsEnabled = profile.IsAvailable;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var profile = ActiveProfile;
        if (profile is null || !profile.IsAvailable)
            return;
        Selection = new ManagedPowerPlanCreateSelection(profile.Profile.Id,
            string.IsNullOrWhiteSpace(PlanNameTextBox.Text) ? null : PlanNameTextBox.Text.Trim());
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private sealed record PowerSettingPreview(string Name, string Description, string ValueText)
    {
        public static PowerSettingPreview From(ManagedPowerSettingValue value)
        {
            var definition = ManagedPowerPlanCatalog.GetSetting(value.SettingId);
            var dc = value.DcValue.HasValue ? $" / 电池 {value.DcValue.Value}{definition.Unit}" : string.Empty;
            return new(definition.Name, definition.Description, $"插电 {value.AcValue}{definition.Unit}{dc}");
        }
    }
}
