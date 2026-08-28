using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Startup;
using GameOptimizer.Infrastructure.Windows.Startup;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Views;
using Microsoft.Win32;
using L = GameOptimizer.Core.Localization.LocalizationService;

namespace GameOptimizer.Shell.Views;

public partial class SettingsPage : UserControl
{
    private AppSettings _settings;
    private bool _isInitialized;
    private const string AppName = "YuqiEngine";
    private readonly int[] _logRetentionDays = { 7, 14, 30, 0 };
    private const int AsyncTimeoutSeconds = 30;
    private const int AutoCheckIntervalHours = 12;
    private const string SupportPanUrl = "https://pan.quark.cn/s/432600ae9d8d";
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IConfigurationExportService _configurationExportService;
    private readonly DiagnosticPackageService _diagnosticPackageService;
    private readonly Func<IStartupEntryOwner> _startupOwnerFactory;
    private readonly IAppSettingsStore _settingsStore;
    private IStartupEntryOwner? _startupOwner;
    private bool _isCheckingUpdate;
    private bool _isDownloadingUpdate;
    private UpdateInfo? _latestUpdateInfo;
    private string? _downloadedPackagePath;
    private bool _hasShownCloudGuideTip;

    public SettingsPage(
        IAppSettingsStore settingsStore,
        IUpdateCheckService updateCheckService,
        IConfigurationExportService configurationExportService,
        DiagnosticPackageService diagnosticPackageService,
        Func<IStartupEntryOwner> startupOwnerFactory)
    {
        PageDiagnosticService.Log("SettingsPage", "Constructor called");
        InitializeComponent();
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _settings = settingsStore.Current;
        _updateCheckService = updateCheckService ?? throw new ArgumentNullException(nameof(updateCheckService));
        _configurationExportService = configurationExportService ?? throw new ArgumentNullException(nameof(configurationExportService));
        _diagnosticPackageService = diagnosticPackageService ?? throw new ArgumentNullException(nameof(diagnosticPackageService));
        _startupOwnerFactory = startupOwnerFactory ?? throw new ArgumentNullException(nameof(startupOwnerFactory));
        // The composition root supplies this factory from provider.GetRequiredService<IStartupEntryOwner>();
        // keeping resolution deferred prevents construction from touching the registry.
        // Keep the page construction path read-only and responsive.  Resolving the owner
        // constructs its durable confirmation secret, which is intentionally deferred until
        // the user explicitly requests a startup-entry change.
        
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }
    
    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
    }
    
    private void UpdateLocalizedTexts()
    {
        var loc = L.Instance;
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        
        // Hero Header - 使用 x:Name 直接更新
        TitleText.Text = loc.Get("Settings.Title");
        SubtitleText.Text = loc.Get("Settings.Subtitle");
        
        // Section Headers
        AppearanceSectionTitle.Text = loc.Get("Settings.AppearanceLanguage");
        SafetySectionTitle.Text = loc.Get("Settings.SafetySettings");
        GeneralSectionTitle.Text = loc.Get("Settings.GeneralSettings");
        LogSectionTitle.Text = loc.Get("Settings.LogManagement");
        DataSectionTitle.Text = loc.Get("Settings.DataManagement");
        
        // Language label
        LanguageLabel.Text = loc.Get("Settings.Language");
        
        // Safety settings
        RiskWarningTitle.Text = loc.Get("Settings.RiskWarning");
        RiskWarningDesc.Text = isEnglish
            ? "Show an additional warning in high-risk optimization confirmations"
            : "高风险优化确认时显示额外警告";
        SoundTitle.Text = loc.Get("Settings.SoundNotification");
        SoundDesc.Text = isEnglish
            ? "Play the Windows notification sound after an optimization succeeds"
            : "优化执行成功时播放系统提示音";
        
        // General settings
        StartupTitle.Text = loc.Get("Settings.StartWithWindows");
        StartupDesc.Text = isEnglish
            ? "Run YuqiEngine after signing in to Windows"
            : "登录 Windows 后自动运行 YuqiEngine";
        TrayTitle.Text = loc.Get("Settings.MinimizeToTray");
        TrayDesc.Text = isEnglish
            ? "Minimize or close to the tray; autostart launches directly into the tray"
            : "最小化或关闭窗口时进入托盘；自启动时静默进入托盘";
        GameNotifyTitle.Text = loc.Get("Settings.GameLaunchNotification");
        GameNotifyDesc.Text = loc.Get("Settings.GameLaunchNotificationDesc");
        
        // Log management
        LogRetentionTitle.Text = loc.Get("Settings.LogRetention");
        Days7Label.Text = loc.Get("Settings.Days7");
        Days14Label.Text = loc.Get("Settings.Days14");
        Days30Label.Text = loc.Get("Settings.Days30");
        ForeverLabel.Text = loc.Get("Settings.Forever");
        LogRetentionApplyHintText.Text = isEnglish
            ? "The setting is saved immediately; cleanup is applied on the next app start."
            : "设置立即保存，清理策略将在下次启动时应用。";
        ApplyCustomLogRetentionButton.Content = isEnglish ? "Apply custom days" : "应用自定义天数";
        CustomLogRetentionHintText.Text = isEnglish
            ? "Enter 1–3650 days; Forever disables automatic cleanup."
            : "可输入 1–3650 天；选择“永久”则不自动清理。";
        ConfigurationPortabilityHintText.Text = isEnglish
            ? "Import/export covers applied optimization profiles, not logs or complete app settings. Import reapplies recorded optimizations after confirmation."
            : "这里导入导出的是已应用优化方案，不是日志备份或完整应用设置；导入会在确认后重新应用优化项。";
        LogBackupDirectoryTitle.Text = isEnglish ? "Log storage directory" : "日志保存目录";
        LogBackupDirectoryHintText.Text = isEnglish
            ? "Diagnostic logs and packages use this directory. Live logs switch after the next restart. Local writable folders only."
            : "诊断日志和诊断包使用同一目录；更改将在下次启动后对实时日志生效。仅支持本地可写目录。";
        ChooseLogBackupDirectoryLabel.Text = isEnglish ? "Choose folder" : "选择目录";
        OpenLogBackupDirectoryLabel.Text = isEnglish ? "Open folder" : "打开目录";
        UseDefaultLogBackupDirectoryLabel.Text = isEnglish ? "Use default" : "恢复默认";
        LogBackupDirectoryFallbackHintText.Text = isEnglish
            ? "If the custom directory is unavailable, the next launch safely falls back to the app data directory."
            : "若自定义目录不可用，下次启动将安全回退到应用数据目录。";
        ProblemPackageTitleText.Text = isEnglish ? "Generate problem package" : "生成问题包";
        ProblemPackageDescriptionText.Text = isEnglish
            ? "Package YuqiEngine runtime logs and minimal version information for troubleshooting."
            : "打包宇奇引擎运行日志和最小版本信息，便于发给作者排查问题。";
        ProblemPackagePrivacyText.Text = isEnglish
            ? "Excludes settings, backups, keys, and user documents. The package is never uploaded automatically."
            : "不包含设置、备份、密钥或用户文档；不会自动上传。";
        GenerateProblemPackageLabel.Text = isEnglish ? "Generate package" : "生成问题包";
        
        // Data management buttons
        ExportConfigLabel.Text = isEnglish ? "Export optimization profile" : "导出优化方案";
        ImportConfigLabel.Text = isEnglish ? "Import optimization profile" : "导入优化方案";
        OpenLogLabel.Text = loc.Get("Settings.OpenLog");
        ResetSettingsLabel.Text = loc.Get("Settings.ResetAllSettings");
        
        // About section
        AppDescriptionText.Text = loc.Get("Settings.AppDescription");
        AboutButtonLabel.Text = loc.Get("Settings.About");
        AuthorButtonLabel.Text = loc.Get("Settings.AuthorLabel");
        UpdateEntryButtonLabel.Text = isEnglish ? "Update" : "更新";
        GitHubRepoTitleText.Text = loc.Get("Settings.UpdateModuleTitle");
        GitHubRepoDescText.Text = loc.Get("Settings.UpdateModuleDesc");
        UpdateFlowText.Text = loc.Get("Settings.UpdateFlow");
        UpdateStrategyBadgeText.Text = loc.Get("Settings.UpdateStrategyBadge");
        CheckUpdateButtonLabel.Text = loc.Get("Settings.CheckUpdate");
        AutoUpdateButtonLabel.Text = loc.Get("Settings.AutoUpdate");
        OpenOfficialReleaseButtonLabel.Text = loc.Get("Settings.OpenOfficialRelease");
        OpenCloudLibraryButtonLabel.Text = loc.Get("Settings.OpenCloudLibraryRecommended");
        CopyCloudLinkButtonLabel.Text = loc.Get("Settings.CopyCloudLink");
        OpenUpdateFolderButtonLabel.Text = loc.Get("Settings.OpenUpdateFolder");
        LatestReleaseLabelText.Text = loc.Get("Settings.LatestReleaseLabel");
        UpdateGuideTitleText.Text = loc.Get("Settings.UpdateGuideTitle");
        CloudLibraryHintText.Text = loc.Get("Settings.CloudLibraryGuide");
        CloudLibraryUrlText.Text = SupportPanUrl;
        UpdateActionHintText.Text = loc.Get("Settings.UpdateActionHint");
        CloudChannelTitleText.Text = loc.Get("Settings.UpdateCloudChannelTitle");
        CloudChannelDescText.Text = loc.Get("Settings.UpdateCloudChannelDesc");
        CloudChannelTagText.Text = loc.Get("Settings.UpdateCloudChannelTag");
        DirectChannelTitleText.Text = loc.Get("Settings.UpdateDirectChannelTitle");
        DirectChannelDescText.Text = loc.Get("Settings.UpdateDirectChannelDesc");
        DirectChannelTagText.Text = loc.Get("Settings.UpdateDirectChannelTag");
        UpdateProgressText.Text = loc.Get("Settings.UpdateProgressIdle");
        UpdateStep1Text.Text = loc.Get("Settings.UpdateStep1");
        UpdateStep2Text.Text = loc.Get("Settings.UpdateStep2");
        UpdateStep3Text.Text = loc.Get("Settings.UpdateStep3");

        UpdateLogRetentionText();
        UpdateLogBackupDirectoryText();
        UpdateProblemPackagePreview();
        RefreshUpdateActionLabels();
        UpdateLatestReleaseInfo(_latestUpdateInfo);
        UpdateCurrentVersionMetaText();
        UpdateAutoCheckMetaText();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var childOfChild in FindVisualChildren<T>(child))
                yield return childOfChild;
        }
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;
        try
        {
            PageDiagnosticService.Log("SettingsPage", "SettingsPage_Loaded - Loading settings to UI");
            
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            
            if (AppSettings.LastLoadWasRecovered)
            {
                ShowToast(L.Instance.Get("Settings.ConfigRecoveredFromBackup"), isWarning: true);
            }
            
            LoadSettingsToUI();
            await RefreshStartupTruthAsync();
            UpdateLocalizedTexts();
            _isInitialized = true;
            _ = TryAutoCheckForUpdatesAsync();
            PageDiagnosticService.LogLoaded("SettingsPage", true, "Settings loaded");
        }
        catch (Exception ex)
        {
            PageDiagnosticService.LogError("SettingsPage", "Failed to load settings", ex);
            ShowToast(L.Instance.Get("Settings.LoadFailed", ex.Message), isWarning: true);
        }
    }
    
    private Grid? _toastContainer;
    
    private void ShowToast(string message, bool isWarning = false)
    {
        var toast = new Border
        {
            Background = isWarning ? ThemeColors.WarningBrush : ThemeColors.SuccessBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 20, 0, 0),
            Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Medium
            }
        };
        
        if (_toastContainer != null)
        {
            _toastContainer.Children.Add(toast);
        }
        else if (Content is ScrollViewer sv)
        {
            _toastContainer = new Grid();
            Content = null;
            _toastContainer.Children.Add(sv);
            _toastContainer.Children.Add(toast);
            Content = _toastContainer;
        }
        else
        {
            return;
        }
        
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        EventHandler? tickHandler = null;
        tickHandler = (s, args) =>
        {
            timer.Stop();
            timer.Tick -= tickHandler;
            _toastContainer?.Children.Remove(toast);
        };
        timer.Tick += tickHandler;
        timer.Start();
    }

    private void LoadSettingsToUI()
    {
        UpdateLanguageSelection(_settings.Language);

        RiskWarningToggle.IsChecked = _settings.ShowRiskWarnings;
        StartupToggle.IsChecked = _settings.StartWithWindows;
        TrayToggle.IsChecked = _settings.MinimizeToTray;
        GameLaunchNotifyToggle.IsChecked = _settings.NotifyOnGameLaunch;
        SoundToggle.IsChecked = _settings.EnableSoundNotification;

        int sliderIndex = Array.IndexOf(_logRetentionDays, _settings.LogRetentionDays);
        if (sliderIndex >= 0) LogRetentionSlider.Value = sliderIndex;
        CustomLogRetentionTextBox.Text = _settings.LogRetentionDays > 0
            ? _settings.LogRetentionDays.ToString()
            : string.Empty;
        UpdateLogRetentionText();
        UpdateLogBackupDirectoryText();
        GitHubRepoTextBox.Text = NormalizeRepository(_settings.GitHubRepo);
        _latestUpdateInfo = null;
        _downloadedPackagePath = null;
        _hasShownCloudGuideTip = false;
        OpenUpdateFolderButton.Visibility = Visibility.Collapsed;
        SetUpdateProgressState(show: false);
        UpdateUpdateActionState();
        UpdateLatestReleaseInfo(null);
        SetUpdateStatus(L.Instance.Get("Settings.UpdateIdleState"), ThemeColors.TextMutedBrush);
        UpdateCurrentVersionMetaText();
        UpdateAutoCheckMetaText();
    }

    private static string NormalizeRepository(string? repository)
    {
        return string.IsNullOrWhiteSpace(repository) ? string.Empty : repository.Trim();
    }

    private void SetUpdateStatus(string message, Brush foreground)
    {
        UpdateStatusText.Text = message;
        UpdateStatusText.Foreground = foreground;
        if (string.IsNullOrWhiteSpace(message))
        {
            UpdateStatusCard.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateStatusCard.Visibility = Visibility.Visible;
        if (ReferenceEquals(foreground, ThemeColors.WarningBrush))
        {
            UpdateStatusIconText.Text = "\uE7BA";
            UpdateStatusIconText.Foreground = ThemeColors.WarningBrush;
            UpdateStatusCard.BorderBrush = ThemeColors.WarningAlpha30Brush;
            UpdateStatusCard.Background = ThemeColors.WarningAlpha15Brush;
            return;
        }

        if (ReferenceEquals(foreground, ThemeColors.SuccessBrush))
        {
            UpdateStatusIconText.Text = "\uE73E";
            UpdateStatusIconText.Foreground = ThemeColors.SuccessBrush;
            UpdateStatusCard.BorderBrush = ThemeColors.SuccessAlpha20Brush;
            UpdateStatusCard.Background = ThemeColors.SuccessAlpha15Brush;
            return;
        }

        UpdateStatusIconText.Text = "\uE946";
        UpdateStatusIconText.Foreground = ThemeColors.InfoBrush;
        UpdateStatusCard.BorderBrush = ThemeColors.InfoAlpha30Brush;
        UpdateStatusCard.Background = ThemeColors.InfoAlpha15Brush;
    }

    private void UpdateAutoCheckMetaText()
    {
        var loc = L.Instance;
        var intervalText = loc.Get("Settings.AutoCheckRule", AutoCheckIntervalHours);
        if (_settings.LastAutoUpdateCheckUtc == DateTime.MinValue)
        {
            AutoCheckMetaText.Text = $"{intervalText}  |  {loc.Get("Settings.AutoCheckLastNever")}";
            return;
        }

        var lastCheckText = _settings.LastAutoUpdateCheckUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        AutoCheckMetaText.Text = $"{intervalText}  |  {loc.Get("Settings.AutoCheckLastAt", lastCheckText)}";
    }

    private void UpdateCurrentVersionMetaText()
    {
        var version = _updateCheckService?.CurrentVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = "0.1.0";
        }

        CurrentVersionMetaText.Text = L.Instance.Get("Settings.CurrentVersionText", $"v{version.TrimStart('v', 'V')}");
    }

    private void UpdateUpdateActionState()
    {
        var serviceAvailable = _updateCheckService != null;
        var busy = _isCheckingUpdate || _isDownloadingUpdate;
        var hasUpdate = _latestUpdateInfo != null;
        var officialReleaseUrl = ResolveOfficialReleaseUrl();

        CheckUpdateButton.IsEnabled = serviceAvailable && !busy;
        AutoUpdateButton.IsEnabled = serviceAvailable && !busy && hasUpdate;
        AutoUpdateButton.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        OpenOfficialReleaseButton.IsEnabled = !busy && hasUpdate && !string.IsNullOrWhiteSpace(officialReleaseUrl);
        OpenOfficialReleaseButton.Visibility = hasUpdate && !string.IsNullOrWhiteSpace(officialReleaseUrl)
            ? Visibility.Visible
            : Visibility.Collapsed;
        DownloadOptionsPanel.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateActionHintText.Visibility = Visibility.Collapsed;
        OpenCloudLibraryButton.IsEnabled = hasUpdate && !busy;
        CopyCloudLinkButton.IsEnabled = hasUpdate && !busy;
        OpenUpdateFolderButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(_downloadedPackagePath);
        OpenUpdateFolderButton.Visibility = string.IsNullOrWhiteSpace(_downloadedPackagePath)
            ? Visibility.Collapsed
            : Visibility.Visible;
        RefreshUpdateActionLabels();
    }

    private void RefreshUpdateActionLabels()
    {
        var loc = L.Instance;
        CheckUpdateButtonLabel.Text = _isCheckingUpdate
            ? loc.Get("Settings.CheckingButton")
            : loc.Get("Settings.CheckUpdate");
        AutoUpdateButtonLabel.Text = _isDownloadingUpdate
            ? loc.Get("Settings.DownloadingButton")
            : loc.Get("Settings.AutoUpdate");
    }

    private void SetUpdateProgressState(bool show, string? message = null)
    {
        UpdateProgressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressText.Text = string.IsNullOrWhiteSpace(message)
            ? L.Instance.Get("Settings.UpdateProgressIdle")
            : message;
    }

    private void UpdateLatestReleaseInfo(UpdateInfo? info)
    {
        if (info is null)
        {
            LatestReleaseLabelText.Visibility = Visibility.Collapsed;
            LatestReleaseInfoText.Visibility = Visibility.Collapsed;
            LatestReleaseInfoText.Text = string.Empty;
            return;
        }

        var loc = L.Instance;
        var releaseDate = info.ReleaseDate == default
            ? loc.Get("Settings.ReleaseDateUnknown")
            : info.ReleaseDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var sizeText = FormatPackageSize(info.Size);
        LatestReleaseInfoText.Text = loc.Get("Settings.LatestReleaseInfo", info.Version, releaseDate, sizeText);
        LatestReleaseLabelText.Visibility = Visibility.Visible;
        LatestReleaseInfoText.Visibility = Visibility.Visible;
    }

    private static string FormatPackageSize(long bytes)
    {
        if (bytes <= 0)
        {
            return L.Instance.Get("Settings.ReleaseSizeUnknown");
        }

        var size = (double)bytes;
        var units = new[] { "B", "KB", "MB", "GB" };
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F1} {units[unitIndex]}";
    }

    private void GitHubRepoTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        var repository = NormalizeRepository(GitHubRepoTextBox.Text);
        GitHubRepoTextBox.Text = repository;

        if (string.Equals(_settings.GitHubRepo, repository, StringComparison.Ordinal))
        {
            return;
        }

        var previous = _settings.GitHubRepo;
        if (!_settingsStore.TryUpdate(settings => settings.GitHubRepo = repository))
        {
            GitHubRepoTextBox.Text = previous;
            ShowToast(L.Instance.Get("Settings.SaveFailed"), isWarning: true);
            return;
        }
        _settings = _settingsStore.Current;
        _latestUpdateInfo = null;
        _downloadedPackagePath = null;
        UpdateUpdateActionState();
        UpdateLatestReleaseInfo(null);
        ShowToast(L.Instance.Get("Settings.GitHubRepoSaved"));
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        _ = await PerformUpdateCheckAsync(triggeredByAutoCheck: false, showNoUpdateMessage: true);
    }

    private async void UpdateEntryButton_Click(object sender, RoutedEventArgs e)
    {
        await PerformUpdateEntryActionAsync();
    }

    private async Task PerformUpdateEntryActionAsync()
    {
        if (_isCheckingUpdate || _isDownloadingUpdate)
        {
            ShowToast(LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "Update task is already running."
                : "更新任务正在进行中，请稍候。");
            return;
        }

        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        ShowToast(isEnglish ? "Checking for updates..." : "正在检查更新...");

        var updateInfo = _latestUpdateInfo;
        if (updateInfo is null)
        {
            var result = await PerformUpdateCheckAsync(triggeredByAutoCheck: false, showNoUpdateMessage: true);
            updateInfo = result?.Data;
        }

        if (updateInfo is null)
        {
            ShowToast(isEnglish
                ? "No new version found."
                : "未检测到新版本。");
            return;
        }

        var action = ShowUpdateAvailableDialog(updateInfo);
        ExecuteUpdateDialogAction(action);
    }

    private enum UpdateDialogAction
    {
        None,
        Cloud,
        InApp,
        Official
    }

    private UpdateDialogAction ShowUpdateAvailableDialog(UpdateInfo updateInfo)
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        var dialogAction = UpdateDialogAction.None;
        var owner = Window.GetWindow(this);
        var displayVersion = ResolveDialogUpdateVersion(updateInfo);

        var dialog = new Window
        {
            Width = 680,
            Height = 430,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            Owner = owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            Foreground = ThemeColors.TextPrimaryBrush
        };

        var overlay = new Grid
        {
            Background = Brushes.Transparent
        };
        overlay.MouseDown += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, overlay))
            {
                dialogAction = UpdateDialogAction.None;
                dialog.Close();
            }
        };

        var card = new Border
        {
            Width = 620,
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeColors.WhiteAlpha25Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                Color = Color.FromArgb(0xB0, 0x00, 0x00, 0x00),
                BlurRadius = 26,
                ShadowDepth = 0,
                Opacity = 0.8
            },
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0xF4, 0x17, 0x1E, 0x2B), 0),
                    new GradientStop(Color.FromArgb(0xF4, 0x10, 0x14, 0x1E), 1)
                }
            }
        };

        var cardGrid = new Grid();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerIcon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = ThemeColors.InfoAlpha15Brush,
            BorderBrush = ThemeColors.InfoAlpha50Brush,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = IconGlyphs.Update,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = ThemeColors.InfoLightBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(headerIcon, 0);
        headerGrid.Children.Add(headerIcon);

        var headerTitle = new TextBlock
        {
            Text = isEnglish ? $"Update Available {displayVersion}" : $"发现新版本 {displayVersion}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeColors.TextPrimaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(headerTitle, 1);
        headerGrid.Children.Add(headerTitle);

        var closeButton = new Button
        {
            Width = 34,
            Height = 34,
            Background = Brushes.Transparent,
            BorderBrush = ThemeColors.WhiteAlpha15Brush,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Content = new TextBlock
            {
                Text = IconGlyphs.Close,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = ThemeColors.TextSecondaryBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        closeButton.Click += (_, _) =>
        {
            dialogAction = UpdateDialogAction.None;
            dialog.Close();
        };
        closeButton.MouseEnter += (_, _) => closeButton.Background = ThemeColors.WhiteAlpha10Brush;
        closeButton.MouseLeave += (_, _) => closeButton.Background = Brushes.Transparent;
        Grid.SetColumn(closeButton, 3);
        headerGrid.Children.Add(closeButton);

        var headerStack = new StackPanel();
        headerStack.Children.Add(headerGrid);
        headerStack.Children.Add(new TextBlock
        {
            Text = isEnglish
                ? "Choose one download channel to continue."
                : "请选择一个下载通道继续更新。",
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 12,
            Foreground = ThemeColors.TextSecondaryBrush,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(headerStack, 0);
        cardGrid.Children.Add(headerStack);

        var optionPanel = new StackPanel
        {
            Margin = new Thickness(0, 14, 0, 0)
        };
        optionPanel.Children.Add(CreateUpdateOptionButton(
            isEnglish ? "Cloud Download First (Recommended)" : "网盘优先下载（推荐）",
            isEnglish ? "Open your cloud library first and locate Yuqi Engine package." : "先打开你的网盘软件库，再找到宇奇引擎安装包。",
            IconGlyphs.Link,
            () =>
            {
                dialogAction = UpdateDialogAction.Cloud;
                dialog.Close();
            },
            isPrimary: true));

        optionPanel.Children.Add(CreateUpdateOptionButton(
            isEnglish ? "In-App Auto Download" : "应用内自动下载",
            isEnglish ? "Download the update package directly inside the application."
                : "在应用内直接拉取并下载更新包。",
            IconGlyphs.Download,
            () =>
            {
                dialogAction = UpdateDialogAction.InApp;
                dialog.Close();
            },
            isPrimary: false));

        optionPanel.Children.Add(CreateUpdateOptionButton(
            isEnglish ? "Open Official Release Page" : "前往官方发布页",
            isEnglish ? "Check release notes and official distribution files."
                : "查看版本说明与官方发布资源。",
            IconGlyphs.Globe,
            () =>
            {
                dialogAction = UpdateDialogAction.Official;
                dialog.Close();
            },
            isPrimary: false));

        Grid.SetRow(optionPanel, 1);
        cardGrid.Children.Add(optionPanel);

        var footerGrid = new Grid
        {
            Margin = new Thickness(0, 4, 0, 0)
        };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tipText = new TextBlock
        {
            Text = isEnglish
                ? "Tip: Cloud channel is recommended first."
                : "提示：建议优先选择网盘通道。",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeColors.TextMutedBrush,
            FontSize = 11
        };
        Grid.SetColumn(tipText, 0);
        footerGrid.Children.Add(tipText);

        var laterButton = new Button
        {
            Content = isEnglish ? "Later" : "稍后",
            MinWidth = 88,
            Height = 34,
            Padding = new Thickness(14, 6, 14, 6),
            Background = Brushes.Transparent,
            BorderBrush = ThemeColors.WhiteAlpha15Brush,
            BorderThickness = new Thickness(1),
            Foreground = ThemeColors.TextSecondaryBrush,
            Cursor = Cursors.Hand
        };
        laterButton.MouseEnter += (_, _) => laterButton.Background = ThemeColors.WhiteAlpha10Brush;
        laterButton.MouseLeave += (_, _) => laterButton.Background = Brushes.Transparent;
        laterButton.Click += (_, _) =>
        {
            dialogAction = UpdateDialogAction.None;
            dialog.Close();
        };
        Grid.SetColumn(laterButton, 1);
        footerGrid.Children.Add(laterButton);

        Grid.SetRow(footerGrid, 2);
        cardGrid.Children.Add(footerGrid);

        card.Child = cardGrid;
        overlay.Children.Add(card);
        dialog.Content = overlay;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialogAction = UpdateDialogAction.None;
                dialog.Close();
            }
        };
        _ = dialog.ShowDialog();
        return dialogAction;
    }

    private Button CreateUpdateOptionButton(
        string title,
        string description,
        string iconGlyph,
        Action onClick,
        bool isPrimary)
    {
        var normalTitleBrush = isPrimary ? ThemeColors.InfoLightBrush : ThemeColors.TextPrimaryBrush;
        var normalBorder = isPrimary ? ThemeColors.InfoAlpha50Brush : ThemeColors.WhiteAlpha15Brush;
        var hoverBorder = isPrimary ? ThemeColors.InfoBrush : ThemeColors.WhiteAlpha25Brush;
        var normalBackground = isPrimary ? ThemeColors.InfoAlpha15Brush : ThemeColors.WhiteAlpha08Brush;
        var hoverBackground = isPrimary ? ThemeColors.InfoAlpha30Brush : ThemeColors.WhiteAlpha10Brush;

        var button = new Button
        {
            MinHeight = 80,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(14, 10, 14, 10),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = normalBackground,
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = isPrimary ? ThemeColors.InfoAlpha30Brush : ThemeColors.WhiteAlpha10Brush,
            BorderBrush = isPrimary ? ThemeColors.InfoAlpha50Brush : ThemeColors.WhiteAlpha15Brush,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = iconGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Foreground = normalTitleBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(icon, 0);
        content.Children.Add(icon);

        var textPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = normalTitleBrush,
            TextAlignment = TextAlignment.Left
        };
        textPanel.Children.Add(titleText);
        textPanel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = ThemeColors.TextSecondaryBrush,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left
        });
        Grid.SetColumn(textPanel, 1);
        content.Children.Add(textPanel);

        button.Content = content;
        button.Click += (_, _) => onClick();
        button.MouseEnter += (_, _) =>
        {
            button.Background = hoverBackground;
            button.BorderBrush = hoverBorder;
            titleText.Foreground = isPrimary ? ThemeColors.InfoBrush : ThemeColors.TextPrimaryBrush;
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = normalBackground;
            button.BorderBrush = normalBorder;
            titleText.Foreground = normalTitleBrush;
        };
        return button;
    }

    private string ResolveDialogUpdateVersion(UpdateInfo? updateInfo)
    {
        static string Normalize(string value)
        {
            var trimmed = value.Trim().TrimStart('v', 'V');
            return string.IsNullOrWhiteSpace(trimmed) ? "v--" : $"v{trimmed}";
        }

        if (!string.IsNullOrWhiteSpace(updateInfo?.Version))
        {
            return Normalize(updateInfo.Version);
        }

        if (!string.IsNullOrWhiteSpace(_latestUpdateInfo?.Version))
        {
            return Normalize(_latestUpdateInfo.Version);
        }

        if (!string.IsNullOrWhiteSpace(_updateCheckService?.CurrentVersion))
        {
            return Normalize(_updateCheckService.CurrentVersion);
        }

        return "v--";
    }

    private void ExecuteUpdateDialogAction(UpdateDialogAction action)
    {
        switch (action)
        {
            case UpdateDialogAction.Cloud:
                OpenCloudLibraryButton_Click(this, new RoutedEventArgs());
                break;
            case UpdateDialogAction.InApp:
                AutoUpdateButton_Click(this, new RoutedEventArgs());
                break;
            case UpdateDialogAction.Official:
                OpenOfficialReleaseButton_Click(this, new RoutedEventArgs());
                break;
            default:
                ShowToast(LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? "Update dialog closed."
                    : "已关闭更新弹窗。");
                break;
        }
    }

    private async void AutoUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdate || _isDownloadingUpdate)
        {
            return;
        }

        var loc = L.Instance;
        if (_updateCheckService == null)
        {
            SetUpdateStatus(loc.Get("Settings.UpdateServiceUnavailable"), ThemeColors.WarningBrush);
            return;
        }

        if (_latestUpdateInfo is null)
        {
            SetUpdateStatus(loc.Get("Settings.AutoUpdatePreparing"), ThemeColors.InfoBrush);
            var checkResult = await PerformUpdateCheckAsync(triggeredByAutoCheck: false, showNoUpdateMessage: false);
            if (checkResult?.Data is null)
            {
                if (checkResult?.IsSuccess == true)
                {
                    SetUpdateStatus(loc.Get("Settings.AutoUpdateNoNewVersion"), ThemeColors.InfoBrush);
                }

                return;
            }
        }

        var updateInfo = _latestUpdateInfo;
        if (updateInfo is null)
        {
            return;
        }

        _isDownloadingUpdate = true;
        UpdateUpdateActionState();
        SetUpdateProgressState(true, loc.Get("Settings.DownloadingUpdate", updateInfo.Version));
        SetUpdateStatus(loc.Get("Settings.DownloadingUpdate", updateInfo.Version), ThemeColors.InfoBrush);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            var result = await _updateCheckService
                .DownloadUpdatePackageAsync(updateInfo.DownloadUrl, updateInfo.Version, timeoutCts.Token)
                .ConfigureAwait(true);

            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.Data))
            {
                _downloadedPackagePath = result.Data;
                SetUpdateStatus(loc.Get("Settings.UpdateDownloadSuccess", updateInfo.Version, result.Data), ThemeColors.SuccessBrush);
                SetUpdateProgressState(false);
            }
            else
            {
                SetUpdateStatus(result.Message, ThemeColors.WarningBrush);
                SetUpdateProgressState(false);
            }
        }
        catch (TaskCanceledException)
        {
            SetUpdateStatus(loc.Get("Settings.DownloadTimeout"), ThemeColors.WarningBrush);
            SetUpdateProgressState(false);
        }
        catch (Exception ex)
        {
            SetUpdateStatus(loc.Get("Settings.UpdateDownloadFailed", ex.Message), ThemeColors.WarningBrush);
            SetUpdateProgressState(false);
        }
        finally
        {
            _isDownloadingUpdate = false;
            UpdateUpdateActionState();
        }
    }

    private async Task TryAutoCheckForUpdatesAsync()
    {
        if (_updateCheckService == null || !ShouldRunAutoCheckNow())
        {
            return;
        }

        if (_settingsStore.TryUpdate(settings => settings.LastAutoUpdateCheckUtc = DateTime.UtcNow))
            _settings = _settingsStore.Current;
        UpdateAutoCheckMetaText();

        _ = await PerformUpdateCheckAsync(triggeredByAutoCheck: true, showNoUpdateMessage: false);
    }

    private bool ShouldRunAutoCheckNow()
    {
        if (_settings.LastAutoUpdateCheckUtc == DateTime.MinValue)
        {
            return true;
        }

        return (DateTime.UtcNow - _settings.LastAutoUpdateCheckUtc) >= TimeSpan.FromHours(AutoCheckIntervalHours);
    }

    private string PrepareRepositoryAndPersistIfNeeded()
    {
        var repository = NormalizeRepository(GitHubRepoTextBox.Text);
        GitHubRepoTextBox.Text = repository;
        if (!string.Equals(_settings.GitHubRepo, repository, StringComparison.Ordinal))
        {
            if (!_settingsStore.TryUpdate(settings => settings.GitHubRepo = repository))
            {
                GitHubRepoTextBox.Text = _settings.GitHubRepo;
                ShowToast(L.Instance.Get("Settings.SaveFailed"), isWarning: true);
                return _settings.GitHubRepo;
            }
            _settings = _settingsStore.Current;
            _latestUpdateInfo = null;
            _downloadedPackagePath = null;
            UpdateLatestReleaseInfo(null);
        }

        return repository;
    }

    private async Task<OperationResult<UpdateInfo?>?> PerformUpdateCheckAsync(bool triggeredByAutoCheck, bool showNoUpdateMessage)
    {
        if (_isCheckingUpdate || _isDownloadingUpdate)
        {
            return null;
        }

        var loc = L.Instance;
        if (_updateCheckService == null)
        {
            if (!triggeredByAutoCheck)
            {
                SetUpdateStatus(loc.Get("Settings.UpdateServiceUnavailable"), ThemeColors.WarningBrush);
            }

            return null;
        }

        var repository = PrepareRepositoryAndPersistIfNeeded();
        _isCheckingUpdate = true;
        UpdateUpdateActionState();
        SetUpdateProgressState(
            true,
            triggeredByAutoCheck ? loc.Get("Settings.AutoCheckStarted") : loc.Get("Settings.Checking"));
        SetUpdateStatus(
            triggeredByAutoCheck ? loc.Get("Settings.AutoCheckStarted") : loc.Get("Settings.Checking"),
            ThemeColors.InfoBrush);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(AsyncTimeoutSeconds));
        try
        {
            var result = await _updateCheckService
                .CheckForUpdateAsync(repository, includePreRelease: false, timeoutCts.Token)
                .ConfigureAwait(true);

            ApplyUpdateCheckResult(result, triggeredByAutoCheck, showNoUpdateMessage);
            return result;
        }
        catch (TaskCanceledException)
        {
            SetUpdateStatus(loc.Get("Settings.CheckUpdateTimeout"), ThemeColors.WarningBrush);
            SetUpdateProgressState(false);
            return null;
        }
        catch (Exception ex)
        {
            SetUpdateStatus(loc.Get("Settings.CheckUpdateFailed", ex.Message), ThemeColors.WarningBrush);
            SetUpdateProgressState(false);
            return null;
        }
        finally
        {
            SetUpdateProgressState(false);
            _isCheckingUpdate = false;
            UpdateUpdateActionState();
        }
    }

    private void ApplyUpdateCheckResult(OperationResult<UpdateInfo?> result, bool triggeredByAutoCheck, bool showNoUpdateMessage)
    {
        var loc = L.Instance;
        if (!result.IsSuccess)
        {
            _latestUpdateInfo = null;
            UpdateLatestReleaseInfo(null);
            SetUpdateStatus(result.Message, ThemeColors.WarningBrush);
            return;
        }

        if (result.Data is UpdateInfo updateInfo)
        {
            _latestUpdateInfo = updateInfo;
            UpdateLatestReleaseInfo(updateInfo);
            SetUpdateStatus(loc.Get("Settings.UpdateFoundStatus", updateInfo.Version), ThemeColors.SuccessBrush);
            return;
        }

        _latestUpdateInfo = null;
        UpdateLatestReleaseInfo(null);
        if (showNoUpdateMessage)
        {
            SetUpdateStatus(result.Message, triggeredByAutoCheck ? ThemeColors.InfoBrush : ThemeColors.SuccessBrush);
        }
        else if (triggeredByAutoCheck)
        {
            SetUpdateStatus(loc.Get("Settings.AutoCheckNoUpdate"), ThemeColors.TextMutedBrush);
        }
    }

    private void OpenCloudLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = L.Instance;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SupportPanUrl,
                UseShellExecute = true
            });

            SetUpdateStatus(loc.Get("Settings.CloudLibraryOpened"), ThemeColors.InfoBrush);
            if (!_hasShownCloudGuideTip)
            {
                _hasShownCloudGuideTip = true;
                MessageBox.Show(
                    loc.Get("Settings.CloudLibraryEntryGuideMessage"),
                    loc.Get("Settings.CloudLibraryEntryGuideTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            SetUpdateStatus(loc.Get("Settings.CloudLibraryOpenFailed", ex.Message), ThemeColors.WarningBrush);
        }
    }

    private void CopyCloudLinkButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = L.Instance;
        try
        {
            Clipboard.SetText(SupportPanUrl);
            SetUpdateStatus(loc.Get("Settings.CloudLibraryLinkCopied"), ThemeColors.SuccessBrush);
        }
        catch (Exception ex)
        {
            SetUpdateStatus(loc.Get("Settings.CloudLibraryLinkCopyFailed", ex.Message), ThemeColors.WarningBrush);
        }
    }

    private void OpenOfficialReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = L.Instance;
        var releaseUrl = ResolveOfficialReleaseUrl();
        if (string.IsNullOrWhiteSpace(releaseUrl))
        {
            SetUpdateStatus(loc.Get("Settings.OfficialReleaseUnavailable"), ThemeColors.WarningBrush);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = releaseUrl,
                UseShellExecute = true
            });

            SetUpdateStatus(loc.Get("Settings.OfficialReleaseOpened"), ThemeColors.InfoBrush);
        }
        catch (Exception ex)
        {
            SetUpdateStatus(loc.Get("Settings.OfficialReleaseOpenFailed", ex.Message), ThemeColors.WarningBrush);
        }
    }

    private string? ResolveOfficialReleaseUrl()
    {
        var repository = NormalizeRepository(_settings.GitHubRepo);
        if (!string.IsNullOrWhiteSpace(repository))
        {
            return $"https://github.com/{repository}/releases";
        }

        if (!string.IsNullOrWhiteSpace(_latestUpdateInfo?.OfficialReleaseUrl))
        {
            return _latestUpdateInfo.OfficialReleaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(_latestUpdateInfo?.DownloadUrl))
        {
            return _latestUpdateInfo.DownloadUrl;
        }

        return null;
    }

    private void OpenUpdateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = L.Instance;
        if (string.IsNullOrWhiteSpace(_downloadedPackagePath))
        {
            SetUpdateStatus(loc.Get("Settings.UpdatePackageMissing"), ThemeColors.WarningBrush);
            return;
        }

        try
        {
            if (File.Exists(_downloadedPackagePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_downloadedPackagePath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var folder = Path.GetDirectoryName(_downloadedPackagePath);
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    SetUpdateStatus(loc.Get("Settings.UpdatePackageMissing"), ThemeColors.WarningBrush);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            SetUpdateStatus(loc.Get("Settings.OpenUpdateFolderFailed", ex.Message), ThemeColors.WarningBrush);
        }
    }

    private void UpdateLanguageSelection(string lang)
    {
        var activeBg = ThemeColors.InfoAlpha15Brush;
        var activeBorder = ThemeColors.InfoAlpha30Brush;
        var activeText = ThemeColors.InfoBrush;

        if (lang == "zh-CN")
        {
            ChineseLanguageBtn.Background = activeBg;
            ChineseLanguageBtn.BorderBrush = activeBorder;
            ChineseLangText.Foreground = activeText;
            EnglishLanguageBtn.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.CardBackground");
            EnglishLanguageBtn.SetResourceReference(Border.BorderBrushProperty, "Theme.Brush.CardBorder");
            EnglishLangText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
        }
        else
        {
            EnglishLanguageBtn.Background = activeBg;
            EnglishLanguageBtn.BorderBrush = activeBorder;
            EnglishLangText.Foreground = activeText;
            ChineseLanguageBtn.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.CardBackground");
            ChineseLanguageBtn.SetResourceReference(Border.BorderBrushProperty, "Theme.Brush.CardBorder");
            ChineseLangText.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.TextSecondary");
        }
    }

    private static void UpdateSelectionCard(Border card, bool selected)
    {
        card.SetResourceReference(
            Border.BackgroundProperty,
            selected ? "Theme.Brush.CardBackgroundHover" : "Theme.Brush.CardBackgroundAlt");
        card.SetResourceReference(
            Border.BorderBrushProperty,
            selected ? "Theme.Brush.ControlAccent" : "Theme.Brush.CardBorder");
        card.BorderThickness = new Thickness(selected ? 2 : 1);
    }

    private void UpdateLogRetentionText()
    {
        var days = _settings.LogRetentionDays;
        var loc = L.Instance;
        LogRetentionValueText.Text = days == 0
            ? loc.Get("Settings.Forever")
            : loc.Get("Settings.DaysUnit", days);
    }

    private void ChineseLanguage_Click(object sender, MouseButtonEventArgs e) => SetLanguage("zh-CN");
    private void EnglishLanguage_Click(object sender, MouseButtonEventArgs e) => SetLanguage("en-US");

    private void SetLanguage(string lang)
    {
        if (!_isInitialized || _settings.Language == lang) return;
        var previous = _settings.Language;
        if (!_settingsStore.TryUpdate(settings => settings.Language = lang))
        {
            UpdateLanguageSelection(previous);
            ShowToast(L.Instance.Get("Settings.SaveFailed"), isWarning: true);
            return;
        }
        _settings = _settingsStore.Current;
        UpdateLanguageSelection(lang);
        LocalizationService.Instance.SetLanguage(lang);
        ShowToast(L.Instance.Get("Settings.LanguageChanged"));
    }

    private void LogRetentionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized) return;
        int index = (int)LogRetentionSlider.Value;
        if (index >= 0 && index < _logRetentionDays.Length)
        {
            var previous = _settings.LogRetentionDays;
            if (!_settingsStore.TryUpdate(settings => settings.LogRetentionDays = _logRetentionDays[index]))
            {
                var previousIndex = Array.IndexOf(_logRetentionDays, previous);
                if (previousIndex >= 0) LogRetentionSlider.Value = previousIndex;
                ShowToast(L.Instance.Get("Settings.SaveFailed"), isWarning: true);
                return;
            }
            _settings = _settingsStore.Current;
            CustomLogRetentionTextBox.Text = _settings.LogRetentionDays > 0
                ? _settings.LogRetentionDays.ToString()
                : string.Empty;
            UpdateLogRetentionText();
        }
    }

    private void ApplyCustomLogRetentionButton_Click(object sender, RoutedEventArgs e)
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        if (!int.TryParse(CustomLogRetentionTextBox.Text?.Trim(), out var days) || days is < 1 or > 3650)
        {
            ShowToast(
                isEnglish ? "Enter a whole number from 1 to 3650." : "请输入 1 到 3650 之间的整数。",
                isWarning: true);
            CustomLogRetentionTextBox.SelectAll();
            CustomLogRetentionTextBox.Focus();
            return;
        }

        if (!_settingsStore.TryUpdate(settings => settings.LogRetentionDays = days))
        {
            ShowToast(L.Instance.Get("Settings.SaveFailed"), isWarning: true);
            return;
        }

        _settings = _settingsStore.Current;
        var presetIndex = Array.IndexOf(_logRetentionDays, days);
        if (presetIndex >= 0)
        {
            LogRetentionSlider.Value = presetIndex;
        }
        UpdateLogRetentionText();
        ShowToast(isEnglish ? "Custom retention saved." : "自定义保留时间已保存。");
    }

    private void RiskWarningToggle_Click(object sender, RoutedEventArgs e)
    {
        ApplyInternalPreference(RiskWarningToggle, _settings.ShowRiskWarnings,
            settings => settings.ShowRiskWarnings = RiskWarningToggle.IsChecked == true);
    }

    private async void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        var enable = StartupToggle.IsChecked == true;
        StartupToggle.IsEnabled = false;
        try
        {
            _startupOwner ??= _startupOwnerFactory();
            var identity = new ManagedAutostartIdentity(
                ManagedAutostartKind.RegistryRun, "current-user", ManagedAutostartApplicationIdentity.ResourceId);
            var prepared = await _startupOwner.PrepareAsync(identity,
                enable ? ManagedAutostartAction.Enable : ManagedAutostartAction.Disable, CancellationToken.None);
            if (!prepared.Success || prepared.Plan is null)
                throw new InvalidOperationException(prepared.Message);

            var plan = prepared.Plan;
            var confirmed = ConfirmDialog.Show(Window.GetWindow(this), "确认开机自启动变更",
                $"目标：YuqiEngine 自有启动项\n动作：{plan.Action}\n风险：{plan.Risk}\n计划：{plan.PlanId:D}\n\n仅此计划在五分钟内有效。",
                IconGlyphs.Question);
            if (!confirmed) throw new OperationCanceledException("User cancelled the immutable startup plan.");

            var evidence = new ManagedAutostartConfirmationEvidence(plan.PlanId, plan.PlanHash, DateTimeOffset.UtcNow, true);
            var token = _startupOwner.Confirm(plan, evidence, TimeSpan.FromMinutes(2));
            var result = await _startupOwner.ExecuteAsync(plan, token, CancellationToken.None);
            if (!result.Success || result.Receipt is null || !result.Receipt.Verified)
                throw new InvalidOperationException(result.Message);
            if (!_settingsStore.TryUpdate(settings => settings.StartWithWindows = enable))
            {
                var restored = await _startupOwner.RestoreAsync(result.Receipt.ReceiptId, CancellationToken.None);
                if (!restored.Success)
                    throw new InvalidOperationException("设置保存失败，且启动项自动回滚未能验证。" + restored.Message);
                throw new InvalidOperationException("设置保存失败，启动项已恢复到变更前状态。");
            }
            _settings = _settingsStore.Current;
            await RefreshStartupTruthAsync();
        }
        catch (OperationCanceledException)
        {
            await RefreshStartupTruthAsync();
            ShowToast(LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "Autostart was not changed."
                : "开机自启动未更改。");
        }
        catch (Exception ex)
        {
            await RefreshStartupTruthAsync();
            ShowToast($"{L.Instance.Get("Settings.StartupRegistryFailed")}: {ex.Message}", isWarning: true);
        }
        finally { StartupToggle.IsEnabled = true; }
    }

    private void TrayToggle_Click(object sender, RoutedEventArgs e)
    {
        ApplyInternalPreference(TrayToggle, _settings.MinimizeToTray,
            settings => settings.MinimizeToTray = TrayToggle.IsChecked == true);
    }

    private void SoundToggle_Click(object sender, RoutedEventArgs e)
    {
        ApplyInternalPreference(SoundToggle, _settings.EnableSoundNotification,
            settings => settings.EnableSoundNotification = SoundToggle.IsChecked == true);
    }

    private void ApplyInternalPreference(CheckBox toggle, bool previous, Action<AppSettings> update)
    {
        if (_settingsStore.TryUpdate(update))
        {
            _settings = _settingsStore.Current;
            return;
        }

        toggle.IsChecked = previous;
        ShowToast(L.Instance.Get("Settings.SaveFailed"), isWarning: true);
    }

    private async Task RefreshStartupTruthAsync()
    {
        try
        {
            _startupOwner ??= _startupOwnerFactory();
            var identity = new ManagedAutostartIdentity(
                ManagedAutostartKind.RegistryRun, "current-user", ManagedAutostartApplicationIdentity.ResourceId);
            var snapshot = await _startupOwner.InspectAsync(identity, CancellationToken.None);
            var enabled = snapshot.Exists && snapshot.Enabled;
            StartupToggle.IsChecked = enabled;
            StartupToggle.IsEnabled = true;
            StartupToggle.ToolTip = null;
            if (_settings.StartWithWindows != enabled &&
                _settingsStore.TryUpdate(settings => settings.StartWithWindows = enabled))
            {
                _settings = _settingsStore.Current;
            }
        }
        catch (Exception ex)
        {
            StartupToggle.IsEnabled = false;
            StartupToggle.ToolTip = $"无法读取 YuqiEngine 自有启动项：{ex.Message}";
        }
    }

    private static string NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool TryEnsureDirectoryWritable(string targetPath, out string normalizedPath, out string errorMessage)
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        if (targetPath.TrimStart().StartsWith(@"\\", StringComparison.Ordinal))
        {
            normalizedPath = string.Empty;
            errorMessage = isEnglish
                ? "Network/UNC folders are not supported; choose a local writable folder."
                : "不支持网络/UNC 目录，请选择本地可写目录。";
            return false;
        }
        normalizedPath = NormalizeDirectoryPath(targetPath);
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            errorMessage = isEnglish ? "Invalid directory path." : "目录路径无效。";
            return false;
        }

        try
        {
            var root = Path.GetPathRoot(normalizedPath);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (normalizedRoot.EndsWith(":", StringComparison.OrdinalIgnoreCase))
                {
                    var drive = new DriveInfo(root);
                    if (drive.DriveType == DriveType.Network)
                    {
                        errorMessage = isEnglish
                            ? "Network drives are not supported; choose a local writable folder."
                            : "不支持网络驱动器，请选择本地可写目录。";
                        return false;
                    }
                    if (!drive.IsReady)
                    {
                        errorMessage = isEnglish
                            ? $"Drive is not ready: {normalizedRoot}"
                            : $"驱动器不可用：{normalizedRoot}";
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errorMessage = isEnglish
                ? $"Drive validation failed: {ex.Message}"
                : $"驱动器检测失败：{ex.Message}";
            return false;
        }

        if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(normalizedPath, out var createError))
        {
            errorMessage = isEnglish
                ? $"Cannot create directory: {createError}"
                : $"无法创建目录：{createError}";
            return false;
        }

        var probeFile = Path.Combine(normalizedPath, $".yuqi_write_probe_{Guid.NewGuid():N}.tmp");
        if (!GameOptimizer.Core.Services.SafeFileWriter.TryWriteAllText(probeFile, "yuqi-write-probe"))
        {
            errorMessage = isEnglish ? "Directory is not writable." : "目录不可写。";
            return false;
        }

        try
        {
            File.Delete(probeFile);
        }
        catch
        {
            // Ignore cleanup failures for probe files.
        }

        return true;
    }

    private string GetDefaultLogBackupDirectory()
    {
        return AppPaths.DefaultLogsDir;
    }

    private string GetEffectiveLogBackupDirectory()
    {
        var configuredPath = NormalizeDirectoryPath(_settings.LogBackupDirectory);
        return string.IsNullOrWhiteSpace(configuredPath) ? GetDefaultLogBackupDirectory() : configuredPath;
    }

    private void UpdateLogBackupDirectoryText()
    {
        var effectivePath = GetEffectiveLogBackupDirectory();
        var usingDefault = string.IsNullOrWhiteSpace(NormalizeDirectoryPath(_settings.LogBackupDirectory));
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        LogBackupDirectoryValueText.Text = usingDefault
            ? $"{effectivePath}{(isEnglish ? " (Default)" : "（默认）")}"
            : effectivePath;
        LogBackupDirectoryValueText.ToolTip = effectivePath;

        LogBackupDirectoryModeBadgeText.Text = usingDefault
            ? (isEnglish ? "Default" : "默认")
            : (isEnglish ? "Custom" : "自定义");
        LogBackupDirectoryModeBadge.Background = usingDefault
            ? ThemeColors.InfoAlpha15Brush
            : ThemeColors.SuccessAlpha15Brush;
        LogBackupDirectoryModeBadge.BorderBrush = usingDefault
            ? ThemeColors.InfoAlpha30Brush
            : ThemeColors.SuccessAlpha20Brush;

        var directoryHealthy = TryEnsureDirectoryWritable(effectivePath, out _, out var directoryError);
        LogBackupDirectoryHealthText.Text = directoryHealthy
            ? (isEnglish ? "Directory is writable" : "目录可写")
            : (isEnglish ? $"Directory unavailable: {directoryError}" : $"目录不可用：{directoryError}");
        LogBackupDirectoryHealthText.Foreground = directoryHealthy
            ? ThemeColors.SuccessBrush
            : ThemeColors.WarningBrush;
    }

    private bool TrySaveLogBackupDirectory(string targetPath, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!TryEnsureDirectoryWritable(targetPath, out var normalizedPath, out errorMessage))
        {
            return false;
        }

        var defaultPath = GetDefaultLogBackupDirectory();
        var value = string.Equals(normalizedPath, defaultPath, StringComparison.OrdinalIgnoreCase)
            ? string.Empty : normalizedPath;
        if (!_settingsStore.TryUpdate(settings => settings.LogBackupDirectory = value))
        {
            errorMessage = LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "The directory is writable, but the setting could not be saved."
                : "目录可写，但设置保存失败。";
            return false;
        }
        _settings = _settingsStore.Current;
        UpdateLogBackupDirectoryText();
        return true;
    }

    private void ChooseLogBackupDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var initialPath = GetEffectiveLogBackupDirectory();
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? "Select log backup output directory"
                    : "选择日志备份输出目录",
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(initialPath) ? initialPath : GetDefaultLogBackupDirectory()
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return;
            }

            if (!TrySaveLogBackupDirectory(dialog.SelectedPath, out var errorMessage))
            {
                InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"), errorMessage, IconGlyphs.Warning);
                return;
            }

            ShowToast(LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "Log directory saved. Live logs will switch after the next restart."
                : "日志目录已保存；实时日志将在下次启动后切换。");
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"),
                LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? $"Failed to select directory: {ex.Message}"
                    : $"选择目录失败：{ex.Message}",
                IconGlyphs.Warning);
        }
    }

    private void UseDefaultLogBackupDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var defaultPath = GetDefaultLogBackupDirectory();
            if (!TryEnsureDirectoryWritable(defaultPath, out _, out var error))
            {
                InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"),
                    LocalizationService.Instance.CurrentLanguage == "en-US"
                        ? $"Cannot create default directory: {error}"
                        : $"无法创建默认目录：{error}",
                    IconGlyphs.Warning);
                return;
            }

            if (!_settingsStore.TryUpdate(settings => settings.LogBackupDirectory = string.Empty))
            {
                ShowToast(L.Instance.Get("Settings.SaveFailed"), isWarning: true);
                return;
            }
            _settings = _settingsStore.Current;
            UpdateLogBackupDirectoryText();
            ShowToast(LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "Switched to the default app data log directory."
                : "已切换到默认应用数据日志目录。");
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"),
                LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? $"Failed to switch directory: {ex.Message}"
                    : $"切换目录失败：{ex.Message}",
                IconGlyphs.Warning);
        }
    }

    private void OpenLogBackupDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = GetEffectiveLogBackupDirectory();
            if (!TryEnsureDirectoryWritable(directory, out var normalizedDirectory, out var error))
            {
                InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"),
                    LocalizationService.Instance.CurrentLanguage == "en-US"
                        ? $"Cannot access directory: {error}"
                        : $"无法访问目录：{error}",
                    IconGlyphs.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", normalizedDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"),
                LocalizationService.Instance.CurrentLanguage == "en-US"
                    ? $"Failed to open directory: {ex.Message}"
                    : $"打开目录失败：{ex.Message}",
                IconGlyphs.Warning);
        }
    }

    private void AuthorButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AboutDialog
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage][AuthorButton_Click] {ex.Message}");
        }
    }

    private void OpenLogFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = GetEffectiveLogBackupDirectory();
            // P3-004: 使用 SafeDirectoryOperations 代替裸露的 Directory.CreateDirectory。
            if (!Directory.Exists(logPath))
            {
                if (!GameOptimizer.Core.Utilities.SafeDirectoryOperations.TryCreateDirectory(logPath, out var error))
                {
                    InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"), L.Instance.Get("Settings.CreateLogFolderFailed", error ?? "Unknown error"), IconGlyphs.Warning);
                    return;
                }
            }
            Process.Start(new ProcessStartInfo("explorer.exe", logPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"), L.Instance.Get("Settings.OpenLogFolderFailed", ex.Message), IconGlyphs.Warning);
        }
    }

    private void UpdateProblemPackagePreview()
    {
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        if (_diagnosticPackageService == null)
        {
            ProblemPackageStatusText.Text = isEnglish
                ? "The problem package service is unavailable."
                : "问题包服务当前不可用。";
            GenerateProblemPackageButton.IsEnabled = false;
            return;
        }

        try
        {
            var preview = _diagnosticPackageService.GetPreview();
            ProblemPackageStatusText.Text = preview.LogFileCount == 0
                ? (isEnglish ? "No runtime logs are currently available; a minimal environment report can still be generated."
                    : "当前没有可收集的运行日志，仍可生成最小环境报告。")
                : (isEnglish
                    ? $"Ready to collect {preview.LogFileCount} log(s), {preview.TotalSizeText}."
                    : $"当前可收集 {preview.LogFileCount} 个运行日志，共 {preview.TotalSizeText}。");
            GenerateProblemPackageButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage][ProblemPackagePreview] {ex.Message}");
            ProblemPackageStatusText.Text = isEnglish
                ? "Unable to inspect runtime logs."
                : "无法检查当前运行日志。";
            GenerateProblemPackageButton.IsEnabled = false;
        }
    }

    private async void GenerateProblemPackageButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var isEnglish = LocalizationService.Instance.CurrentLanguage == "en-US";
        if (_diagnosticPackageService == null)
        {
            InfoDialog.Show(owner, L.Instance.Get("Common.Error"),
                isEnglish ? "The problem package service is unavailable." : "问题包服务当前不可用。",
                IconGlyphs.Warning);
            return;
        }

        var preview = _diagnosticPackageService.GetPreview();
        var visibleNames = new List<string>();
        for (var index = 0; index < preview.LogFileNames.Count && index < 6; index++)
            visibleNames.Add(preview.LogFileNames[index]);
        var fileSummary = visibleNames.Count == 0
            ? (isEnglish ? "No runtime log files" : "无运行日志文件")
            : string.Join("\n", visibleNames.ConvertAll(name => $"• {name}"));
        if (preview.LogFileNames.Count > visibleNames.Count)
            fileSummary += isEnglish
                ? $"\n• and {preview.LogFileNames.Count - visibleNames.Count} more"
                : $"\n• 以及其他 {preview.LogFileNames.Count - visibleNames.Count} 个";

        var confirmationMessage = isEnglish
            ? $"The package will contain {preview.LogFileCount} whitelisted runtime log(s) ({preview.TotalSizeText}) and minimal version information.\n\n{fileSummary}\n\nSettings, backups, keys, and user documents are excluded.\nThe package is not uploaded automatically. You can inspect the ZIP before sending it to the author."
            : $"将收集 {preview.LogFileCount} 个白名单运行日志（{preview.TotalSizeText}）和最小版本信息。\n\n{fileSummary}\n\n{preview.PrivacyNotice}\n问题包不会自动上传；发给作者前可自行检查 ZIP 内容。";
        if (!ConfirmDialog.Show(owner,
                isEnglish ? "Generate problem package" : "生成问题包",
                confirmationMessage,
                IconGlyphs.Package))
        {
            return;
        }

        GenerateProblemPackageButton.IsEnabled = false;
        ProblemPackageStatusText.Text = isEnglish
            ? "Flushing and packaging runtime logs…"
            : "正在刷新并打包运行日志…";
        try
        {
            var result = await _diagnosticPackageService.CreateDiagnosticPackageAsync(CancellationToken.None);
            if (!result.Success)
            {
                ProblemPackageStatusText.Text = isEnglish ? "Problem package generation failed." : "问题包生成失败。";
                InfoDialog.Show(owner, L.Instance.Get("Common.Error"), result.Error, IconGlyphs.Warning);
                return;
            }

            ProblemPackageStatusText.Text = isEnglish
                ? $"Generated locally (not uploaded): {result.PackagePath}"
                : $"已生成（未自动上传）：{result.PackagePath}";
            var openFolder = ConfirmDialog.Show(owner,
                isEnglish ? "Problem package generated" : "问题包已生成",
                isEnglish
                    ? $"Saved to:\n{result.PackagePath}\n\nOpen its containing folder now?"
                    : $"已保存到：\n{result.PackagePath}\n\n是否立即打开所在位置？",
                IconGlyphs.Check);
            if (openFolder)
            {
                var (_, processError) = SafeProcessHelper.StartProcess(new ProcessStartInfo(
                    "explorer.exe", $"/select,\"{result.PackagePath}\"")
                {
                    UseShellExecute = true
                });
                if (!string.IsNullOrWhiteSpace(processError))
                {
                    InfoDialog.Show(owner, L.Instance.Get("Common.Error"), processError, IconGlyphs.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage][GenerateProblemPackage] {ex}");
            ProblemPackageStatusText.Text = isEnglish ? "Problem package generation failed." : "问题包生成失败。";
            InfoDialog.Show(owner, L.Instance.Get("Common.Error"), ex.Message, IconGlyphs.Warning);
        }
        finally
        {
            GenerateProblemPackageButton.IsEnabled = true;
        }
    }

    private async void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        var decision = await UnavailableFileShellSafetyOwner.Instance.EvaluateAsync(
            new FileShellSafetyRequest("settings.clear-logs", FileShellMutationKind.QuarantineFiles, AppPaths.LogsDir),
            CancellationToken.None);
        InfoDialog.Show(
            Window.GetWindow(this),
            "安全阻断",
            $"{decision.Status}: {decision.Message}\n\n日志不会被永久删除；请等待具备预览、隔离清单和恢复能力的日志保留入口。",
            IconGlyphs.Warning);
    }

    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // P0-012: 增强重置警告,显示详细的数据丢失信息
        var confirmed = ConfirmDialog.Show(
            Window.GetWindow(this),
            L.Instance.Get("Settings.ResetWarningDetailed"),
            L.Instance.Get("Settings.ResetWillLose"),
            IconGlyphs.Warning);
        
        if (confirmed)
        {
            var defaults = new AppSettings();
            var startupTruth = StartupToggle.IsChecked == true;
            var saved = _settingsStore.TryUpdate(settings =>
            {
                foreach (var property in typeof(AppSettings).GetProperties())
                {
                    if (!property.CanRead || !property.CanWrite || property.Name == nameof(AppSettings.StartWithWindows))
                        continue;
                    property.SetValue(settings, property.GetValue(defaults));
                }
                settings.StartWithWindows = startupTruth;
            });
            if (!saved)
            {
                ShowToast(L.Instance.Get("Settings.SaveFailed"), isWarning: true);
                return;
            }
            _settings = _settingsStore.Current;
            LoadSettingsToUI();
            LocalizationService.Instance.SetLanguage(_settings.Language);
            ShowToast(L.Instance.Get("Settings.ResetComplete"));
        }
    }

    private async void ExportConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button != null) button.IsEnabled = false;
        
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = L.Instance.Get("Settings.ExportConfig"),
                Filter = "JSON (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"YuqiEngine_OptimizationProfile_{DateTime.Now:yyyyMMdd}"
            };

            if (dialog.ShowDialog() == true)
            {
                var result = await _configurationExportService.ExportAsync(dialog.FileName);
                if (result.IsSuccess)
                    InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Success"), L.Instance.Get("Settings.ExportSuccess", dialog.FileName), IconGlyphs.Check);
                else
                    InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Settings.ExportConfig"), L.Instance.Get("Settings.ExportFailed", result.Message), IconGlyphs.Warning);
            }
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"), L.Instance.Get("Settings.ExportFailed", ex.Message), IconGlyphs.Warning);
        }
        finally
        {
            if (button != null) button.IsEnabled = true;
        }
    }

    private async void ImportConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button != null) button.IsEnabled = false;
        
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = L.Instance.Get("Settings.ImportConfig"),
                Filter = "JSON (*.json)|*.json",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog() == true)
            {
                // P3-POWER-003: 验证导入配置文件
                if (!System.IO.File.Exists(dialog.FileName))
                {
                    InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"), L.Instance.Get("Settings.ImportFileNotFound"), IconGlyphs.Warning);
                    return;
                }
                
                // 验证JSON格式
                try
                {
                    var jsonContent = await System.IO.File.ReadAllTextAsync(dialog.FileName);
                    var testParse = System.Text.Json.JsonDocument.Parse(jsonContent);
                    testParse.Dispose();
                }
                catch (System.Text.Json.JsonException)
                {
                    InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"), L.Instance.Get("Settings.ImportInvalidFormat"), IconGlyphs.Warning);
                    return;
                }
                catch (Exception ex)
                {
                    InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"), L.Instance.Get("Settings.ImportValidationFailed", ex.Message), IconGlyphs.Warning);
                    return;
                }
                
                var confirm = ConfirmDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Confirm"), L.Instance.Get("Settings.ImportConfirm"), IconGlyphs.Question);
                if (!confirm) return;

                var result = await _configurationExportService.ImportAsync(dialog.FileName);
                if (result.IsSuccess && result.Data != null)
                {
                    var applyResult = await _configurationExportService.ApplyConfigurationAsync(result.Data);
                    if (applyResult.IsSuccess)
                    {
                        InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Success"), L.Instance.Get("Settings.ImportSuccess"), IconGlyphs.Check);
                    }
                    else
                    {
                        var message = string.IsNullOrWhiteSpace(applyResult.Detail)
                            ? applyResult.Message
                            : $"{applyResult.Message}\n{applyResult.Detail}";
                        InfoDialog.Show(
                            Window.GetWindow(this),
                            L.Instance.Get("Settings.ImportConfig"),
                            message,
                            IconGlyphs.Warning);
                    }
                }
                else
                {
                    InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Settings.ImportConfig"), L.Instance.Get("Settings.ImportFailed", result.Message), IconGlyphs.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            InfoDialog.Show(Window.GetWindow(this), L.Instance.Get("Common.Error"), L.Instance.Get("Settings.ImportFailed", ex.Message), IconGlyphs.Warning);
        }
        finally
        {
            if (button != null) button.IsEnabled = true;
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var aboutDialog = new AboutDialog { Owner = Window.GetWindow(this) };
            aboutDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage][AboutButton_Click] {ex.Message}");
        }
    }

    private void PeripheralAdvancedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new PeripheralAdvancedDialog { Owner = Window.GetWindow(this) };
            var result = dialog.ShowDialog();
            
            if (result == true && dialog.IsConfirmed)
            {
                ShowToast(L.Instance.Get("Settings.PeripheralSettingsSaved"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsPage] Failed to open peripheral settings: {ex.Message}");
            ShowToast(L.Instance.Get("Settings.PeripheralSettingsFailed", ex.Message), isWarning: true);
        }
    }
}




