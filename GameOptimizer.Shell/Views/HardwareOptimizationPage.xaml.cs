using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameOptimizer.Core.Models;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Shell.Views;

public partial class HardwareOptimizationPage : UserControl
{
    private readonly DeviceManagerService _deviceManager;
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private HardwareInfo? _hardwareInfo;
    
    // 设备信息缓存
    private DeviceInfo? _hpetDevice;
    private DeviceInfo? _systemTimerDevice;
    private DeviceInfo? _umBusDevice;
    private DeviceInfo? _compositeBusDevice;

    public HardwareOptimizationPage(IHardwareDetectionService hardwareDetectionService)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        InitializeComponent();
        _deviceManager = new DeviceManagerService();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await DetectHardwareAsync();
        await DetectDevicesAsync();
    }

    private async Task DetectHardwareAsync()
    {
        try
        {
            _hardwareInfo = await _hardwareDetectionService.GetOrDetectAsync();
            
            // 更新 UI
            TxtCpuName.Text = _hardwareInfo.CpuName;
            TxtCpuVendor.Text = _hardwareInfo.GetCpuVendorDisplayName();
            TxtGpuName.Text = _hardwareInfo.GpuName;
            TxtGpuVendor.Text = _hardwareInfo.GetGpuVendorDisplayName();

            // 根据 CPU 厂商更新推荐
            UpdateRecommendations();

            // XMP/EXPO 状态
            if (_hardwareInfo.IsXmpEnabled == false)
            {
                XmpWarningPanel.Visibility = Visibility.Visible;
                TxtXmpStatus.Text = _hardwareInfo.GetXmpStatusDescription();
            }

            // AMD X3D 专属提示
            if (_hardwareInfo.IsAmdX3D)
            {
                X3DWarningPanel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareOptPage] DetectHardware error: {ex.Message}");
        }
    }

    private void UpdateRecommendations()
    {
        if (_hardwareInfo == null) return;

        var isAmd = _hardwareInfo.CpuVendor == CpuVendor.AMD;
        var isIntel = _hardwareInfo.CpuVendor == CpuVendor.Intel;

        // HPET 推荐
        if (isAmd)
        {
            TxtHpetDesc.Text = "✅ AMD CPU 建议禁用 HPET 以降低系统延迟";
            BadgeHpet.Style = (Style)Resources["SafeBadgeStyle"];
            TxtHpetBadge.Text = "推荐禁用";
            TxtHpetBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
        }
        else if (isIntel)
        {
            TxtHpetDesc.Text = "❌ Intel CPU 不建议禁用 HPET，可能导致系统不稳定";
            BadgeHpet.Style = (Style)Resources["DangerBadgeStyle"];
            TxtHpetBadge.Text = "不推荐";
            TxtHpetBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
        }

        // 系统计时器推荐
        if (isAmd)
        {
            TxtSystemTimerDesc.Text = "✅ AMD CPU 可以禁用系统计时器";
            BadgeSystemTimer.Style = (Style)Resources["SafeBadgeStyle"];
            TxtSystemTimerBadge.Text = "可禁用";
            TxtSystemTimerBadge.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
        }
        else if (isIntel)
        {
            TxtSystemTimerDesc.Text = "⚠️ Intel CPU 禁用系统计时器需谨慎，可能影响部分应用";
            BadgeSystemTimer.Style = (Style)Resources["WarningBadgeStyle"];
            TxtSystemTimerBadge.Text = "谨慎";
            TxtSystemTimerBadge.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x00));
        }
    }

    private async Task DetectDevicesAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                _hpetDevice = _deviceManager.FindDevice("High precision event timer");
                _systemTimerDevice = _deviceManager.FindDevice("System timer");
                _umBusDevice = _deviceManager.FindDevice("UMBus Root Bus Enumerator");
                _compositeBusDevice = _deviceManager.FindDevice("Composite Bus Enumerator");
            });

            // 更新 UI
            UpdateDeviceStatus(TxtHpetStatus, BtnHpetToggle, _hpetDevice);
            UpdateDeviceStatus(TxtSystemTimerStatus, BtnSystemTimerToggle, _systemTimerDevice);
            UpdateDeviceStatus(TxtUMBusStatus, BtnUMBusToggle, _umBusDevice);
            UpdateDeviceStatus(TxtCompositeBusStatus, BtnCompositeBusToggle, _compositeBusDevice);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareOptPage] DetectDevices error: {ex.Message}");
        }
    }

    private static void UpdateDeviceStatus(TextBlock statusText, Button toggleBtn, DeviceInfo? device)
    {
        if (device == null)
        {
            statusText.Text = "状态：未找到设备";
            toggleBtn.IsEnabled = false;
            toggleBtn.Content = "不可用";
            return;
        }

        if (device.IsEnabled)
        {
            statusText.Text = "状态：已启用";
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
            toggleBtn.Content = "禁用";
        }
        else
        {
            statusText.Text = "状态：已禁用";
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x00));
            toggleBtn.Content = "启用";
        }
    }

    private async void BtnHpetToggle_Click(object sender, RoutedEventArgs e)
    {
        await ToggleDeviceAsync(_hpetDevice, "高精度事件计时器 (HPET)", TxtHpetStatus, BtnHpetToggle);
    }

    private async void BtnSystemTimerToggle_Click(object sender, RoutedEventArgs e)
    {
        await ToggleDeviceAsync(_systemTimerDevice, "系统计时器", TxtSystemTimerStatus, BtnSystemTimerToggle);
    }

    private async void BtnUMBusToggle_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "⚠️ 警告：禁用 UMBus 根总线枚举器可能导致：\n\n" +
            "• 部分 USB 设备无法识别\n" +
            "• 虚拟设备功能异常\n" +
            "• 某些驱动程序无法正常工作\n\n" +
            "确定要继续吗？",
            "高风险操作确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;
        await ToggleDeviceAsync(_umBusDevice, "UMBus 根总线枚举器", TxtUMBusStatus, BtnUMBusToggle);
    }

    private async void BtnCompositeBusToggle_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "⚠️ 警告：禁用复合总线枚举器可能导致：\n\n" +
            "• 带麦克风的耳机无法正常工作\n" +
            "• 多功能 USB 设备功能受限\n" +
            "• 复合 HID 设备异常\n\n" +
            "确定要继续吗？",
            "高风险操作确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;
        await ToggleDeviceAsync(_compositeBusDevice, "复合总线枚举器", TxtCompositeBusStatus, BtnCompositeBusToggle);
    }

    private async Task ToggleDeviceAsync(DeviceInfo? device, string deviceName, TextBlock statusText, Button toggleBtn)
    {
        if (device == null)
        {
            MessageBox.Show($"未找到 {deviceName} 设备", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var isCurrentlyEnabled = device.IsEnabled;
        var action = isCurrentlyEnabled ? "禁用" : "启用";

        toggleBtn.IsEnabled = false;
        toggleBtn.Content = $"{action}中...";

        try
        {
            var success = await Task.Run(() =>
                isCurrentlyEnabled
                    ? _deviceManager.DisableDevice(device.DeviceId)
                    : _deviceManager.EnableDevice(device.DeviceId));

            if (success)
            {
                // 刷新设备状态
                await DetectDevicesAsync();
                MessageBox.Show(
                    $"{deviceName} 已{action}\n\n部分更改可能需要重启电脑才能生效。",
                    "操作成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"{action} {deviceName} 失败\n\n可能原因：\n• 需要管理员权限\n• 设备被系统保护\n• 驱动程序限制",
                    "操作失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                toggleBtn.IsEnabled = true;
                toggleBtn.Content = action;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareOptPage] ToggleDevice error: {ex.Message}");
            MessageBox.Show($"操作失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            toggleBtn.IsEnabled = true;
            toggleBtn.Content = action;
        }
    }
}
