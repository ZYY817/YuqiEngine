// File: GameOptimizer.Shell/Views/FirstRunBaselineDialog.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Controls;
using GameOptimizer.Shell.Services.Baseline;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Views;

public partial class FirstRunBaselineDialog : Window
{
    private const string ModifiedSystemCompatibilityWarning = "系统被自行精简、封装、深度改装，或已被其他工具修改策略、服务、网络和电源配置时，部分检测、优化与恢复可能不可用或不兼容。";
    private const string ModifiedSystemRiskWarning = "精简版或第三方优化痕迹较多时，请先停止批量优化；无法通过预检的项目不会被强行执行。";

    private enum UiStage
    {
        Intro,
        Running,
        Manual,
        Failed,
        Completed
    }

    private static readonly IReadOnlyDictionary<string, string> DomainLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["D01"] = "系统还原点",
        ["D02"] = "软件核心快照",
        ["D03"] = "受保护的注册表设置",
        ["D04"] = "应用配置",
        ["D05"] = "业务档案",
        ["D06"] = "核心加速配置",
        ["D07"] = "清理历史备份",
        ["D08"] = "网络配置",
        ["D09"] = "电源计划",
        ["D10"] = "启动配置",
        ["D11"] = "系统服务状态",
        ["D12"] = "计划任务状态",
        ["D13"] = "系统还原保护",
        ["D14"] = "诊断与审计",
        ["D15"] = "本地策略",
        ["D16"] = "安全防护",
        ["D17"] = "启动项",
        ["D18"] = "高级网络",
        ["D19"] = "系统功能开关",
        ["D20"] = "渲染与显示",
        ["D21"] = "内存与分页",
        ["D22"] = "更新维护策略"
    };

    private readonly IFirstRunBaselineOrchestrator _baselineOrchestrator;
    private readonly IBaselineGateService _baselineGateService;
    private readonly IBaselineManualGuideService _manualGuideService;
    private readonly FirstProtectionPreferenceStore _preferenceStore;
    private readonly IDiagnosticLogService? _diagnosticLogService;
    private readonly ObservableCollection<string> _logItems = new();

    private UiStage _stage = UiStage.Intro;
    private bool _isRunning;
    private string _lastEventKey = string.Empty;
    private int _introStep = 1;
    private int _riskAcknowledgementEpoch;
    private bool _riskAcknowledged;
    private bool _allowClose;
    private bool _handlingCloseRequest;
    private bool _authorInfoShown;
    private bool _systemRestorePointPending;

    public FirstRunBaselineDialog(
        IFirstRunBaselineOrchestrator baselineOrchestrator,
        IBaselineGateService baselineGateService,
        IBaselineManualGuideService manualGuideService,
        FirstProtectionPreferenceStore preferenceStore,
        IDiagnosticLogService? diagnosticLogService)
    {
        _baselineOrchestrator = baselineOrchestrator ?? throw new ArgumentNullException(nameof(baselineOrchestrator));
        _baselineGateService = baselineGateService ?? throw new ArgumentNullException(nameof(baselineGateService));
        _manualGuideService = manualGuideService ?? throw new ArgumentNullException(nameof(manualGuideService));
        _preferenceStore = preferenceStore ?? throw new ArgumentNullException(nameof(preferenceStore));
        _diagnosticLogService = diagnosticLogService;

        InitializeComponent();
        LogListBox.ItemsSource = _logItems;
        Loaded += FirstRunBaselineDialog_Loaded;
        Closing += FirstRunBaselineDialog_Closing;

        ApplyPlainLanguageCopy();
        SetStage(UiStage.Intro);
    }

    private void ApplyPlainLanguageCopy()
    {
        Title = Text("FirstProtection.Title", "首次使用说明与安全备份");
        DialogHeader.Title = Text("FirstProtection.Header", "首次使用说明与安全备份");
        DialogHeader.Subtitle = Text("FirstProtection.Subtitle", "Yuqi Engine");
        BackupPathText.Text = Path.Combine(AppPaths.BackupsDir, "Baseline", "packages");

        ManualTitleText.Text = "还差一步手动操作";
        FailedTitleText.Text = "备份没有完成";
        CompletedTitleText.Text = "备份已完成，可以正常使用软件";

        IntroExitBtn.Content = Text("FirstProtection.Skip", "暂时跳过");
        StartBackupBtn.Content = "→　开始创建备份";
        ManualOpenSettingsBtn.Content = "打开系统保护";
        ManualRecheckBtn.Content = "完成后重新检测";
        ManualExitBtn.Content = Text("FirstProtection.Continue", "暂不备份，继续使用");
        FailedRetryBtn.Content = "重新备份";
        FailedExitBtn.Content = Text("FirstProtection.Continue", "暂不备份，继续使用");
        CompletedEnterBtn.Content = "进入软件";

        StepTextBlock.Text = "等待开始";
        StatusTextBlock.Text = "点击“开始备份”后将执行首次安全基线备份。";

        ReplaceStaticText("当前为测试版本", "使用前请先备份");
        ReplaceStaticText("功能、兼容性和恢复链仍在持续验证，可能存在未发现的问题。请在使用任何优化前完成安全备份，并优先在可恢复环境中测试。", "不同电脑的硬件、系统和驱动可能不同。遇到不可用的功能时，软件会提示原因，不会强行修改。");
        ReplaceStaticText("现在创建首次全量安全备份", "先为软件能改动的设置创建备份");
        ReplaceStaticText("创建完成后可在备份管理中查看、验证，并通过受控流程恢复。它不能替代 Windows 系统还原点：请在开始前自行创建一个还原点。", "备份可在“备份管理”中查看，并在需要时恢复本软件改动过的设置。它不能替代 Windows 系统还原点，请同时自行创建一个还原点。");
        ReplaceStaticText("首次安全备份会保护这些范围", "备份会记录这些内容");
        ReplaceStaticText("只有通过真实预检和收集器验证的范围，才会被标记为可恢复。", "软件只会记录已经确认可以恢复的项目。不能安全恢复的项目不会被加入备份。");
        ReplaceStaticText("按现有执行链回读验证的应用配置与恢复记录。", "保存软件中的设置和已做的更改，以便需要时恢复。");
        ReplaceStaticText("没有安全恢复链的项目不会被伪造成一键恢复；电源异常时请切回优化前的原电源计划。若没有可用备份或还原依据且系统仍无法恢复，可能只能重装系统。", "不能安全恢复的项目不会被假装成可以一键恢复。电源异常时请切回原来的电源计划；没有备份或系统还原点时，部分问题可能无法自动修复。");
        ReplaceStaticText("首次备份暂时停在安全门禁。完成提示的人工处理后可以重新检测。", "备份暂时无法继续。请按提示完成必要操作后，再回来重新检测。");
    }

    private void ReplaceStaticText(string source, string replacement)
    {
        foreach (TextBlock textBlock in FindVisualChildren<TextBlock>(this))
        {
            if (string.Equals(textBlock.Text, source, StringComparison.Ordinal))
            {
                textBlock.Text = replacement;
                return;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string Text(string key, string fallback) =>
        LocalizationService.Instance.GetOrDefault(key, fallback);

    private async void FirstRunBaselineDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var manifest = await _baselineOrchestrator.GetCurrentManifestAsync().ConfigureAwait(true);
            var safetyState = await _baselineGateService.GetStateAsync().ConfigureAwait(true);
            UpdateManifestStatus(manifest, safetyState);

            if (safetyState.FirstRunState != FirstRunBaselineState.CompletedVerified)
            {
                GateStatusBadge.Text = "尚未备份";
                GateStatusBadge.Status = BadgeStatus.Warning;
                return;
            }

            MainProgressBar.IsIndeterminate = false;
            MainProgressBar.Value = 100;
            GateStatusBadge.Text = "备份已完成";
            GateStatusBadge.Status = BadgeStatus.Success;
            StepTextBlock.Text = "已完成";
            StatusTextBlock.Text = "检测到本机已有有效安全基线，无需重复备份。";
                CompletedDetailTextBlock.Text = "系统已具备可恢复底线，本次可直接进入软件。";
                await _preferenceStore.RecordCompletedAsync().ConfigureAwait(true);
                SetStage(UiStage.Completed);
        }
        catch (Exception ex)
        {
            AppendLog($"[初始化] 状态检查失败：{ex.Message}");
            GateStatusBadge.Text = "状态读取失败";
            GateStatusBadge.Status = BadgeStatus.Error;
            FailedReasonTextBlock.Text = NormalizeFailureReason(ex.Message);
            SetStage(UiStage.Failed);
        }
    }

    private void UpdateManifestStatus(BaselinePackageManifest? manifest, BaselineSafetyState safetyState)
    {
        ManifestPackageText.Text = manifest is null || string.IsNullOrWhiteSpace(manifest.PackageId)
            ? "未发现已登记的基线包"
            : manifest.PackageId;
        ManifestHashText.Text = string.IsNullOrWhiteSpace(safetyState.AuthorizedManifestSha256)
            ? "尚未完成首次备份检查"
            : safetyState.AuthorizedManifestSha256;
        ManifestIntegrityText.Text = manifest is null
            ? "未验证"
            : string.IsNullOrWhiteSpace(manifest.IntegrityState) ? "Unknown" : manifest.IntegrityState;
        ManifestRecoverabilityText.Text = manifest is null
            ? "未检查"
            : $"{manifest.RecoverabilityCheckState}；最近检查：{(manifest.LastRecoverabilityCheckUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "暂无")}";
        GateStatusBadge.Text = safetyState.FirstRunState switch
        {
            FirstRunBaselineState.CompletedVerified => "备份已完成",
            FirstRunBaselineState.ManualInterventionRequired => "需要人工处理",
            FirstRunBaselineState.PartialFailed => "部分失败",
            FirstRunBaselineState.Corrupted => "备份文件异常",
            _ => "尚未备份"
        };
        GateStatusBadge.Status = safetyState.FirstRunState switch
        {
            FirstRunBaselineState.CompletedVerified => BadgeStatus.Info,
            FirstRunBaselineState.ManualInterventionRequired => BadgeStatus.Warning,
            FirstRunBaselineState.PartialFailed or FirstRunBaselineState.Corrupted => BadgeStatus.Error,
            _ => BadgeStatus.Loading
        };
    }

    private void FirstRunBaselineDialog_Closing(object? sender, CancelEventArgs e)
    {
        if (_isRunning)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "正在执行备份，请等待当前步骤完成后再退出。",
                "备份进行中",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_allowClose || _handlingCloseRequest)
        {
            return;
        }

        // Alt+F4 和系统关闭按钮也不能绕过“跳过首次备份”的确认与作者说明。
        e.Cancel = true;
        _handlingCloseRequest = true;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (_stage == UiStage.Completed)
                {
                    ShowAuthorInfoBeforeEnteringSoftware();
                    CloseAfterAuthorInfo();
                    return;
                }

                await DeferBaselineAndCloseAsync("用户通过窗口关闭操作暂时跳过首次安全备份。")
                    .ConfigureAwait(true);
            }
            finally
            {
                _handlingCloseRequest = false;
            }
        });
    }

    private async Task RunBaselineAsync()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        try
        {
            SetStage(UiStage.Running);
            MainProgressBar.IsIndeterminate = true;
            MainProgressBar.Value = 0;
            StepTextBlock.Text = "准备中";
            StatusTextBlock.Text = "正在启动首次安全备份流程...";
            AppendLog("[执行] 已开始首次安全备份。");

            var progress = new Progress<BaselineProgressEvent>(evt =>
            {
                var message = evt.Message ?? string.Empty;
                var eventKey = $"{evt.Step}|{evt.DomainId}|{evt.DomainStatus}|{message}";
                if (!string.Equals(_lastEventKey, eventKey, StringComparison.Ordinal))
                {
                    _lastEventKey = eventKey;
                    AppendLog(BuildFriendlyLog(evt, message));
                }

                StepTextBlock.Text = ResolveStepText(evt.Step);
                StatusTextBlock.Text = BuildFriendlyStatus(evt, message);
                var progressValue = ResolveProgress(evt);
                MainProgressBar.IsIndeterminate = !progressValue.HasValue;
                if (progressValue.HasValue)
                {
                    MainProgressBar.Value = progressValue.Value;
                }
                _diagnosticLogService?.Append(
                    LogLevel.Information,
                    "FirstRunBaselineDialog.Progress",
                    $"{evt.Step} / {evt.DomainId ?? "-"} / {evt.DomainStatus?.ToString() ?? "-"}",
                    message);
            });

            var result = await _baselineOrchestrator.RunAsync(progress).ConfigureAwait(true);
            if (result.Success)
            {
                await _preferenceStore.RecordCompletedAsync().ConfigureAwait(true);
                _systemRestorePointPending = result.RuntimeState.Domains.Any(domain =>
                    string.Equals(domain.DomainId, "D13", StringComparison.OrdinalIgnoreCase)
                    && domain.Status != BaselineDomainStatus.Succeeded);
                MainProgressBar.IsIndeterminate = false;
                MainProgressBar.Value = 100;
                StepTextBlock.Text = "完成";
                StatusTextBlock.Text = "备份已完成，并通过完整性校验。";
                CompletedDetailTextBlock.Text = _systemRestorePointPending
                    ? "软件自身的备份已完成并通过校验。Windows 系统还原点暂未创建；建议稍后从“系统保护”手动创建，不影响本次软件备份。"
                    : string.IsNullOrWhiteSpace(result.Message)
                    ? $"安全基线建立成功（{DateTime.Now:yyyy-MM-dd HH:mm:ss}）。进入后会自动打开备份中心，你可以查看首次基线和系统还原点联动状态。"
                    : result.Message;
                AppendLog("[完成] 首次安全备份已完成。");
                SetStage(UiStage.Completed);
                return;
            }

            if (result.RequiresManualGuide)
            {
                ManualReasonTextBlock.Text = string.IsNullOrWhiteSpace(result.Message)
                    ? "系统还原保护尚未就绪。请先在“系统保护”中启用还原并创建至少一个还原点，然后点击“完成后重新检测”。"
                    : NormalizeManualReason(result.Message);
                AppendLog("[阻塞] 需要先完成手动处理。");
                SetStage(UiStage.Manual);
                return;
            }

            FailedReasonTextBlock.Text = string.IsNullOrWhiteSpace(result.Message)
                ? "备份没有完成，请点击“重新备份”。"
                : NormalizeFailureReason(result.Message);
            await _preferenceStore.RecordFailureAsync(FailedReasonTextBlock.Text).ConfigureAwait(true);
            AppendLog("[失败] 备份未完成。");
            SetStage(UiStage.Failed);
        }
        catch (Exception ex)
        {
            FailedReasonTextBlock.Text = NormalizeFailureReason(ex.Message);
            await _preferenceStore.RecordFailureAsync(FailedReasonTextBlock.Text).ConfigureAwait(true);
            AppendLog($"[异常] {ex.Message}");
            _diagnosticLogService?.Append(LogLevel.Error, "FirstRunBaselineDialog.Run", ex.Message, ex.ToString());
            SetStage(UiStage.Failed);
        }
        finally
        {
            _isRunning = false;
        }
    }

    private static double? ResolveProgress(BaselineProgressEvent evt)
    {
        if (evt.TotalDomains <= 0)
        {
            return evt.Step == BaselineFlowStep.Completed ? 100 : null;
        }

        return Math.Clamp(evt.CompletedDomains * 100.0 / evt.TotalDomains, 0, 100);
    }

    private static string ResolveStepText(BaselineFlowStep step)
    {
        return step switch
        {
            BaselineFlowStep.Overview => "任务概览",
            BaselineFlowStep.Preflight => "环境预检",
            BaselineFlowStep.Collecting => "数据备份",
            BaselineFlowStep.ManualGuide => "人工处理",
            BaselineFlowStep.Verifying => "完整性校验",
            BaselineFlowStep.Completed => "执行完成",
            _ => "执行中"
        };
    }

    private static string ResolveDomainLabel(string? domainId)
    {
        if (string.IsNullOrWhiteSpace(domainId))
        {
            return "系统基线";
        }

        return DomainLabels.TryGetValue(domainId, out var label)
            ? $"{domainId} {label}"
            : domainId;
    }

    private static string BuildFriendlyStatus(BaselineProgressEvent evt, string rawMessage)
    {
        return evt.Step switch
        {
            BaselineFlowStep.Overview => "正在准备备份任务...",
            BaselineFlowStep.Preflight => "正在检查权限、磁盘和系统能力...",
            BaselineFlowStep.Collecting => evt.TotalDomains > 0
                ? $"当前正在备份：{ResolveDomainLabel(evt.DomainId)}（{evt.CompletedDomains}/{evt.TotalDomains}）"
                : $"当前正在备份：{ResolveDomainLabel(evt.DomainId)}",
            BaselineFlowStep.ManualGuide => "需要先完成手动处理，处理后可继续。",
            BaselineFlowStep.Verifying => "正在打包并校验备份完整性...",
            BaselineFlowStep.Completed => "备份已完成，正在更新恢复保障状态...",
            _ => string.IsNullOrWhiteSpace(rawMessage) ? "执行中..." : rawMessage
        };
    }

    private static string BuildFriendlyLog(BaselineProgressEvent evt, string rawMessage)
    {
        var stepText = ResolveStepText(evt.Step);
        var domainText = string.IsNullOrWhiteSpace(evt.DomainId) ? string.Empty : $" | {ResolveDomainLabel(evt.DomainId)}";
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return $"[{stepText}{domainText}] 正在执行";
        }

        return $"[{stepText}{domainText}] {rawMessage}";
    }

    private static bool ContainsAny(string source, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (!string.IsNullOrWhiteSpace(token) && source.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeManualReason(string rawReason)
    {
        var reason = rawReason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "系统还原点未就绪，请按提示完成手动处理后再继续。";
        }

        var lower = reason.ToLowerInvariant();
        if (ContainsAny(lower, "restore", "还原点", "system protection", "manual", "d13"))
        {
            return "系统还原点尚未准备好。请打开“系统保护”，启用还原并创建至少一个还原点，然后重新检测。";
        }

        return $"请先完成手动处理：{reason}";
    }

    private static string NormalizeFailureReason(string rawReason)
    {
        var reason = rawReason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "备份没有完成，请点击“重新备份”再试一次。";
        }

        var lower = reason.ToLowerInvariant();
        if (ContainsAny(lower, "access denied", "unauthorized", "permission", "权限", "管理员"))
        {
            return GameOptimizer.Core.Services.ElevationGate.DescribeWriteDenial("首次安全基线备份", reason);
        }

        if (ContainsAny(lower, "disk", "space", "磁盘", "空间"))
        {
            return "磁盘空间不足，无法完成备份。请先清理空间后重试。";
        }

        if (ContainsAny(lower, "restore", "还原点", "d13", "system protection"))
        {
            return "系统还原点检查未通过。请先完成系统保护设置后再试。";
        }

        if (ContainsAny(lower, "registry", "注册表"))
        {
            return "注册表备份失败。请检查权限或安全软件拦截后重试。";
        }

        if (ContainsAny(lower, "network", "winsock", "proxy", "dns", "网络", "代理"))
        {
            return "网络配置备份失败。请确认网络组件可访问后重试。";
        }

        return $"备份未完成：{reason}";
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _logItems.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        while (_logItems.Count > 200)
        {
            _logItems.RemoveAt(0);
        }

        if (_logItems.Count > 0)
        {
            LogListBox.ScrollIntoView(_logItems[^1]);
        }
    }

    private async Task RecheckManualGuideAsync()
    {
        if (_isRunning)
        {
            return;
        }

        try
        {
            ManualRecheckBtn.IsEnabled = false;
            ManualOpenSettingsBtn.IsEnabled = false;
            AppendLog("[人工处理] 正在重新检测系统保护和还原点状态...");

            var recheck = await _baselineOrchestrator.RecheckManualGuideAsync().ConfigureAwait(true);
            if (!recheck.Success)
            {
                ManualReasonTextBlock.Text = string.IsNullOrWhiteSpace(recheck.Message)
                    ? "重新检测未通过，请确认系统保护已开启并已创建还原点。"
                    : NormalizeManualReason(recheck.Message);
                AppendLog($"[人工处理] 重新检测未通过：{ManualReasonTextBlock.Text}");
                return;
            }

            AppendLog("[人工处理] 重新检测通过，继续执行备份。");
            await RunBaselineAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ManualReasonTextBlock.Text = $"重新检测异常：{ex.Message}";
            AppendLog($"[人工处理] 重新检测异常：{ex.Message}");
            _diagnosticLogService?.Append(LogLevel.Error, "FirstRunBaselineDialog.Recheck", ex.Message, ex.ToString());
        }
        finally
        {
            ManualRecheckBtn.IsEnabled = true;
            ManualOpenSettingsBtn.IsEnabled = true;
        }
    }

    private void SetStage(UiStage stage)
    {
        _stage = stage;
        WelcomeStepPanel.Visibility = Visibility.Collapsed;
        BoundaryStepPanel.Visibility = Visibility.Collapsed;
        AuthorStepPanel.Visibility = Visibility.Collapsed;
        RiskStepPanel.Visibility = Visibility.Collapsed;
        BackupStepPanel.Visibility = Visibility.Collapsed;
        RunningPanel.Visibility = stage == UiStage.Running ? Visibility.Visible : Visibility.Collapsed;
        ManualPanel.Visibility = stage == UiStage.Manual ? Visibility.Visible : Visibility.Collapsed;
        FailedPanel.Visibility = stage == UiStage.Failed ? Visibility.Visible : Visibility.Collapsed;
        CompletedPanel.Visibility = stage == UiStage.Completed ? Visibility.Visible : Visibility.Collapsed;

        IntroBackBtn.Visibility = Visibility.Collapsed;
        IntroExitBtn.Visibility = Visibility.Collapsed;
        IntroNextBtn.Visibility = Visibility.Collapsed;
        StartBackupBtn.Visibility = Visibility.Collapsed;
        ManualOpenSettingsBtn.Visibility = stage == UiStage.Manual ? Visibility.Visible : Visibility.Collapsed;
        ManualRecheckBtn.Visibility = stage == UiStage.Manual ? Visibility.Visible : Visibility.Collapsed;
        ManualExitBtn.Visibility = stage == UiStage.Manual ? Visibility.Visible : Visibility.Collapsed;
        FailedRetryBtn.Visibility = stage == UiStage.Failed ? Visibility.Visible : Visibility.Collapsed;
        FailedExitBtn.Visibility = stage == UiStage.Failed ? Visibility.Visible : Visibility.Collapsed;
        CompletedEnterBtn.Visibility = stage == UiStage.Completed ? Visibility.Visible : Visibility.Collapsed;
        CompletedOpenSettingsBtn.Visibility = stage == UiStage.Completed && _systemRestorePointPending
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (stage == UiStage.Intro)
        {
            ShowIntroStep();
            return;
        }

        UpdateProgressSegments(4);
        FooterHintText.Text = stage switch
        {
            UiStage.Running => "正在备份中，请不要关闭窗口或关机。",
            UiStage.Manual => "请按教程完成系统保护后重新检测，也可以暂不备份进入软件。",
            UiStage.Failed => "可以重新尝试，或明确跳过后继续使用；未完成不会被标记为已备份。",
            UiStage.Completed => "你已具备经校验的恢复依据，进入后可在备份管理中查看。",
            _ => string.Empty
        };
    }

    private void ShowIntroStep()
    {
        _introStep = Math.Clamp(_introStep, 1, 4);
        UpdateProgressSegments(_introStep);
        WelcomeStepPanel.Visibility = _introStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        BoundaryStepPanel.Visibility = _introStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        AuthorStepPanel.Visibility = Visibility.Collapsed;
        RiskStepPanel.Visibility = _introStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        BackupStepPanel.Visibility = _introStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        IntroBackBtn.Visibility = _introStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        IntroExitBtn.Visibility = Visibility.Visible;
        IntroExitBtn.Content = _introStep == 1 ? "稍后再说" : "暂时跳过";
        IntroNextBtn.Visibility = _introStep < 4 ? Visibility.Visible : Visibility.Collapsed;
        StartBackupBtn.Visibility = _introStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        IntroNextBtn.Content = _introStep == 3 ? "→　继续" : "→　开始了解";
        IntroNextBtn.IsEnabled = _introStep != 3 || _riskAcknowledged;
        StartBackupBtn.IsEnabled = _introStep == 4 && _riskAcknowledged;
        IntroStepIndicatorText.Text = $"第 {_introStep} 步 / 共 4 步";
        FooterHintText.Text = _introStep switch
        {
            1 => "第 1 步 / 共 4 步：欢迎",
            2 => "第 2 步 / 共 4 步：使用提醒",
            3 => "第 3 步 / 共 4 步：确认备份",
            _ => "第 4 步 / 共 4 步：创建备份"
        };
    }

    private async Task EnableStartBackupAfterAcknowledgementAsync(int epoch)
    {
        for (var remaining = 5; remaining > 0; remaining--)
        {
            if (epoch != _riskAcknowledgementEpoch || _introStep != 3 || !RiskAcknowledgementCheckBox.IsChecked.GetValueOrDefault())
            {
                return;
            }

            RiskCountdownText.Text = $"请先完整阅读，{remaining}s 后可继续";
            IntroNextBtn.Content = $"请阅读 {remaining}s";
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
        }

        if (epoch == _riskAcknowledgementEpoch && _introStep == 3 && RiskAcknowledgementCheckBox.IsChecked.GetValueOrDefault())
        {
            _riskAcknowledged = true;
            RiskCountdownText.Text = "已确认，可以继续创建首次安全备份";
            IntroNextBtn.IsEnabled = true;
            IntroNextBtn.Content = "→　继续";
        }
    }

    private async void StartBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_introStep != 4 || !_riskAcknowledged || !StartBackupBtn.IsEnabled)
        {
            return;
        }

        await RunBaselineAsync().ConfigureAwait(true);
    }

    private void IntroBackBtn_Click(object sender, RoutedEventArgs e)
    {
        _riskAcknowledgementEpoch++;
        _introStep = Math.Max(1, _introStep - 1);
        ShowIntroStep();
    }

    private void IntroNextBtn_Click(object sender, RoutedEventArgs e)
    {
        _riskAcknowledgementEpoch++;
        if (_introStep == 3 && !_riskAcknowledged)
        {
            return;
        }

        _introStep = Math.Min(4, _introStep + 1);
        ShowIntroStep();
    }

    private void RiskAcknowledgementCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _riskAcknowledgementEpoch++;
        if (_introStep != 3 || !RiskAcknowledgementCheckBox.IsChecked.GetValueOrDefault())
        {
            _riskAcknowledged = false;
            IntroNextBtn.IsEnabled = false;
            IntroNextBtn.Content = "请先阅读并确认";
            RiskCountdownText.Text = "请先完整阅读，5s 后可继续";
            return;
        }

        _riskAcknowledged = false;
        IntroNextBtn.IsEnabled = false;
        _ = EnableStartBackupAfterAcknowledgementAsync(_riskAcknowledgementEpoch);
    }

    private async void IntroExitBtn_Click(object sender, RoutedEventArgs e)
    {
        await DeferBaselineAndCloseAsync($"用户在首次说明第 {_introStep} 步选择暂时跳过。").ConfigureAwait(true);
    }

    private void ManualOpenSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _manualGuideService.OpenSystemProtectionSettings();
            AppendLog("[人工处理] 已打开系统保护设置。");
        }
        catch (Exception ex)
        {
            AppendLog($"[人工处理] 打开系统保护设置失败：{ex.Message}");
        }
    }

    private async void ManualRecheckBtn_Click(object sender, RoutedEventArgs e)
    {
        await RecheckManualGuideAsync().ConfigureAwait(true);
    }

    private async void ManualExitBtn_Click(object sender, RoutedEventArgs e)
    {
        await DeferBaselineAndCloseAsync(ManualReasonTextBlock.Text).ConfigureAwait(true);
    }

    private async void FailedRetryBtn_Click(object sender, RoutedEventArgs e)
    {
        await RunBaselineAsync().ConfigureAwait(true);
    }

    private async void FailedExitBtn_Click(object sender, RoutedEventArgs e)
    {
        await DeferBaselineAndCloseAsync(FailedReasonTextBlock.Text).ConfigureAwait(true);
    }

    private void CompletedEnterBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowAuthorInfoBeforeEnteringSoftware();
        CloseAfterAuthorInfo();
    }

    private void CompletedOpenSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        _manualGuideService.OpenSystemProtectionSettings();
    }

    private void UpdateProgressSegments(int completedStep)
    {
        var completedBrush = (Brush)FindResource("Onboarding.Action");
        var pendingBrush = (Brush)FindResource("Onboarding.ProgressPending");
        var segments = new[]
        {
            ProgressSegment1,
            ProgressSegment2,
            ProgressSegment3,
            ProgressSegment4
        };

        ProgressSegment5.Visibility = Visibility.Collapsed;

        for (var index = 0; index < segments.Length; index++)
        {
            segments[index].Background = index < completedStep ? completedBrush : pendingBrush;
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == UiStage.Completed)
        {
            ShowAuthorInfoBeforeEnteringSoftware();
            CloseAfterAuthorInfo();
            return;
        }

        await DeferBaselineAndCloseAsync("用户从首次安全备份引导关闭窗口。").ConfigureAwait(true);
    }

    private async Task DeferBaselineAndCloseAsync(string reason)
    {
        if (!ConfirmDeferredBaseline())
        {
            return;
        }

        try
        {
            await _preferenceStore.DeferAsync(reason).ConfigureAwait(true);
            ShowAuthorInfoBeforeEnteringSoftware();
            CloseAfterAuthorInfo();
        }
        catch (Exception ex)
        {
            AppendLog($"[首次备份] 记录跳过状态失败：{ex.Message}");
            MessageBox.Show(
                this,
                "未能安全记录跳过状态，因此仍停留在首次备份引导。请重新选择创建备份或稍后再试。",
                "无法跳过首次备份",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool ConfirmDeferredBaseline()
    {
        const string message = "尚未建立首次安全备份。以后仍可在“备份管理”创建新的备份，但它只能记录当时的状态，无法补回首次使用前的系统基线。\n\n"
            + "请另外确认 Windows 系统保护已启用，并手动创建一个系统还原点。后续若出现问题，请按以下顺序处理：\n"
            + "1. 优先使用系统还原点；\n"
            + "2. 显卡、芯片组或设备异常时，从设备厂商渠道重新安装驱动；\n"
            + "3. 电源配置异常时，切回优化前的原电源计划；\n"
            + "4. 若没有可用备份或还原依据，且系统仍无法恢复，可能只能重装系统。\n\n"
            + "确定暂时跳过首次安全备份并继续使用软件吗？";

        var dialog = new ConfirmDialog(
            "确认暂时跳过首次安全备份",
            message,
            icon: "\uE7BA",
            confirmText: "仍然跳过")
        {
            Owner = this
        };

        dialog.ShowDialog();
        return dialog.IsConfirmed;
    }

    private void ShowAuthorInfoBeforeEnteringSoftware()
    {
        if (_authorInfoShown)
        {
            return;
        }

        _authorInfoShown = true;
        var dialog = new AboutDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void CloseAfterAuthorInfo()
    {
        _allowClose = true;
        Close();
    }

    private void OpenAuthorSupportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutDialog { Owner = this };
        dialog.ShowDialog();
    }

    private void WechatDonationImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        ShowDonationPreview("/Assets/donate_wechat.jpg", "微信赞赏");

    private void AlipayDonationImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        ShowDonationPreview("/Assets/donate_alipay.jpg", "支付宝赞赏");

    private void ShowDonationPreview(string resourcePath, string title)
    {
        var preview = new Window
        {
            Title = title,
            Owner = this,
            Width = 780,
            Height = 760,
            MinWidth = 560,
            MinHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
            Foreground = Brushes.MidnightBlue
        };

        var image = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(28),
            SnapsToDevicePixels = true
        };
        try
        {
            image.Source = new BitmapImage(new Uri($"pack://application:,,,{resourcePath}", UriKind.Absolute));
        }
        catch (Exception ex)
        {
            _diagnosticLogService?.Append(LogLevel.Error, "FirstRunBaselineDialog.DonationPreview", "赞赏码资源无法加载。", ex.Message);
            MessageBox.Show(this, "赞赏码图片未能加载，请检查安装文件是否完整。", title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var closeButton = new Button
        {
            Content = "关闭",
            Padding = new Thickness(18, 7, 18, 7),
            Margin = new Thickness(0, 0, 24, 18),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => preview.Close();

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(image, 0);
        Grid.SetRow(closeButton, 1);
        layout.Children.Add(image);
        layout.Children.Add(closeButton);
        preview.Content = layout;
        preview.ShowDialog();
    }

    private void OpenBackupFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(AppPaths.BackupsDir, "Baseline", "packages");
        if (!Directory.Exists(directory))
        {
            MessageBox.Show(this, "备份目录会在首次备份通过预检后创建。", "备份位置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法打开备份目录：{ex.Message}", "备份位置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
