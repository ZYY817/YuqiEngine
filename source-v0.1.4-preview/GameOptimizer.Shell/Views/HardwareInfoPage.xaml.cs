// File: GameOptimizer.Shell/Views/HardwareInfoPage.xaml.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Models;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Controls;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using Microsoft.Win32;

namespace GameOptimizer.Shell.Views;

public partial class HardwareInfoPage : UserControl
{
    private readonly IHardwareDetectionService _hardwareDetectionService;
    private HardwareInfo? _hardwareInfo;
    private List<DiskInfoModel>? _diskInfoList;
    private List<GpuAdapterDisplayModel>? _gpuAdapterList;
    private CancellationTokenSource? _detectionCancellation;
    private int _detectionVersion;
    private HardwarePageState _pageState = HardwarePageState.Loading;

    private enum HardwarePageState
    {
        Loading,
        Ready,
        Empty,
        Error
    }

    public HardwareInfoPage(IHardwareDetectionService hardwareDetectionService)
    {
        _hardwareDetectionService = hardwareDetectionService ?? throw new ArgumentNullException(nameof(hardwareDetectionService));
        PageDiagnosticService.Log("HardwareInfoPage", "Constructor called");
        InitializeComponent();
        Unloaded += OnUnloaded;
        SetPageState(HardwarePageState.Loading);
    }
    
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        Interlocked.Increment(ref _detectionVersion);
        var cancellation = Interlocked.Exchange(ref _detectionCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        // Why: InvokeAsync 替代 Invoke，防止后台线程死锁
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        
        // Page Header
        if (FindName("PageHeader") is PageHeader header)
        {
            header.Title = loc.Get("Hardware.Title");
            header.Subtitle = loc.Get("Hardware.Subtitle");
        }
        if (FindName("CopyConfigButtonText") is TextBlock copyText) copyText.Text = loc.Get("Hardware.CopyConfig");
        if (FindName("ExportReportButtonText") is TextBlock exportText) exportText.Text = loc.Get("Hardware.ExportReport");
        if (FindName("RefreshButtonText") is TextBlock refreshText) refreshText.Text = loc.Get("Hardware.Refresh");
        if (FindName("StatusRetryButton") is Button retryBtn) retryBtn.Content = loc.Get("Hardware.Refresh");
        
        // Section Labels
        if (FindName("OsLabel") is TextBlock osLabel) osLabel.Text = loc.Get("Hardware.OperatingSystem");
        if (FindName("UptimeLabel") is TextBlock uptimeLabel) uptimeLabel.Text = loc.Get("Hardware.Uptime");
        if (FindName("MotherboardLabel") is TextBlock mbLabel) mbLabel.Text = loc.Get("Hardware.Motherboard");
        if (FindName("ProcessorLabel") is TextBlock cpuLabel) cpuLabel.Text = loc.Get("Hardware.Processor");
        if (FindName("CoresLabel") is TextBlock coresLabel) coresLabel.Text = " " + loc.Get("Hardware.Cores");
        if (FindName("ThreadsLabel") is TextBlock threadsLabel) threadsLabel.Text = " " + loc.Get("Hardware.Threads");
        if (FindName("GraphicsLabel") is TextBlock gpuLabel) gpuLabel.Text = loc.Get("Hardware.Graphics");
        // D5: GpuBadgeText 由 UpdateUI() 根据 GPU 厂商动态设置，此处不覆盖
        if (FindName("MemoryLabel") is TextBlock memLabel) memLabel.Text = loc.Get("Hardware.Memory");
        if (FindName("StorageLabel") is TextBlock storageLabel) storageLabel.Text = loc.Get("Hardware.Storage");
        if (FindName("DiskEmptyText") is TextBlock diskEmptyText) diskEmptyText.Text = loc.Get("Hardware.NotDetected");
        
        UpdateUI();
        SetPageState(_pageState);
    }
    
    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        PageDiagnosticService.Log("HardwareInfoPage", "UserControl_Loaded - Starting hardware detection");
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        UpdateLocalizedTexts();
        try
        {
            await DetectHardwareAsync(forceRefresh: false);
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("HardwareInfoPage", "UserControl_Loaded failed", ex);
        }
    }
    
    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await DetectHardwareAsync(forceRefresh: true);
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("HardwareInfoPage", "RefreshButton_Click failed", ex);
        }
    }

    private async Task DetectHardwareAsync(bool forceRefresh)
    {
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _detectionCancellation, cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        var detectionVersion = Interlocked.Increment(ref _detectionVersion);
        SetPageState(HardwarePageState.Loading);
        
        try
        {
            if (forceRefresh)
                _hardwareDetectionService.InvalidateCache();

            var detectionTask = _hardwareDetectionService.GetOrDetectAsync();
            var hardwareInfo = await detectionTask.WaitAsync(cancellation.Token);
            var diskInfoList = await Task.Run(() => GetDiskInfo(), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            if (detectionVersion != Volatile.Read(ref _detectionVersion))
                return;

            _hardwareInfo = hardwareInfo;
            _diskInfoList = diskInfoList;
            _gpuAdapterList = hardwareInfo.AllGpus
                .Select(gpu => new GpuAdapterDisplayModel(
                    gpu.Name,
                    gpu.Vendor.ToString(),
                    GpuKindDisplay(gpu.Kind),
                    DisplayValue(gpu.DriverVersion),
                    gpu.ClassificationSource))
                .ToList();

            UpdateUI();
            var hasData = HasMeaningfulHardwareData(hardwareInfo) || diskInfoList.Count > 0;
            SetPageState(hasData ? HardwarePageState.Ready : HardwarePageState.Empty);
            PageDiagnosticService.LogLoaded("HardwareInfoPage", hasData, $"CPU: {hardwareInfo.CpuName}");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            PageDiagnosticService.Log("HardwareInfoPage", "Hardware detection cancelled");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("HardwareInfoPage", "Hardware detection failed", ex);
            if (detectionVersion == Volatile.Read(ref _detectionVersion))
                SetPageState(HardwarePageState.Error);
        }
        finally
        {
            if (ReferenceEquals(_detectionCancellation, cancellation))
                _detectionCancellation = null;
            cancellation.Dispose();
        }
    }

    private void SetPageState(HardwarePageState state)
    {
        _pageState = state;
        var loc = LocalizationService.Instance;
        var hasData = _hardwareInfo is not null && HasMeaningfulHardwareData(_hardwareInfo);

        StatusBanner.Visibility = state == HardwarePageState.Ready ? Visibility.Collapsed : Visibility.Visible;
        StatusProgress.Visibility = state == HardwarePageState.Loading ? Visibility.Visible : Visibility.Collapsed;
        StatusRetryButton.Visibility = state is HardwarePageState.Empty or HardwarePageState.Error
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusText.Text = state switch
        {
            HardwarePageState.Loading => loc.Get("Hardware.Detecting"),
            HardwarePageState.Empty => loc.Get("Hardware.NotDetected"),
            HardwarePageState.Error => loc.Get("Hardware.DetectionFailed"),
            _ => string.Empty
        };

        HardwareContent.Visibility = state is HardwarePageState.Empty || (state == HardwarePageState.Error && !hasData)
            ? Visibility.Collapsed
            : Visibility.Visible;
        HardwareContent.Opacity = state == HardwarePageState.Loading ? 0.55 : 1;
        RefreshButton.IsEnabled = state != HardwarePageState.Loading;
        CopyConfigButton.IsEnabled = hasData && state != HardwarePageState.Loading;
        ExportReportButton.IsEnabled = hasData && state != HardwarePageState.Loading;
    }

    private static bool HasMeaningfulHardwareData(HardwareInfo info)
        => IsKnownValue(info.OsVersion)
           || IsKnownValue(info.CpuName)
           || IsKnownValue(info.GpuName)
           || IsKnownValue(info.MotherboardName)
           || info.TotalMemoryGb > 0;

    private static bool IsKnownValue(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && !string.Equals(value.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase)
           && value.Trim() != "--";

    private static string DisplayValue(string? value)
        => IsKnownValue(value) ? value!.Trim() : LocalizationService.Instance.Get("Hardware.NotDetected");

    private void UpdateUI()
    {
        if (_hardwareInfo == null) return;
        var loc = LocalizationService.Instance;

        if (OsVersion != null) OsVersion.Text = DisplayValue(_hardwareInfo.OsVersion);
        if (SystemArch != null) SystemArch.Text = Environment.Is64BitOperatingSystem ? loc.Get("Hardware.Bit64") : loc.Get("Hardware.Bit32");
        if (WindowsBuild != null) WindowsBuild.Text = IsKnownValue(_hardwareInfo.WindowsBuild) ? $"Build {_hardwareInfo.WindowsBuild}" : "--";

        if (CpuName != null) CpuName.Text = DisplayValue(_hardwareInfo.CpuName);
        if (CpuVendorText != null) CpuVendorText.Text = _hardwareInfo.GetCpuVendorDisplayName();
        if (CpuVendorSubtitle != null) CpuVendorSubtitle.Text = VendorSubtitle(_hardwareInfo.GetCpuVendorDisplayName(), "CPU");
        if (CpuCores != null) CpuCores.Text = _hardwareInfo.CpuCores > 0 ? _hardwareInfo.CpuCores.ToString() : "--";
        if (CpuThreads != null) CpuThreads.Text = _hardwareInfo.CpuThreads > 0 ? _hardwareInfo.CpuThreads.ToString() : "--";

        if (GpuName != null) GpuName.Text = DisplayValue(_hardwareInfo.GpuName);

        // BUG-005: 根据 GPU 厂商动态设置图标文字和徽章
        if (GpuIconText != null)
        {
            GpuIconText.Text = _hardwareInfo.GpuVendor switch
            {
                GpuVendor.NVIDIA => "NVIDIA",
                GpuVendor.AMD => "AMD",
                GpuVendor.Intel => "Intel",
                _ => "GPU"
            };
        }
        if (GpuBadgeText != null)
        {
            GpuBadgeText.Text = _hardwareInfo.GpuVendor == GpuVendor.Unknown
                ? loc.Get("Hardware.NotDetected")
                : GpuKindDisplay(_hardwareInfo.PrimaryGpuKind);
        }
        if (GpuVendorSubtitle != null) GpuVendorSubtitle.Text = VendorSubtitle(_hardwareInfo.GetGpuVendorDisplayName(), "GPU");

        if (MemoryTotal != null) MemoryTotal.Text = _hardwareInfo.TotalMemoryGb > 0 ? $"{_hardwareInfo.TotalMemoryGb:F1}" : "--";
        if (MemoryType != null) MemoryType.Text = DisplayValue(_hardwareInfo.MemoryType);
        if (MemoryFrequency != null) MemoryFrequency.Text = _hardwareInfo.MemoryFrequency > 0 ? $"{_hardwareInfo.MemoryFrequency} MHz" : "--";
        if (MemoryVendorSubtitle != null)
        {
            var isEnglish = loc.CurrentLanguage == "en-US";
            MemoryVendorSubtitle.Text = IsKnownValue(_hardwareInfo.MemoryType)
                ? (isEnglish ? $"Detected {_hardwareInfo.MemoryType}" : $"已识别 {_hardwareInfo.MemoryType}")
                : (isEnglish ? "Memory module · vendor unavailable" : "内存模块 · 厂商未知");
        }

        if (Motherboard != null) Motherboard.Text = DisplayValue(_hardwareInfo.MotherboardName);

        if (ComputerName != null) ComputerName.Text = Environment.MachineName;
        if (UserName != null) UserName.Text = Environment.UserName;
        if (Uptime != null) Uptime.Text = GetSystemUptime();
        if (DotNetVersion != null) DotNetVersion.Text = Environment.Version.ToString();

        if (_diskInfoList != null && _diskInfoList.Count > 0)
        {
            if (DiskSummary != null) DiskSummary.Text = loc.Get("Hardware.Partitions", _diskInfoList.Count);
            if (DiskListPanel != null) DiskListPanel.ItemsSource = _diskInfoList;
            if (DiskEmptyText != null) DiskEmptyText.Visibility = Visibility.Collapsed;
        }
        else
        {
            if (DiskSummary != null) DiskSummary.Text = loc.Get("Hardware.NotDetected");
            if (DiskListPanel != null) DiskListPanel.ItemsSource = null;
            if (DiskEmptyText != null) DiskEmptyText.Visibility = Visibility.Visible;
        }

        if (GpuAdapterListPanel != null)
            GpuAdapterListPanel.ItemsSource = _gpuAdapterList;
        if (GpuAdapterEmptyText != null)
            GpuAdapterEmptyText.Visibility = _gpuAdapterList is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string GpuKindDisplay(GpuAdapterKind kind) => kind switch
    {
        GpuAdapterKind.Integrated => LocalizationService.Instance.Get("Hardware.IntegratedGpu"),
        GpuAdapterKind.Discrete => LocalizationService.Instance.Get("Hardware.DiscreteGpu"),
        GpuAdapterKind.SoftwareOrVirtual => LocalizationService.Instance.Get("Hardware.GpuTypeSoftwareOrVirtual"),
        _ => LocalizationService.Instance.Get("Hardware.GpuTypeUnknown")
    };

    private static string GetSystemUptime()
    {
        try
        {
            var loc = LocalizationService.Instance;
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            if (uptime.TotalDays >= 1)
                return loc.Get("Hardware.Days", (int)uptime.TotalDays, uptime.Hours);
            if (uptime.TotalHours >= 1)
                return loc.Get("Hardware.Hours", (int)uptime.TotalHours, uptime.Minutes);
            return loc.Get("Hardware.Minutes", uptime.Minutes);
        }
        catch
        {
            return "--";
        }
    }

    private static List<DiskInfoModel> GetDiskInfo()
    {
        var result = new List<DiskInfoModel>();
        var loc = LocalizationService.Instance;
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var usagePercent = DiskInfoModel.CalculateUsagePercent(drive.TotalSize, drive.AvailableFreeSpace);

                result.Add(new DiskInfoModel
                {
                    DriveLetter = drive.Name.TrimEnd('\\'),
                    Label = string.IsNullOrEmpty(drive.VolumeLabel) ? loc.Get("Hardware.LocalDisk") : drive.VolumeLabel,
                    DriveType = drive.DriveFormat,
                    TotalSpace = FormatSize(drive.TotalSize),
                    UsedSpace = FormatSize(drive.TotalSize - drive.AvailableFreeSpace),
                    FreeSpace = FormatSize(drive.AvailableFreeSpace),
                    UsagePercent = usagePercent
                });
            }
        }
        catch (Exception ex) 
        { 
            Debug.WriteLine($"[HardwareInfo] GetDiskInfo failed: {ex.Message}"); 
        }
        return result;
    }

    private static string FormatSize(long bytes)
    {
        var gb = bytes / (1024.0 * 1024 * 1024);
        return gb >= 1000 ? $"{gb / 1024:F2} TB" : $"{gb:F1} GB";
    }

    private void CopyInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        if (_hardwareInfo == null)
        {
            ToastService.Instance.Warning(loc.Get("Hardware.Title"), loc.Get("Hardware.WaitDetection"));
            return;
        }

        var cpuTopology = _hardwareInfo.CpuCores > 0 && _hardwareInfo.CpuThreads > 0
            ? $"{_hardwareInfo.CpuCores}{loc.Get("Hardware.Cores")}{_hardwareInfo.CpuThreads}{loc.Get("Hardware.Threads")}"
            : loc.Get("Hardware.NotDetected");
        var memorySummary = _hardwareInfo.TotalMemoryGb > 0
            ? $"{_hardwareInfo.TotalMemoryGb:F1} GB {DisplayValue(_hardwareInfo.MemoryType)}"
            : loc.Get("Hardware.NotDetected");
        var sb = new StringBuilder();
        sb.AppendLine($"{loc.Get("Hardware.OperatingSystem")}: {DisplayValue(_hardwareInfo.OsVersion)} ({(Environment.Is64BitOperatingSystem ? loc.Get("Hardware.Bit64") : loc.Get("Hardware.Bit32"))})");
        sb.AppendLine($"{loc.Get("Hardware.Processor")}: {DisplayValue(_hardwareInfo.CpuName)} ({cpuTopology})");
        sb.AppendLine($"{loc.Get("Hardware.Graphics")}: {DisplayValue(_hardwareInfo.GpuName)}");
        foreach (var gpu in _hardwareInfo.AllGpus)
            sb.AppendLine($"  - {DisplayValue(gpu.Name)} | {gpu.Vendor} | {GpuKindDisplay(gpu.Kind)} | {DisplayValue(gpu.DriverVersion)}");
        sb.AppendLine($"{loc.Get("Hardware.Memory")}: {memorySummary}");
        sb.AppendLine($"{loc.Get("Hardware.Motherboard")}: {DisplayValue(_hardwareInfo.MotherboardName)}");
        
        if (ClipboardHelper.SetText(sb.ToString()))
        {
            ToastService.Instance.Success(loc.Get("Hardware.Title"), loc.Get("Hardware.CopySuccess"));
        }
        else
        {
            ToastService.Instance.Error(loc.Get("Hardware.Title"), loc.Get("Hardware.CopyFailed"));
        }
    }

    private void OpenSystemInfoButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "msinfo32.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("HardwareInfoPage", "OpenSystemInfoButton_Click failed", ex);
            ToastService.Instance.Error(LocalizationService.Instance.Get("Hardware.Title"), "无法打开 Windows 系统信息。");
        }
    }

    private static string VendorSubtitle(string vendor, string component)
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        if (!IsKnownValue(vendor) || vendor is "未知" or "Unknown")
            return isEnglish ? $"{component} vendor unavailable" : $"{component} 厂商未知";

        return isEnglish ? $"Detected {vendor}" : $"已识别 {vendor}";
    }

    private void ExportReportButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        if (_hardwareInfo == null)
        {
            InfoDialog.Show(Window.GetWindow(this), loc.Get("Dialog.Info"), loc.Get("HardwareExport.WaitDetection"), Constants.IconGlyphs.Lightbulb);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = loc.Get("HardwareExport.Filter"),
            FileName = loc.Get("HardwareExport.FileName", DateTime.Now),
            Title = loc.Get("HardwareExport.Title")
        };

        if (dialog.ShowDialog() == true)
        {
            // P3-HW-002: 验证导出路径
            var exportPath = dialog.FileName;
            var directory = Path.GetDirectoryName(exportPath);
            
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), loc.Get("Hardware.ExportPathInvalid"), Constants.IconGlyphs.Warning);
                return;
            }
            
            // 检查写权限
            try
            {
                var testFile = Path.Combine(directory, $".test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
            }
            catch
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), loc.Get("Hardware.ExportPathNoPermission"), Constants.IconGlyphs.Lock);
                return;
            }
            
            try
            {
                var arch = Environment.Is64BitOperatingSystem ? loc.Get("HardwareExport.Arch64") : loc.Get("HardwareExport.Arch32");
                var sb = new StringBuilder();
                sb.AppendLine("══════════════════════════════════════════════════════════════");
                sb.AppendLine($"                    {loc.Get("HardwareExport.ReportTitle")}");
                sb.AppendLine($"                    {loc.Get("HardwareExport.GeneratedAt", DateTime.Now)}");
                sb.AppendLine("══════════════════════════════════════════════════════════════");
                sb.AppendLine();
                sb.AppendLine(loc.Get("HardwareExport.OS"));
                sb.AppendLine(loc.Get("HardwareExport.Version", _hardwareInfo.OsVersion));
                sb.AppendLine(loc.Get("HardwareExport.Architecture", arch));
                sb.AppendLine($"  Build: {_hardwareInfo.WindowsBuild}");
                sb.AppendLine();
                sb.AppendLine(loc.Get("HardwareExport.CPU"));
                sb.AppendLine(loc.Get("HardwareExport.Model", _hardwareInfo.CpuName));
                sb.AppendLine(loc.Get("HardwareExport.CoreThread", _hardwareInfo.CpuCores, _hardwareInfo.CpuThreads));
                sb.AppendLine();
                sb.AppendLine(loc.Get("HardwareExport.GPU"));
                sb.AppendLine(loc.Get("HardwareExport.Model", _hardwareInfo.GpuName));
                foreach (var gpu in _hardwareInfo.AllGpus)
                    sb.AppendLine($"  {gpu.Name} | {gpu.Vendor} | {GpuKindDisplay(gpu.Kind)} | Driver {gpu.DriverVersion}");
                sb.AppendLine();
                sb.AppendLine(loc.Get("HardwareExport.RAM"));
                sb.AppendLine(loc.Get("HardwareExport.Capacity", _hardwareInfo.TotalMemoryGb));
                sb.AppendLine(loc.Get("HardwareExport.MemType", _hardwareInfo.MemoryType, _hardwareInfo.MemoryFrequency));
                sb.AppendLine();
                sb.AppendLine(loc.Get("HardwareExport.Motherboard"));
                sb.AppendLine(loc.Get("HardwareExport.Model", _hardwareInfo.MotherboardName));
                sb.AppendLine();
                sb.AppendLine(loc.Get("HardwareExport.SystemInfo"));
                sb.AppendLine(loc.Get("HardwareExport.ComputerName", Environment.MachineName));
                sb.AppendLine(loc.Get("HardwareExport.UserName", Environment.UserName));
                sb.AppendLine(loc.Get("HardwareExport.Uptime", GetSystemUptime()));
                sb.AppendLine(loc.Get("HardwareExport.DotNetVersion", Environment.Version));
                sb.AppendLine();

                if (_diskInfoList != null && _diskInfoList.Count > 0)
                {
                    sb.AppendLine(loc.Get("HardwareExport.Storage"));
                    foreach (var disk in _diskInfoList)
                    {
                        sb.AppendLine($"  {disk.DriveLetter} {disk.Label} ({disk.DriveType})");
                        sb.AppendLine(loc.Get("HardwareExport.DiskUsage", disk.UsedSpace, disk.TotalSpace, disk.UsagePercent));
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("══════════════════════════════════════════════════════════════");

                var (success, error) = GameOptimizer.Core.Services.SafeFileHelper.WriteAllText(dialog.FileName, sb.ToString());
                if (success)
                {
                    InfoDialog.Show(Window.GetWindow(this), loc.Get("HardwareExport.ExportSuccessTitle"), loc.Get("HardwareExport.ExportSuccess", dialog.FileName), Constants.IconGlyphs.Check);
                }
                else
                {
                    InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), loc.Get("HardwareExport.ExportFailed", error ?? loc.Get("Common.Error")), Constants.IconGlyphs.Warning);
                }
            }
            catch (Exception ex)
            {
                InfoDialog.Show(Window.GetWindow(this), loc.Get("Common.Error"), loc.Get("HardwareExport.ExportFailed", ex.Message), Constants.IconGlyphs.Warning);
            }
        }
    }
}

public class DiskInfoModel
{
    public string DriveLetter { get; set; } = "";
    public string Label { get; set; } = "";
    public string DriveType { get; set; } = "";
    public string TotalSpace { get; set; } = "";
    public string UsedSpace { get; set; } = "";
    public string FreeSpace { get; set; } = "";
    public double UsagePercent { get; set; }
    public string UsagePercentText => $"{Math.Clamp(UsagePercent, 0, 100):F0}%";
    public string FreeSpaceText => LocalizationService.Instance.Get("Hardware.Available") + FreeSpace;

    public static double CalculateUsagePercent(long totalBytes, long availableBytes)
    {
        if (totalBytes <= 0)
            return 0;

        var safeAvailable = Math.Clamp(availableBytes, 0, totalBytes);
        var usedBytes = totalBytes - safeAvailable;
        return Math.Clamp(usedBytes * 100d / totalBytes, 0d, 100d);
    }
}

public sealed record GpuAdapterDisplayModel(
    string Name,
    string Vendor,
    string Kind,
    string DriverVersion,
    string ClassificationSource);
