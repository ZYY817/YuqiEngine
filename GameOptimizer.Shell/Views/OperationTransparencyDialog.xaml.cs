using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views;

public partial class OperationTransparencyDialog : Window
{
    private readonly OptimizationToggleModel _toggle;
    private readonly string _moduleTitle;
    private readonly string _entryPath;
    private readonly string _registryPath;
    private readonly string _applyCommand;
    private readonly string _verifyCommand;
    private readonly string _rollbackCommand;

    public OperationTransparencyDialog(OptimizationToggleModel toggle, string? moduleTitle)
    {
        _toggle = toggle ?? throw new ArgumentNullException(nameof(toggle));
        _moduleTitle = string.IsNullOrWhiteSpace(moduleTitle) ? "优化项" : moduleTitle.Trim();

        InitializeComponent();

        _entryPath = ResolveEntryPath(toggle);
        _registryPath = ResolveRegistryPath(toggle);
        _applyCommand = BuildApplyCommand(toggle, _registryPath);
        _verifyCommand = BuildVerifyCommand(toggle, _registryPath);
        _rollbackCommand = BuildRollbackCommand(toggle, _registryPath);

        BindStaticFields(toggle);
        Loaded += OnDialogLoaded;
    }

    public static void Show(Window? owner, OptimizationToggleModel toggle, string? moduleTitle)
    {
        var dialog = new OperationTransparencyDialog(toggle, moduleTitle)
        {
            Owner = owner
        };

        dialog.ShowDialog();
    }

    private async void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        BindLatestEvidence();
        await RefreshCurrentValueAsync();
    }

    private void BindStaticFields(OptimizationToggleModel toggle)
    {
        WindowTitleText.Text = $"操作透明卡 - {_moduleTitle}";
        OperationTitleText.Text = Pick(toggle.Title, "优化项");
        OperationDescText.Text = Pick(toggle.Description, "暂无描述。");

        TypeBadgeText.Text = ResolveTypeLabel(toggle.OperationType);
        RiskBadgeText.Text = ResolveRiskLabel(toggle.RiskLevel);

        FunctionPurposeText.Text = OperationDescText.Text;
        ModifyObjectText.Text = ResolveModificationObject(toggle.OperationType);
        ModifyLocationText.Text = BuildLocationDescription(toggle, _registryPath, _entryPath);

        var canRollback = CanUseOneClickRollback(toggle);
        WindowsDefaultText.Text = BuildWindowsDefaultText(toggle);
        RecommendedValueText.Text = Pick(toggle.OptimizedValue, toggle.TargetValue, "未提供建议值");
        BeforeValueText.Text = BuildBeforeValueText(toggle, canRollback);
        CurrentValueText.Text = Pick(toggle.RuntimeStatusDetails, "点击“读取当前值”进行查询");

        ApplyActionText.Text = BuildExecutionAction(toggle, _applyCommand);
        ApplyCommandText.Text = _applyCommand;
        VerifyCommandText.Text = _verifyCommand;
        RollbackCommandText.Text = BuildRollbackCommandText(toggle, canRollback, _rollbackCommand);

        EffectiveConditionText.Text = BuildEffectiveCondition(toggle);
        BenefitText.Text = Pick(toggle.Benefit, "未提供");
        SideEffectText.Text = BuildSideEffect(toggle);
        RiskReasonText.Text = BuildRiskReason(toggle);

        OneClickRollbackText.Text = BuildOneClickRollback(_moduleTitle, canRollback);
        ManualRollbackText.Text = BuildManualRollback(toggle, canRollback, _rollbackCommand);
        RollbackTroubleshootText.Text = BuildRollbackTroubleshoot(toggle);
        OneClickRollbackButton.IsEnabled = canRollback;
        CopyRollbackButton.IsEnabled = canRollback && IsCopyableRollbackCommand(RollbackCommandText.Text);

        RegistryPathText.Text = Pick(_registryPath, "无");
        ManualEntryText.Text = Pick(_entryPath, "无");
        OpenRegistryButton.Visibility = string.IsNullOrWhiteSpace(_registryPath) ? Visibility.Collapsed : Visibility.Visible;

        FooterHintText.Text = canRollback
            ? "本项具有可验证的恢复回执；回滚仍会重新校验当前状态。"
            : "未记录本软件写入前的原值。为避免覆盖用户或其他工具的设置，不提供猜测式回滚。";
        AdvancedItemsControl.ItemsSource = BuildAdvancedFields(toggle, _moduleTitle);
    }

    private void BindLatestEvidence()
    {
        if (string.IsNullOrWhiteSpace(_toggle.OperationId))
        {
            SetEmptyEvidence("无执行证据：operationId 为空。");
            return;
        }

        var dashboard = Application.Current?.MainWindow?.DataContext as DashboardViewModel;
        if (dashboard is null)
        {
            SetEmptyEvidence("无执行证据：无法获取主界面上下文。");
            return;
        }

        if (!dashboard.TryGetLatestOperationEvidence(_toggle.OperationId, out var evidence) || evidence is null)
        {
            SetEmptyEvidence("当前会话未找到执行证据。");
            return;
        }

        var localizedStatus = LocalizeEvidenceStatus(evidence.Status);
        EvidenceStatusText.Text = evidence.RequiresReboot
            ? $"{localizedStatus}（需要重启）"
            : localizedStatus;
        EvidenceMessageText.Text = Pick(evidence.Message, evidence.AfterValue, "无附加信息");
        EvidenceErrorText.Text = string.IsNullOrWhiteSpace(evidence.ErrorMessage) ? "无" : evidence.ErrorMessage;
        EvidenceSourceText.Text = Pick(
            evidence.SourceHint?
                .Replace("Module:", "模块：", StringComparison.OrdinalIgnoreCase)
                .Replace("Batch:", "批次：", StringComparison.OrdinalIgnoreCase),
            "来源：当前会话缓存");

        if (!string.IsNullOrWhiteSpace(evidence.BeforeValue))
        {
            BeforeValueText.Text = evidence.BeforeValue.Trim();
        }
    }

    private void SetEmptyEvidence(string hint)
    {
        EvidenceStatusText.Text = "暂无最近执行证据";
        EvidenceMessageText.Text = hint;
        EvidenceErrorText.Text = "无";
        EvidenceSourceText.Text = "来源：当前会话缓存";
    }

    private async Task RefreshCurrentValueAsync()
    {
        CurrentValueText.Text = "正在读取当前值...";

        if (!CanExecuteReadOnlyVerify(_verifyCommand))
        {
            CurrentValueText.Text = Pick(
                _toggle.RuntimeStatusDetails,
                "当前无法自动校验，请使用快捷入口手工核验。");
            return;
        }

        var probe = await RunReadOnlyCommandAsync(_verifyCommand, timeoutMs: 8000);
        if (!probe.Success)
        {
            CurrentValueText.Text = Pick(
                _toggle.RuntimeStatusDetails,
                "实时读取失败，请手工执行校验命令。");

            if (string.IsNullOrWhiteSpace(EvidenceErrorText.Text) || EvidenceErrorText.Text == "无")
            {
                EvidenceErrorText.Text = Pick(probe.StdErr, probe.Error, "实时查询失败");
            }
            return;
        }

        var parsed = ExtractCurrentValue(
            _toggle.OperationType,
            probe.StdOut,
            probe.StdErr,
            _toggle.RegistryValueName);
        CurrentValueText.Text = Pick(parsed, _toggle.RuntimeStatusDetails, "查询成功，但未解析出明确值。");
    }

    private static bool CanExecuteReadOnlyVerify(string verifyCommand)
    {
        if (string.IsNullOrWhiteSpace(verifyCommand))
        {
            return false;
        }

        var normalized = verifyCommand.Trim();
        if (normalized.StartsWith("刷新状态", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("参考手工步骤", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = normalized
            .Split(new[] { "&&" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.All(IsReadOnlyCommandSegment);
    }

    private static bool IsReadOnlyCommandSegment(string segment)
    {
        var cmd = segment.Trim().ToLowerInvariant();
        return cmd.StartsWith("reg query ")
               || cmd.StartsWith("sc qc ")
               || cmd.StartsWith("sc query ")
               || cmd.StartsWith("schtasks /query ")
               || cmd.StartsWith("powercfg /getactivescheme")
               || cmd.StartsWith("bcdedit")
               || cmd.StartsWith("wmic ");
    }

    private static async Task<ProbeResult> RunReadOnlyCommandAsync(string command, int timeoutMs)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/d /c {command}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var stdOutTask = process.StandardOutput.ReadToEndAsync();
                var stdErrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // best effort
                    }

                    return new ProbeResult(false, string.Empty, string.Empty, "命令超时");
                }

                var stdout = stdOutTask.GetAwaiter().GetResult();
                var stderr = stdErrTask.GetAwaiter().GetResult();
                return new ProbeResult(process.ExitCode == 0, stdout, stderr, process.ExitCode.ToString());
            }
            catch (Exception ex)
            {
                return new ProbeResult(false, string.Empty, string.Empty, ex.Message);
            }
        });
    }

    private static string ExtractCurrentValue(
        OperationType operationType,
        string stdOut,
        string stdErr,
        string? registryValueName)
    {
        var output = string.IsNullOrWhiteSpace(stdOut) ? stdErr : stdOut;
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        var lines = output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        switch (operationType)
        {
            case OperationType.Registry:
                if (!string.IsNullOrWhiteSpace(registryValueName))
                {
                    var valueLine = lines.FirstOrDefault(line =>
                        line.IndexOf(registryValueName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!string.IsNullOrWhiteSpace(valueLine))
                    {
                        return valueLine;
                    }
                }
                return SummarizeLines(lines, 4);

            case OperationType.Service:
                return SummarizeLines(lines.Where(line =>
                    line.IndexOf("SERVICE_NAME", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("START_TYPE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("STATE", StringComparison.OrdinalIgnoreCase) >= 0), 6);

            case OperationType.ScheduledTask:
                return SummarizeLines(lines.Where(line =>
                    line.IndexOf("TaskName", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0), 6);

            case OperationType.PowerPlan:
                return SummarizeLines(lines.Where(line =>
                    line.IndexOf("Power Scheme GUID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("GUID", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("Active", StringComparison.OrdinalIgnoreCase) >= 0), 4);

            default:
                return SummarizeLines(lines, 6);
        }
    }

    private static string SummarizeLines(IEnumerable<string> source, int maxLines)
    {
        var selected = source.Take(maxLines).ToArray();
        if (selected.Length == 0)
        {
            return string.Empty;
        }

        var joined = string.Join(Environment.NewLine, selected);
        return joined.Length <= 1000 ? joined : joined[..1000] + "...";
    }

    private static IReadOnlyList<AdvancedField> BuildAdvancedFields(OptimizationToggleModel toggle, string moduleTitle)
    {
        return
        [
            new AdvancedField("模块", moduleTitle),
            new AdvancedField("操作ID", Pick(toggle.OperationId, "无")),
            new AdvancedField("类型", ResolveTypeLabel(toggle.OperationType)),
            new AdvancedField("风险", ResolveRiskLabel(toggle.RiskLevel)),
            new AdvancedField("需要管理员", toggle.RequiresAdmin ? "是" : "否"),
            new AdvancedField("需要重启", toggle.RequiresReboot ? "是" : "否"),
            new AdvancedField("需要二次确认", toggle.RequiresExplicitConfirmation ? "是" : "否"),
            new AdvancedField("建议还原点", toggle.RequiresRestorePoint ? "是" : "否"),
            new AdvancedField("注册表路径", Pick(toggle.RegistryPath, "无")),
            new AdvancedField("值名", Pick(toggle.RegistryValueName, "无")),
            new AdvancedField("值类型", Pick(toggle.ValueType, "无")),
            new AdvancedField("目标名称", Pick(toggle.TargetName, "无")),
            new AdvancedField("目标值", Pick(toggle.TargetValue, "无"))
        ];
    }

    private static string ResolveModificationObject(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.Registry => "注册表",
            OperationType.Service => "服务",
            OperationType.ScheduledTask => "计划任务",
            OperationType.PowerPlan => "电源",
            OperationType.Command => "命令",
            OperationType.SystemApi => "系统 API / 策略",
            _ => "其他系统对象"
        };
    }

    private static string ResolveEntryPath(OptimizationToggleModel toggle)
    {
        if (!string.IsNullOrWhiteSpace(toggle.ManualPath))
        {
            return toggle.ManualPath.Trim();
        }

        return toggle.OperationType switch
        {
            OperationType.Registry => "regedit.exe",
            OperationType.Service => "services.msc",
            OperationType.ScheduledTask => "taskschd.msc",
            OperationType.PowerPlan => "control.exe /name Microsoft.PowerOptions",
            _ => "cmd.exe"
        };
    }

    private static string ResolveRegistryPath(OptimizationToggleModel toggle)
    {
        if (string.IsNullOrWhiteSpace(toggle.RegistryPath))
        {
            return string.Empty;
        }

        string hive = string.IsNullOrWhiteSpace(toggle.RegistryHiveText) ? "HKLM" : toggle.RegistryHiveText.Trim();
        return $"{hive}\\{toggle.RegistryPath.Trim()}";
    }

    private static string BuildLocationDescription(OptimizationToggleModel toggle, string registryPath, string entryPath)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(registryPath))
        {
            lines.Add($"路径: {registryPath}");
        }

        if (!string.IsNullOrWhiteSpace(toggle.RegistryValueName))
        {
            lines.Add($"值名: {toggle.RegistryValueName}");
        }

        if (!string.IsNullOrWhiteSpace(toggle.TargetName))
        {
            lines.Add($"目标: {toggle.TargetName}");
        }

        lines.Add($"入口: {entryPath}");
        return string.Join(Environment.NewLine, lines.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildApplyCommand(OptimizationToggleModel toggle, string registryPath)
    {
        if (!string.IsNullOrWhiteSpace(registryPath) && !string.IsNullOrWhiteSpace(toggle.RegistryValueName))
        {
            if (IsCompositeRegistryValue(toggle.RegistryValueName, toggle.OptimizedValue, toggle.TargetValue))
            {
                return "受管多字段写入：该操作包含多个值或多个目标，透明卡不伪造单条 reg 命令；请使用软件的确认流程执行，并通过下方校验入口查看结果。";
            }

            string valueType = NormalizeRegistryType(toggle.ValueType);
            string value = Pick(toggle.OptimizedValue, toggle.TargetValue, "1");
            return $"reg add \"{registryPath}\" /v \"{toggle.RegistryValueName}\" /t {valueType} /d \"{value}\" /f";
        }

        if (toggle.OperationType == OperationType.Service && !string.IsNullOrWhiteSpace(toggle.TargetName))
        {
            var target = (toggle.TargetValue ?? string.Empty).Trim();
            var mode = target.Contains("disable", StringComparison.OrdinalIgnoreCase) || target.Contains("禁用", StringComparison.OrdinalIgnoreCase)
                ? "disabled"
                : (target.Contains("manual", StringComparison.OrdinalIgnoreCase) || target.Contains("手动", StringComparison.OrdinalIgnoreCase)
                    ? "demand"
                    : "auto");
            return $"sc config \"{toggle.TargetName}\" start= {mode}";
        }

        if (toggle.OperationType == OperationType.ScheduledTask && !string.IsNullOrWhiteSpace(toggle.TargetName))
        {
            var enable = !(toggle.TargetValue ?? string.Empty).Contains("禁用", StringComparison.OrdinalIgnoreCase) &&
                         !(toggle.TargetValue ?? string.Empty).Contains("disable", StringComparison.OrdinalIgnoreCase);
            return $"schtasks /change /tn \"{toggle.TargetName}\" {(enable ? "/enable" : "/disable")}";
        }

        return Pick(toggle.ManualSteps, "请参考手工入口和模块说明。");
    }

    private static string BuildVerifyCommand(OptimizationToggleModel toggle, string registryPath)
    {
        if (!string.IsNullOrWhiteSpace(registryPath) && !string.IsNullOrWhiteSpace(toggle.RegistryValueName))
        {
            return $"reg query \"{registryPath}\" /v \"{toggle.RegistryValueName}\"";
        }

        if (toggle.OperationType == OperationType.Service && !string.IsNullOrWhiteSpace(toggle.TargetName))
        {
            return $"sc qc \"{toggle.TargetName}\" && sc query \"{toggle.TargetName}\"";
        }

        if (toggle.OperationType == OperationType.ScheduledTask && !string.IsNullOrWhiteSpace(toggle.TargetName))
        {
            return $"schtasks /query /tn \"{toggle.TargetName}\" /fo list /v";
        }

        if (toggle.OperationType == OperationType.PowerPlan)
        {
            return "powercfg /getactivescheme";
        }

        return "请刷新状态或使用系统工具校验。";
    }

    private static string BuildRollbackCommand(OptimizationToggleModel toggle, string registryPath)
    {
        if (!string.IsNullOrWhiteSpace(registryPath) && !string.IsNullOrWhiteSpace(toggle.RegistryValueName))
        {
            if (toggle.RequiresSnapshotForRestore || string.IsNullOrWhiteSpace(toggle.DefaultValue))
            {
                return "未提供可直接执行的回滚命令。此项必须使用本软件保存并校验过的恢复回执，不能依据缺失的原始值猜测删除或覆盖注册表项。";
            }

            string valueType = NormalizeRegistryType(toggle.ValueType);
            return $"reg add \"{registryPath}\" /v \"{toggle.RegistryValueName}\" /t {valueType} /d \"{toggle.DefaultValue}\" /f";
        }

        if (toggle.OperationType == OperationType.Service && !string.IsNullOrWhiteSpace(toggle.TargetName))
        {
            return $"sc config \"{toggle.TargetName}\" start= demand";
        }

        if (toggle.OperationType == OperationType.ScheduledTask && !string.IsNullOrWhiteSpace(toggle.TargetName))
        {
            return $"schtasks /change /tn \"{toggle.TargetName}\" /enable";
        }

        return "关闭该功能并恢复模块默认配置。";
    }

    private static string BuildExecutionAction(OptimizationToggleModel toggle, string applyCommand)
    {
        var target = Pick(toggle.TargetName, toggle.RegistryValueName, "系统对象");
        return toggle.OperationType switch
        {
            OperationType.Registry => $"写入注册表项 \"{target}\"。",
            OperationType.Service => $"更新服务启动方式 \"{target}\"。",
            OperationType.ScheduledTask => $"启用或禁用计划任务 \"{target}\"。",
            OperationType.PowerPlan => $"写入电源参数并激活目标方案。",
            OperationType.Command => $"执行系统命令，目标 \"{target}\"。",
            _ => $"执行优化动作，目标 \"{target}\"。"
        } + Environment.NewLine + $"执行方式说明: {applyCommand}";
    }

    private static string BuildEffectiveCondition(OptimizationToggleModel toggle)
    {
        var conditions = new List<string>();
        conditions.Add(toggle.RequiresReboot ? "应用或回滚后需要重启。" : "默认无需重启。");
        if (toggle.RequiresAdmin)
        {
            conditions.Add("需要管理员权限。");
        }
        if (toggle.RequiresExplicitConfirmation)
        {
            conditions.Add("高风险操作需要二次确认。");
        }
        if (toggle.RequiresRestorePoint)
        {
            conditions.Add("执行前建议创建还原点。");
        }
        return string.Join(Environment.NewLine, conditions);
    }

    private static string BuildSideEffect(OptimizationToggleModel toggle)
    {
        var drawback = Pick(toggle.Drawback, "未提供");
        var warning = Pick(toggle.ImpactWarning, "未提供");
        return $"副作用: {drawback}{Environment.NewLine}风险提示: {warning}";
    }

    private static string BuildRiskReason(OptimizationToggleModel toggle)
    {
        var risk = ResolveRiskLabel(toggle.RiskLevel);
        var reason = Pick(toggle.ImpactWarning, toggle.Drawback, "模块未提供风险理由。");
        return $"{risk}。理由：{reason}";
    }

    private static string BuildOneClickRollback(string moduleTitle, bool canRollback)
    {
        return canRollback
            ? $"一键回滚入口：点击“立即回滚此项”即可只回滚当前条目。模块：{moduleTitle}。"
            : "当前值已符合目标，但本软件未持有可验证的应用前恢复回执。为避免覆盖用户或其他工具的配置，一键回滚已禁用；你仍可通过快捷入口查看和手工编辑该位置。";
    }

    private static string BuildManualRollback(OptimizationToggleModel toggle, bool canRollback, string rollbackCommand)
    {
        if (!canRollback)
        {
            return "未提供手工回滚命令：当前系统可能由用户、OEM 或其他工具设置为目标值，且未记录本软件写入前的原值。请先在“备份管理”建立可验证备份；如确需调整，请通过快捷入口检查并按你的系统配置手工编辑。";
        }

        var manual = Pick(toggle.ManualSteps, "未提供手工步骤。");
        return $"回滚命令：{rollbackCommand}{Environment.NewLine}{Environment.NewLine}手工步骤：{manual}";
    }

    private static string BuildRollbackTroubleshoot(OptimizationToggleModel toggle)
    {
        var lines = new List<string>
        {
            "1) 先执行校验命令并记录输出。",
            "2) 使用管理员权限执行回滚命令。",
            "3) 再次执行校验命令确认回滚结果。",
            "4) 仍失败时，通过快捷入口手工检查目标位置。"
        };
        if (toggle.RequiresReboot)
        {
            lines.Add("5) 回滚后重启系统，确保状态生效。");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeRegistryType(string? valueType)
    {
        if (string.IsNullOrWhiteSpace(valueType))
        {
            return "REG_DWORD";
        }

        string normalized = valueType.Trim().ToUpperInvariant();
        return normalized switch
        {
            "DWORD" => "REG_DWORD",
            "QWORD" => "REG_QWORD",
            "SZ" => "REG_SZ",
            "EXPAND_SZ" => "REG_EXPAND_SZ",
            "MULTI_SZ" => "REG_MULTI_SZ",
            "BINARY" => "REG_BINARY",
            _ when normalized.StartsWith("REG_") => normalized,
            _ => "REG_DWORD"
        };
    }

    private static string ResolveTypeLabel(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.Registry => "注册表",
            OperationType.Service => "服务",
            OperationType.ScheduledTask => "任务",
            OperationType.PowerPlan => "电源",
            OperationType.Command => "命令",
            OperationType.SystemApi => "系统 API",
            _ => "其他"
        };
    }

    private static string ResolveRiskLabel(OperationRiskLevel riskLevel)
    {
        return riskLevel switch
        {
            OperationRiskLevel.Extreme => "高风险",
            OperationRiskLevel.Balanced => "中风险",
            _ => "低风险"
        };
    }

    private static string LocalizeEvidenceStatus(string status)
    {
        return status switch
        {
            "Success" => "成功",
            "Failed" => "失败",
            "Skipped" => "已跳过",
            "NoChange" => "无变化",
            _ => string.IsNullOrWhiteSpace(status) ? "未知" : status
        };
    }

    private static string Pick(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenEntryButton_Click(object sender, RoutedEventArgs e)
    {
        TryOpen(_entryPath);
    }

    private void OpenRegistryButton_Click(object sender, RoutedEventArgs e)
    {
        TryOpen("regedit.exe");
    }

    private async void RefreshCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshCurrentValueAsync();
    }

    private async void OneClickRollbackButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_toggle.OperationId) || !_toggle.IsApplied || !_toggle.CanRestore)
        {
            MessageBox.Show(this, "当前条目没有可验证的恢复回执。为避免猜测原始值并覆盖未知配置，软件不会执行回滚。", "回滚不可用", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"确认立即回滚该条目？\n\n{_toggle.Title}\n\n该操作会立即写入系统配置。",
            "确认回滚",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (Application.Current?.MainWindow?.DataContext is not DashboardViewModel dashboard)
        {
            MessageBox.Show(this, "回滚失败：主界面上下文不可用。", "回滚", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        OneClickRollbackButton.IsEnabled = false;
        var originalCaption = OneClickRollbackButton.Content;
        OneClickRollbackButton.Content = "回滚中...";
        try
        {
            var result = await dashboard.TryRollbackSingleOperationAsync(_toggle.OperationId);
            if (!result.Success)
            {
                MessageBox.Show(this, $"回滚失败：\n{result.Message}", "回滚", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BindLatestEvidence();
            await RefreshCurrentValueAsync();
            MessageBox.Show(this, "当前条目回滚完成。", "回滚", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            OneClickRollbackButton.Content = originalCaption;
            OneClickRollbackButton.IsEnabled = !string.IsNullOrWhiteSpace(_toggle.OperationId);
        }
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(Join(_registryPath, _entryPath));
    }

    private void CopyApplyButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(_applyCommand);
    }

    private void CopyVerifyButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(_verifyCommand);
    }

    private void CopyRollbackButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(_rollbackCommand);
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        string all = Join(
            WindowTitleText.Text,
            OperationTitleText.Text,
            OperationDescText.Text,
            TypeBadgeText.Text,
            RiskBadgeText.Text,
            FunctionPurposeText.Text,
            ModifyObjectText.Text,
            ModifyLocationText.Text,
            WindowsDefaultText.Text,
            RecommendedValueText.Text,
            BeforeValueText.Text,
            CurrentValueText.Text,
            ApplyActionText.Text,
            ApplyCommandText.Text,
            VerifyCommandText.Text,
            RollbackCommandText.Text,
            EffectiveConditionText.Text,
            BenefitText.Text,
            SideEffectText.Text,
            RiskReasonText.Text,
            OneClickRollbackText.Text,
            ManualRollbackText.Text,
            RollbackTroubleshootText.Text,
            EvidenceStatusText.Text,
            EvidenceMessageText.Text,
            EvidenceErrorText.Text,
            EvidenceSourceText.Text);
        var advancedFields = AdvancedItemsControl.Items
            .OfType<AdvancedField>()
            .Select(field => $"{field.Label}: {field.Value}");
        all = Join(all, string.Join(Environment.NewLine, advancedFields));
        CopyText(all);
    }

    private static bool CanUseOneClickRollback(OptimizationToggleModel toggle) =>
        toggle.IsApplied && toggle.CanRestore && !string.IsNullOrWhiteSpace(toggle.OperationId);

    private static string BuildWindowsDefaultText(OptimizationToggleModel toggle)
    {
        if (!string.IsNullOrWhiteSpace(toggle.DefaultValue))
        {
            return toggle.DefaultValue.Trim();
        }

        return toggle.RequiresSnapshotForRestore
            ? "未提供统一默认值：Windows、OEM、用户和其他工具的原始配置可能不同，恢复仅可使用本软件保存的应用前回执。"
            : "模块未提供该字段的通用默认值。";
    }

    private static string BuildBeforeValueText(OptimizationToggleModel toggle, bool canRollback)
    {
        if (!string.IsNullOrWhiteSpace(toggle.ValuePreview))
        {
            return toggle.ValuePreview.Trim();
        }

        return toggle.RequiresSnapshotForRestore && !canRollback
            ? "未记录本软件写入前的值；当前目标状态可能由用户或其他工具设置。"
            : "未记录改前值。";
    }

    private static string BuildRollbackCommandText(
        OptimizationToggleModel toggle,
        bool canRollback,
        string rollbackCommand)
    {
        if (toggle.RequiresSnapshotForRestore)
        {
            return canRollback
                ? "受管回滚：由本软件保存的应用前恢复回执执行，不导出可能不完整的手工命令。"
                : "回滚不可用：未找到可验证的应用前恢复回执；不会猜测原始值。";
        }

        return rollbackCommand;
    }

    private static bool IsCopyableRollbackCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command)
        && !command.StartsWith("受管回滚", StringComparison.Ordinal)
        && !command.StartsWith("回滚不可用", StringComparison.Ordinal)
        && !command.StartsWith("未提供", StringComparison.Ordinal);

    private static bool IsCompositeRegistryValue(
        string registryValueName,
        string? optimizedValue,
        string? targetValue) =>
        registryValueName.Contains(';', StringComparison.Ordinal)
        || (optimizedValue?.Contains(';', StringComparison.Ordinal) ?? false)
        || (targetValue?.Contains(';', StringComparison.Ordinal) ?? false);

    private static string Join(params string?[] values)
    {
        var sb = new StringBuilder();
        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append(value.Trim());
        }

        return sb.ToString();
    }

    private static void CopyText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            Clipboard.SetText(value.Trim());
        }
        catch
        {
            // Ignore clipboard failures to keep dialog non-blocking.
        }
    }

    private static void TryOpen(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return;
        }

        try
        {
            var (fileName, arguments) = SplitCommandLine(commandLine.Trim());
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell open failures to keep dialog non-blocking.
        }
    }

    private static (string FileName, string Arguments) SplitCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return (string.Empty, string.Empty);
        }

        if (commandLine[0] == '"')
        {
            var endQuote = commandLine.IndexOf('"', 1);
            if (endQuote > 1)
            {
                var file = commandLine.Substring(1, endQuote - 1);
                var args = commandLine[(endQuote + 1)..].Trim();
                return (file, args);
            }
        }

        var firstSpace = commandLine.IndexOf(' ');
        if (firstSpace < 0)
        {
            return (commandLine, string.Empty);
        }

        return (commandLine[..firstSpace], commandLine[(firstSpace + 1)..].Trim());
    }

    private sealed record AdvancedField(string Label, string Value);
    private sealed record ProbeResult(bool Success, string StdOut, string StdErr, string Error);
}


