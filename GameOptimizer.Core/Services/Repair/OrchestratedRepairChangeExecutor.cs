using System.Security.Principal;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.NetworkReset;

namespace GameOptimizer.Core.Services.Repair;

public sealed class OrchestratedRepairChangeExecutor : IRepairChangeExecutor
{
    private static readonly IReadOnlyDictionary<string, RepairChangeBinding> Bindings = CreateBindings();
    private static readonly IReadOnlySet<string> BidirectionalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "repair.game_mode",
        "repair.hardware_acceleration",
        "repair.feedback",
        "repair.dpi_reset",
        "repair.vsync",
        "repair.advertising_id",
        "repair.game_dvr",
        "repair.privacy_settings",
        "repair.hags",
        "repair.diagnostic_data",
        "repair.fast_startup",
        "repair.appinfo",
        "repair.themes",
        "repair.audio_service"
    };

    public static bool IsBidirectional(string repairItemId) =>
        BidirectionalIds.Contains(repairItemId);

    /// <summary>
    /// Returns the same user-facing mutation summaries used by the execution
    /// binding. The UI uses this before confirmation so a repair never looks
    /// like an unexplained "one-click" action.
    /// </summary>
    public static IReadOnlyList<string> GetPlannedChangeSummaries(string repairItemId)
    {
        if (Bindings.TryGetValue(repairItemId, out var binding))
        {
            return binding.Steps
                .Select(step => step.ConfirmationSummary)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        if (NetworkRepairCatalog.TryGet(repairItemId, out var network)
            && network.Profile is { } profile)
        {
            return
            [
                profile switch
                {
                    NetworkResetProfile.TcpIp => "重置 TCP/IP 协议配置；现有网络连接可能短暂中断。",
                    NetworkResetProfile.Winsock => "重置 Windows 套接字目录；通常需要重启后重新建立连接。",
                    NetworkResetProfile.DnsCache => "清除本机 DNS 缓存；不会修改路由器或 DNS 服务商设置。",
                    NetworkResetProfile.Full => "恢复本机网络设置；会移除已保存的网络配置，并需要重启。",
                    _ => "执行受控网络修复，并保留执行结果供后续核验。"
                }
            ];
        }

        return ["该修复没有可验证的变更计划，因此不会执行。"];
    }

    private static IReadOnlyDictionary<string, RepairChangeBinding> CreateBindings()
    {
        var bindings = new Dictionary<string, RepairChangeBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["repair.game_mode"] = new(
                "repair.game_mode",
                [
                    new(RepairChangeCapabilityIds.ExistingGameModeEnable, "恢复游戏模式", "启用 Windows 自动游戏模式资源调度。"),
                    new(RepairChangeCapabilityIds.GameModeAutoEnable, "恢复游戏模式", "恢复 Windows 游戏模式自动优化开关。")
                ]),
            ["repair.hardware_acceleration"] = new(
                "repair.hardware_acceleration",
                [new(RepairChangeCapabilityIds.HardwareAccelerationReset, "恢复硬件加速", "恢复 WPF/桌面图形硬件加速设置。")]),
            ["repair.feedback"] = new(
                "repair.feedback",
                [new(RepairChangeCapabilityIds.FeedbackReset, "恢复反馈频率", "恢复 Windows 用户反馈频率设置。")]),
            ["repair.dpi_reset"] = new(
                "repair.dpi_reset",
                [new(RepairChangeCapabilityIds.DpiReset, "重置 DPI 缩放", "删除当前用户的自定义 DPI 覆盖，恢复系统默认缩放。")]),
            ["repair.vsync"] = new(
                "repair.vsync",
                [new(RepairChangeCapabilityIds.VsyncReset, "重置垂直同步", "删除当前用户的 ForceVSync 覆盖，恢复应用默认行为。")]),
            ["repair.advertising_id"] = new(
                "repair.advertising_id",
                [new(
                    RepairChangeCapabilityIds.AdvertisingId,
                    "重置广告 ID",
                    "启用广告 ID 并删除旧标识，让 Windows 在后续需要时重新生成。",
                    CreateAdvertisingIdRepairTargetPayload())]),
            ["repair.game_dvr"] = new(
                "repair.game_dvr",
                [
                    new(RepairChangeCapabilityIds.GameDvrEnabled, "Enable Game DVR", "Restore the current-user Game DVR switch.", CreateDwordSetTargetPayload(1)),
                    new(RepairChangeCapabilityIds.GameDvrAppCapture, "Enable game capture", "Restore the current-user capture switch.", CreateDwordSetTargetPayload(1))
                ]),
            ["repair.privacy_settings"] = new(
                "repair.privacy_settings",
                [
                    new(RepairChangeCapabilityIds.LocationConsent, "Allow location access", "Restore current-user location consent.", CreateStringSetTargetPayload("Allow")),
                    new(RepairChangeCapabilityIds.WebcamConsent, "Allow camera access", "Restore current-user camera consent.", CreateStringSetTargetPayload("Allow")),
                    new(RepairChangeCapabilityIds.MicrophoneConsent, "Allow microphone access", "Restore current-user microphone consent.", CreateStringSetTargetPayload("Allow"))
                ]),
            ["repair.hags"] = MachineDwordBinding(
                "repair.hags", RepairChangeCapabilityIds.HagsEnable, "恢复 HAGS", "启用硬件加速 GPU 调度；需要受支持的 Windows 与显卡驱动。", 2),
            ["repair.diagnostic_data"] = MachineDwordBinding(
                "repair.diagnostic_data", RepairChangeCapabilityIds.DiagnosticDataReset, "重置诊断数据", "将 Windows 诊断数据策略恢复为完整级别。", 3),
            ["repair.fast_startup"] = MachineDwordBinding(
                "repair.fast_startup", RepairChangeCapabilityIds.FastStartupEnable, "恢复快速启动", "启用 Windows 快速启动；可能需要重启后生效。", 1),
            ["repair.telemetry"] = MachinePlatformBinding(
                "repair.telemetry", RepairChangeCapabilityIds.TelemetryServiceReset, "重置遥测服务", "恢复遥测服务；正式平台适配器未验证前保持阻断。"),
            ["repair.memory_compression"] = MachinePlatformBinding(
                "repair.memory_compression", RepairChangeCapabilityIds.MemoryCompressionEnable, "恢复内存压缩", "启用内存压缩；正式平台适配器未验证前保持阻断。"),
            ["repair.hibernation"] = MachinePlatformBinding(
                "repair.hibernation", RepairChangeCapabilityIds.HibernationEnable, "恢复休眠", "启用系统休眠；正式平台适配器未验证前保持阻断。"),
            ["repair.power_plan"] = MachinePlatformBinding(
                "repair.power_plan", RepairChangeCapabilityIds.PowerPlansReset, "重置电源计划", "该操作会删除自定义方案；缺少可逆快照时保持阻断。"),
            ["repair.power"] = MachinePlatformBinding(
                "repair.power", RepairChangeCapabilityIds.PowerPlansReset, "重置电源计划", "该操作会删除自定义方案；缺少可逆快照时保持阻断。"),
            ["repair.high_performance"] = MachinePlatformBinding(
                "repair.high_performance", RepairChangeCapabilityIds.HighPerformancePlanRestore, "恢复高性能方案", "缺少方案级快照与精确恢复契约时保持阻断。"),
            ["repair.ultimate_performance"] = MachinePlatformBinding(
                "repair.ultimate_performance", RepairChangeCapabilityIds.UltimatePerformancePlanRestore, "恢复卓越性能方案", "缺少方案级快照与精确恢复契约时保持阻断。"),
            ["repair.sleep"] = MachinePlatformBinding(
                "repair.sleep", RepairChangeCapabilityIds.SleepPolicyReset, "重置睡眠策略", "缺少 AC/DC 原值快照与精确恢复契约时保持阻断。"),
            ["repair.core_parking"] = MachinePlatformBinding(
                "repair.core_parking", RepairChangeCapabilityIds.CoreParkingReset, "重置核心停靠", "缺少活动方案和 AC/DC 原值快照时保持阻断。"),
            ["repair.cpu_frequency"] = MachinePlatformBinding(
                "repair.cpu_frequency", RepairChangeCapabilityIds.CpuFrequencyReset, "重置 CPU 频率", "缺少活动方案和 AC/DC 原值快照时保持阻断。"),
            ["repair.battery_report"] = MachinePlatformBinding(
                "repair.battery_report", RepairChangeCapabilityIds.BatteryReportGenerate, "生成电池报告", "输出路径、覆盖和隐私契约未验证前保持阻断。"),
            ["repair.power_troubleshoot"] = MachinePlatformBinding(
                "repair.power_troubleshoot", RepairChangeCapabilityIds.PowerTroubleshooterLaunch, "启动电源故障排除", "外部诊断工具生命周期和结果回读未验证前保持阻断。"),
            ["repair.services_reset"] = MachinePlatformBinding(
                "repair.services_reset", RepairChangeCapabilityIds.ServicesReset, "重置关键服务", "该批次跨 Windows Update、安装器与受保护服务，缺少逐项快照时保持阻断。"),
            ["repair.bits"] = MachinePlatformBinding(
                "repair.bits", RepairChangeCapabilityIds.BitsEnsureRunning, "修复 BITS 服务", "BITS 属于 Windows Update 域，由对应 Owner 接管前保持阻断。"),
            ["repair.cryptsvc"] = MachinePlatformBinding(
                "repair.cryptsvc", RepairChangeCapabilityIds.CryptSvcEnsureRunning, "修复加密服务", "CryptSvc 涉及签名与更新链，由对应 Owner 接管前保持阻断。"),
            ["repair.appinfo"] = MachinePlatformBinding(
                "repair.appinfo", RepairChangeCapabilityIds.AppInfoEnsureRunning, "修复应用程序信息服务", "确保 Appinfo 运行；保留启动类型并生成可恢复材料。"),
            ["repair.themes"] = MachinePlatformBinding(
                "repair.themes", RepairChangeCapabilityIds.ThemesEnsureRunning, "修复主题服务", "确保 Themes 运行；保留启动类型并生成可恢复材料。"),
            ["repair.audio_service"] = MachinePlatformBinding(
                "repair.audio_service", RepairChangeCapabilityIds.AudioServiceEnsureRunning, "确保 Windows Audio 服务运行", "只确保 Audiosrv 运行；不执行停止后再启动，并生成可恢复材料。"),
            ["repair.shellhwdetection"] = MachinePlatformBinding(
                "repair.shellhwdetection", RepairChangeCapabilityIds.ShellHardwareDetectionEnsureRunning, "修复硬件检测服务", "硬件检测属于设备域，由对应 Owner 接管前保持阻断。"),
            ["repair.plugplay"] = MachinePlatformBinding(
                "repair.plugplay", RepairChangeCapabilityIds.PlugPlayEnsureRunning, "修复即插即用服务", "PlugPlay 是受保护设备服务，禁止通用服务 Owner 修改。"),
            ["repair.netprofm"] = MachinePlatformBinding(
                "repair.netprofm", RepairChangeCapabilityIds.NetworkListEnsureRunning, "修复网络列表服务", "网络服务由 Network Owner 接管前保持阻断。"),
            ["repair.nlasvc"] = MachinePlatformBinding(
                "repair.nlasvc", RepairChangeCapabilityIds.NetworkLocationEnsureRunning, "修复网络位置感知", "网络服务由 Network Owner 接管前保持阻断。"),
            ["repair.rpcss"] = MachinePlatformBinding(
                "repair.rpcss", RepairChangeCapabilityIds.RpcSsEnsureRunning, "修复 RPC 服务", "RpcSs 是系统关键服务，禁止通用服务 Owner 修改。")
        };
        foreach (var item in SystemMaintenanceRepairCatalog.All)
        {
            bindings.Add(
                item.RepairItemId,
                MachinePlatformBinding(
                    item.RepairItemId,
                    item.CapabilityId,
                    item.Title,
                    item.BlockReason));
        }

        foreach (var item in BootMemoryRepairCatalog.All)
        {
            bindings.Add(
                item.RepairItemId,
                MachinePlatformBinding(
                    item.RepairItemId,
                    item.CapabilityId,
                    item.Title,
                    item.BlockReason));
        }

        foreach (var item in CpuSchedulingRepairCatalog.All)
        {
            bindings.Add(
                item.RepairItemId,
                MachinePlatformBinding(
                    item.RepairItemId,
                    item.CapabilityId,
                    item.Title,
                    item.BlockReason));
        }

        return bindings;
    }

    private readonly IChangeOrchestrator _orchestrator;
    private readonly IChangeConfirmationAuthority _confirmationAuthority;
    private readonly TimeProvider _timeProvider;
    private readonly INetworkResetOwner? _networkResetOwner;
    private readonly IResidualRepairOwner _residualRepairOwner;

    public OrchestratedRepairChangeExecutor(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority confirmationAuthority,
        TimeProvider? timeProvider = null,
        INetworkResetOwner? networkResetOwner = null,
        IResidualRepairOwner? residualRepairOwner = null)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _confirmationAuthority = confirmationAuthority ?? throw new ArgumentNullException(nameof(confirmationAuthority));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _networkResetOwner = networkResetOwner;
        _residualRepairOwner = residualRepairOwner ?? FailClosedResidualRepairOwner.Instance;
    }

    public bool IsManaged(string repairItemId) =>
        Bindings.ContainsKey(repairItemId)
        || NetworkRepairCatalog.Contains(repairItemId)
        || FileShellRepairCatalog.Contains(repairItemId)
        || GraphicsDisplayRepairCatalog.Contains(repairItemId)
        || ResidualRepairCatalog.Contains(repairItemId);

    public async Task<RepairResult> ExecuteAsync(
        RepairItem item,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (NetworkRepairCatalog.TryGet(item.Id, out var networkRepair))
        {
            return await ExecuteNetworkRepairAsync(networkRepair, progress, cancellationToken).ConfigureAwait(false);
        }

        if (FileShellRepairCatalog.TryGet(item.Id, out var fileShellRepair))
        {
            return await FileShellRepairBoundary.DenyAsync(
                fileShellRepair.RepairItemId,
                fileShellRepair.MutationKind,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        if (GraphicsDisplayRepairCatalog.TryGet(item.Id, out var graphicsDisplayRepair))
        {
            return await GraphicsRepairBoundary.DenyAsync(
                graphicsDisplayRepair.RepairItemId,
                graphicsDisplayRepair.MutationKind,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        if (ResidualRepairCatalog.TryGet(item.Id, out var residualRepair))
        {
            return await _residualRepairOwner.ExecuteAsync(
                residualRepair,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        if (!Bindings.TryGetValue(item.Id, out var binding))
        {
            return RepairResult.Blocked(
                $"Repair item is not managed by the unified change orchestrator: {item.Id}",
                "repair.executor-binding-missing");
        }

        var operationReceipts = new List<RepairChangeOperationReceipt>(binding.Steps.Count);
        var mutationOccurred = false;
        try
        {
            for (var index = 0; index < binding.Steps.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = binding.Steps[index];
                progress?.Report(new RepairProgress
                {
                    Message = $"正在准备：{step.Title}",
                    PercentComplete = index * 100 / binding.Steps.Count,
                    CurrentStep = $"{index + 1}/{binding.Steps.Count}"
                });

                var preparation = await _orchestrator.PrepareAsync(
                    new ChangeRequest(step.CapabilityId, CreateScope(step.ScopeKind), step.TargetPayload),
                    cancellationToken).ConfigureAwait(false);
                if (preparation.Status == ChangePreparationStatus.NoChange)
                {
                    continue;
                }

                if (preparation.Status != ChangePreparationStatus.Ready || preparation.Plan is null)
                {
                    return await FailAndCompensateAsync(
                        binding,
                        operationReceipts,
                        RepairResult.FromReasonCode(
                            preparation.Message,
                            preparation.ReasonCode,
                            availability: RepairExecutionAvailabilityCatalog.Get(binding.RepairItemId)),
                        progress).ConfigureAwait(false);
                }

                ConfirmationToken confirmation;
                if (preparation.Plan.RequiresExplicitConfirmation)
                {
                    var issued = await _confirmationAuthority.RequestAsync(
                        preparation.Plan,
                        step.Title,
                        step.ConfirmationSummary,
                        cancellationToken).ConfigureAwait(false);
                    if (issued is null)
                    {
                        return await FailAndCompensateAsync(
                            binding,
                            operationReceipts,
                            RepairResult.Blocked(
                                "用户未确认或确认计划已过期，未继续写入。",
                                "confirmation.denied",
                                availability: RepairExecutionAvailabilityCatalog.Get(binding.RepairItemId)),
                            progress).ConfigureAwait(false);
                    }

                    confirmation = issued;
                }
                else
                {
                    confirmation = ConfirmationToken.ForPreparedPlan(preparation.Plan);
                }

                var receipt = await _orchestrator.ExecuteAsync(
                    preparation.Plan,
                    confirmation,
                    cancellationToken).ConfigureAwait(false);
                operationReceipts.Add(ToRepairReceipt(receipt.Operation));
                mutationOccurred |= receipt.Operation.MutationOccurred;
                if (receipt.Outcome is not (ChangeOutcome.SucceededVerified or ChangeOutcome.NoChange))
                {
                    return await FailAndCompensateAsync(
                        binding,
                        operationReceipts,
                        FromChangeOutcome(receipt.Operation, binding.RepairItemId),
                        progress).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return await FailAndCompensateAsync(
                binding,
                operationReceipts,
                RepairResult.Cancelled(
                    "修复已取消；已完成步骤将尝试恢复。",
                    "cancelled.repair",
                    availability: RepairExecutionAvailability.WorkingBidirectional),
                progress).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var availability = RepairExecutionAvailabilityCatalog.Get(binding.RepairItemId);
            var failure = RepairResult.FromException(
                "修复执行失败；未将异常当作成功。",
                ex,
                availability);
            if (failure.Code == RepairResultCode.Failed
                && availability != RepairExecutionAvailability.WorkingBidirectional)
            {
                failure = RepairResult.Blocked(
                    failure.Message,
                    "repair.executor-failed",
                    failure.Detail,
                    availability) with { Exception = failure.Exception };
            }

            return await FailAndCompensateAsync(
                binding,
                operationReceipts,
                failure,
                progress).ConfigureAwait(false);
        }

        progress?.Report(new RepairProgress { Message = "修复已完成并回读验证。", PercentComplete = 100 });
        var result = mutationOccurred
            ? RepairResult.Succeeded("修复已通过统一协调器执行并回读验证。")
            : RepairResult.NoChange("当前状态已经符合目标；未执行系统写入。");
        return result with
        {
            ChangeReceipt = CreateGroupReceipt(binding.RepairItemId, operationReceipts, compensationAttempted: false)
        };
    }

    public async Task<RepairResult> RestoreAsync(
        RepairRecoveryRequest request,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (NetworkRepairCatalog.Contains(request.RepairItemId))
        {
            return RepairResult.Failed(
                "网络修复使用 Network Reset Owner 的独立 durable receipt；通用 Repair recovery 回执不能伪装为网络恢复材料。");
        }


        if (FileShellRepairCatalog.Contains(request.RepairItemId))
        {
            return await FileShellRepairBoundary.DenyRestoreAsync(
                request.RepairItemId,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        if (GraphicsDisplayRepairCatalog.Contains(request.RepairItemId))
        {
            return await GraphicsRepairBoundary.DenyRestoreAsync(
                request.RepairItemId,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        if (ResidualRepairCatalog.TryGet(request.RepairItemId, out var residualRepair))
        {
            return await _residualRepairOwner.RestoreAsync(
                residualRepair,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        if (!Bindings.TryGetValue(request.RepairItemId, out var binding))
        {
            return RestoreRequestBlocked(
                request,
                "恢复项未绑定到统一协调器，已拒绝任何旧 delegate 或 snapshot 恢复。",
                "restore.request-item-unmanaged");
        }

        var sourceReceipt = request.Receipt;
        if (!string.Equals(request.RepairItemId, sourceReceipt.RepairItemId, StringComparison.OrdinalIgnoreCase))
        {
            return RestoreRequestBlocked(
                request,
                "恢复回执与修复项不匹配，已拒绝恢复。",
                "restore.request-item-mismatch");
        }

        if (sourceReceipt.ReceiptId == Guid.Empty)
        {
            return RestoreRequestBlocked(
                request,
                "恢复请求缺少有效的 Repair ReceiptId，已拒绝恢复。",
                "restore.request-receipt-invalid");
        }

        if (sourceReceipt.Operations is null || sourceReceipt.Operations.Count == 0)
        {
            return RestoreRequestBlocked(
                request,
                "恢复请求缺少 durable operation 指针，已拒绝恢复。",
                "restore.request-operations-invalid");
        }

        var pointers = sourceReceipt.Operations
            .Where(operation => operation.RecoveryId is not null)
            .ToArray();
        if (pointers.Length == 0)
        {
            return RepairResult.RecoveryRequired(
                "恢复回执没有 RecoveryId；无法证明同源恢复材料，恢复状态未知。",
                "restore.recovery-id-missing",
                availability: RepairExecutionAvailabilityCatalog.Get(binding.RepairItemId)) with
            {
                ChangeReceipt = sourceReceipt
            };
        }

        var expectedCapabilities = binding.Steps
            .Select(step => step.CapabilityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pointers.Any(pointer =>
                pointer.ReceiptId == Guid.Empty
                || pointer.RecoveryId == Guid.Empty
                || string.IsNullOrWhiteSpace(pointer.CapabilityId)))
        {
            return RestoreRequestBlocked(
                request,
                "恢复请求包含无效的 ReceiptId、RecoveryId 或能力指针，已拒绝恢复。",
                "restore.request-pointer-invalid");
        }

        if (pointers.Any(pointer => !expectedCapabilities.Contains(pointer.CapabilityId)))
        {
            return RestoreRequestBlocked(
                request,
                "恢复请求中的能力指针不属于当前 Repair 绑定，已拒绝恢复。",
                "restore.request-capability-mismatch");
        }

        if (pointers.GroupBy(pointer => pointer.ReceiptId).Any(group =>
                group.Select(pointer => pointer.RecoveryId).Distinct().Count() != 1)
            || pointers.GroupBy(pointer => (pointer.ReceiptId, pointer.RecoveryId)).Any(group => group.Count() > 1))
        {
            return RestoreRequestBlocked(
                request,
                "恢复请求包含重复或互相冲突的 durable 指针，已拒绝恢复。",
                "restore.request-pointer-duplicate");
        }

        var restored = new List<RepairChangeOperationReceipt>(pointers.Length);
        try
        {
            foreach (var pointer in pointers.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                BatchReceipt receipt;
                try
                {
                    receipt = await _orchestrator.RestoreAsync(
                        new RestoreRequest(pointer.ReceiptId, pointer.RecoveryId),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    return RestoreFailure(
                        request,
                        restored,
                        RepairResult.Cancelled(
                            "恢复已取消；尚未确认所有 durable 指针的最终状态。",
                            "cancelled.restore",
                            ex.Message,
                            RepairExecutionAvailabilityCatalog.Get(binding.RepairItemId)));
                }
                catch (Exception ex)
                {
                    return RestoreFailure(
                        request,
                        restored,
                        FromRestoreException(ex, request.RepairItemId));
                }

                var operation = receipt.Operation;
                if (operation.Outcome == ChangeOutcome.RestoredVerified
                    && operation.RecoveryId != pointer.RecoveryId)
                {
                    return RestoreFailure(
                        request,
                        restored,
                        RepairResult.RecoveryRequired(
                            "协调器返回的恢复结果未绑定到请求中的 RecoveryId；最终状态未知。",
                            "restore.result-recovery-mismatch",
                            availability: RepairExecutionAvailabilityCatalog.Get(binding.RepairItemId)));
                }

                var observed = pointer with
                {
                    Outcome = operation.Outcome,
                    MutationOccurred = operation.MutationOccurred
                };
                restored.Add(observed);
                if (operation.Outcome != ChangeOutcome.RestoredVerified)
                {
                    return RestoreFailure(
                        request,
                        restored,
                        FromRestoreOutcome(operation, request.RepairItemId));
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            return RestoreFailure(
                request,
                restored,
                RepairResult.Cancelled(
                    "恢复已取消；尚未确认所有 durable 指针的最终状态。",
                    "cancelled.restore",
                    ex.Message,
                    RepairExecutionAvailabilityCatalog.Get(binding.RepairItemId)));
        }
        catch (Exception ex)
        {
            return RestoreFailure(
                request,
                restored,
                FromRestoreException(ex, request.RepairItemId));
        }

        progress?.Report(new RepairProgress { Message = "恢复已完成并回读验证。", PercentComplete = 100 });
        return RepairResult.Succeeded("修复项已按同源 durable recovery 回执恢复。") with
        {
            ChangeReceipt = MergeRestoreReceipt(sourceReceipt, restored)
        };
    }

    private async Task<RepairResult> ExecuteNetworkRepairAsync(
        NetworkRepairDefinition definition,
        IProgress<RepairProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!definition.UsesNetworkResetOwner)
        {
            return RepairResult.Blocked(
                definition.BlockReason,
                "network.owner-not-applicable",
                availability: RepairExecutionAvailability.SafetyBlocked);
        }

        if (_networkResetOwner is null)
        {
            return RepairResult.Blocked(
                "Network Reset Owner 未接入，已安全阻断；不会回退旧网络命令直写。",
                "network.owner-unavailable",
                availability: RepairExecutionAvailability.FailClosed);
        }

        progress?.Report(new RepairProgress { Message = "正在准备网络快照与安全计划…", PercentComplete = 10 });
        var receipt = await _networkResetOwner.RunInteractiveAsync(
            definition.Profile!.Value,
            cancellationToken).ConfigureAwait(false);
        progress?.Report(new RepairProgress { Message = receipt.Message, PercentComplete = 100 });

        return receipt.Outcome switch
        {
            NetworkResetOutcome.SucceededVerified
                or NetworkResetOutcome.AppliedRequiresRestartManualRecovery =>
                RepairResult.Succeeded(receipt.Message, receipt.RebootRequired),
            NetworkResetOutcome.CancelledBeforeWrite =>
                RepairResult.Cancelled(receipt.Message, receipt.ReasonCode),
            NetworkResetOutcome.Blocked or NetworkResetOutcome.Denied or NetworkResetOutcome.Conflict =>
                RepairResult.FromReasonCode(receipt.Message, receipt.ReasonCode),
            _ => RepairResult.Failed(receipt.Message, reasonCode: receipt.ReasonCode)
        };
    }

    private async Task<RepairResult> FailAndCompensateAsync(
        RepairChangeBinding binding,
        List<RepairChangeOperationReceipt> receipts,
        RepairResult failure,
        IProgress<RepairProgress>? progress)
    {
        var compensationAttempted = receipts.Any(receipt => receipt.RecoveryId is not null);
        if (compensationAttempted)
        {
            progress?.Report(new RepairProgress { Message = "修复未完整完成，正在恢复已变更步骤。", PercentComplete = 90 });
            foreach (var pointer in receipts.AsEnumerable().Reverse())
            {
                if (pointer.RecoveryId is null)
                {
                    continue;
                }

                try
                {
                    await _orchestrator.RestoreAsync(
                        new RestoreRequest(pointer.ReceiptId, pointer.RecoveryId),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // The original durable receipt remains the recovery authority. Never hide the failure.
                }
            }
        }

        return failure with
        {
            ChangeReceipt = CreateGroupReceipt(binding.RepairItemId, receipts, compensationAttempted)
        };
    }

    private static RepairResult FromChangeOutcome(
        OperationReceipt operation,
        string repairItemId)
    {
        var availability = RepairExecutionAvailabilityCatalog.Get(repairItemId);
        return operation.Outcome switch
        {
            ChangeOutcome.NoChange => RepairResult.NoChange(operation.Message, availability),
            ChangeOutcome.CancelledBeforeWrite or ChangeOutcome.CancelledAfterPartialWrite =>
                RepairResult.Cancelled(operation.Message, operation.ReasonCode, availability: availability),
            ChangeOutcome.Denied => RepairResult.FromReasonCode(
                operation.Message,
                operation.ReasonCode,
                availability: availability),
            ChangeOutcome.Blocked or ChangeOutcome.NotApplicable or ChangeOutcome.Conflict =>
                RepairResult.FromReasonCode(
                    operation.Message,
                    operation.ReasonCode,
                    availability: availability),
            _ => RepairResult.Failed(
                operation.Message,
                detail: operation.ReasonCode,
                reasonCode: operation.ReasonCode,
                availability: availability)
        };
    }

    private static RepairResult FromRestoreOutcome(
        OperationReceipt operation,
        string repairItemId)
    {
        var availability = RepairExecutionAvailabilityCatalog.Get(repairItemId);
        return operation.Outcome switch
        {
            ChangeOutcome.RestoredVerified => RepairResult.Succeeded(operation.Message, availability: availability),
            ChangeOutcome.CancelledBeforeWrite => RepairResult.Cancelled(
                operation.Message,
                operation.ReasonCode,
                availability: availability),
            ChangeOutcome.Denied => RepairResult.PermissionDenied(
                operation.Message,
                operation.ReasonCode ?? "permission.denied",
                availability: availability),
            ChangeOutcome.NotApplicable => RepairResult.FromReasonCode(
                operation.Message,
                operation.ReasonCode,
                availability: availability),
            ChangeOutcome.Blocked => RestoreBlockedOutcome(operation, availability),
            _ => RepairResult.RecoveryRequired(
                operation.Message,
                operation.ReasonCode ?? "restore.recovery-required",
                availability: availability)
        };
    }

    private static RepairResult RestoreBlockedOutcome(
        OperationReceipt operation,
        RepairExecutionAvailability availability)
    {
        var classified = RepairResult.FromReasonCode(
            operation.Message,
            operation.ReasonCode,
            availability: availability);
        return classified.Code is
            RepairResultCode.PermissionDenied
                or RepairResultCode.VersionUnsupported
                or RepairResultCode.NotApplicable
                or RepairResultCode.PolicyDenied
                or RepairResultCode.Cancelled
                ? classified
                : RepairResult.RecoveryRequired(
                    operation.Message,
                    operation.ReasonCode ?? "restore.recovery-required",
                    availability: availability);
    }

    private static RepairResult FromRestoreException(Exception exception, string repairItemId)
    {
        var availability = RepairExecutionAvailabilityCatalog.Get(repairItemId);
        var mapped = RepairResult.FromException(
            "恢复协调器未返回可验证终态；恢复状态未知。",
            exception,
            availability);
        return mapped.Code == RepairResultCode.Failed
            ? RepairResult.RecoveryRequired(
                mapped.Message,
                "restore.execution-exception",
                mapped.Detail,
                availability) with { Exception = mapped.Exception }
            : mapped;
    }

    private static RepairResult RestoreRequestBlocked(
        RepairRecoveryRequest request,
        string message,
        string reasonCode)
    {
        var availability = RepairExecutionAvailabilityCatalog.Get(request.RepairItemId);
        return RepairResult.Blocked(message, reasonCode, availability: availability) with
        {
            ChangeReceipt = request.Receipt
        };
    }

    private RepairResult RestoreFailure(
        RepairRecoveryRequest request,
        IReadOnlyList<RepairChangeOperationReceipt> restored,
        RepairResult result) =>
        result with
        {
            ChangeReceipt = MergeRestoreReceipt(request.Receipt, restored)
        };

    private RepairChangeReceipt MergeRestoreReceipt(
        RepairChangeReceipt source,
        IReadOnlyList<RepairChangeOperationReceipt> restored) =>
        source with
        {
            Operations = source.Operations
                .Select(operation => restored.FirstOrDefault(updated =>
                    updated.ReceiptId == operation.ReceiptId
                    && updated.RecoveryId == operation.RecoveryId) ?? operation)
                .ToArray(),
            CompensationAttempted = true,
            CompletedAtUtc = _timeProvider.GetUtcNow()
        };

    private RepairChangeReceipt CreateGroupReceipt(
        string repairItemId,
        IReadOnlyList<RepairChangeOperationReceipt> operations,
        bool compensationAttempted) =>
        new(
            Guid.NewGuid(),
            repairItemId,
            operations.ToArray(),
            compensationAttempted,
            _timeProvider.GetUtcNow());

    private static RepairChangeOperationReceipt ToRepairReceipt(OperationReceipt receipt) =>
        new(receipt.CapabilityId, receipt.ReceiptId, receipt.RecoveryId, receipt.Outcome, receipt.MutationOccurred);

    private static TargetScope CreateScope(TargetScopeKind kind)
    {
        if (kind == TargetScopeKind.Machine)
        {
            return TargetScope.Machine(Environment.MachineName);
        }

        if (kind != TargetScopeKind.CurrentUser)
        {
            throw new InvalidOperationException($"修复协调器不支持目标作用域：{kind}");
        }

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
        {
            throw new InvalidOperationException("无法确定当前 Windows 用户 SID，已阻断当前用户修复。 ");
        }

        return TargetScope.CurrentUser(sid);
    }

    private static string CreateAdvertisingIdRepairTargetPayload() =>
        JsonSerializer.Serialize(new ValueSetTargetRequest(
        [
            new("Enabled", 1, null, 1, 0),
            new("Id", 0, null, null, 1)
        ]));

    private static string CreateDwordSetTargetPayload(int value) =>
        JsonSerializer.Serialize(new DwordTargetRequest(Action: 0, Value: value));

    private static string CreateStringSetTargetPayload(string value) =>
        JsonSerializer.Serialize(new StringTargetRequest(Action: 0, Value: value, RequireExistingValue: true));

    private static RepairChangeBinding MachineDwordBinding(
        string repairItemId,
        string capabilityId,
        string title,
        string confirmation,
        int value) =>
        new(repairItemId, [new(capabilityId, title, confirmation, CreateDwordSetTargetPayload(value), TargetScopeKind.Machine)]);

    private static RepairChangeBinding MachinePlatformBinding(
        string repairItemId,
        string capabilityId,
        string title,
        string confirmation) =>
        new(repairItemId, [new(capabilityId, title, confirmation, "apply", TargetScopeKind.Machine)]);

    private sealed record ValueSetTargetRequest(IReadOnlyList<TypedTargetValue> Values);
    private sealed record TypedTargetValue(
        string ValueName,
        int Kind,
        string? StringValue,
        int? DwordValue,
        int Action);
    private sealed record DwordTargetRequest(int Action, int? Value);
    private sealed record StringTargetRequest(int Action, string? Value, bool RequireExistingValue);
}
