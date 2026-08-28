using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Utilities;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

[SupportedOSPlatform("windows")]
public sealed class OptimizeAfdParametersOperation : IOptimizationOperation
{
    private const string AfdPath = @"SYSTEM\CurrentControlSet\Services\AFD\Parameters";

    private static readonly MultiRegistryValueOperation DelegateOperation = new(
        id: "network.optimize_afd",
        title: "优化 Winsock 参数",
        description: "优化 AFD 接收/发送窗口参数，提升网络吞吐量",
        category: "网络优化",
        definitions: new[]
        {
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.LocalMachine, AfdPath, "DefaultReceiveWindow", 65535, RegistryValueKind.DWord),
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.LocalMachine, AfdPath, "DefaultSendWindow", 65535, RegistryValueKind.DWord),
        });

    public string Id => "network.optimize_afd";
    public string Title => "优化 Winsock 参数";
    public string Description => "优化 AFD 接收/发送窗口参数，提升网络吞吐量";
    public string Category => "网络优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; } = "提升网络吞吐量";
    public string? Drawback { get; init; } = "可能增加内存占用";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKLM";
    public string RegistryPath => AfdPath;
    public string RegistryValueName => "DefaultReceiveWindow;DefaultSendWindow";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.OpenSubKey(AfdPath);
        var receive = key?.GetValue("DefaultReceiveWindow") as int?;
        var send = key?.GetValue("DefaultSendWindow") as int?;
        var optimized = receive >= 65535 && send >= 65535;
        return Task.FromResult(new OperationStatus(optimized, optimized ? "AFD 已优化" : "AFD 使用默认参数"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已优化 AFD 参数", "AFD 参数已优化");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复 AFD 参数", "AFD 参数已恢复");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableQosPacketSchedulerOperation : IOptimizationOperation
{
    private const string QosPath = @"SOFTWARE\Policies\Microsoft\Windows\Psched";

    private static readonly RegistryToggleOperation DelegateOperation = new(
        id: "network.disable_qos_scheduler",
        title: "禁用 QoS 数据包调度",
        description: "禁用 QoS 数据包调度，释放系统默认保留的 20% 带宽",
        category: "网络优化",
        hive: RegistryHive.LocalMachine,
        subKeyPath: QosPath,
        valueName: "NonBestEffortLimit",
        desiredValue: 0,
        valueKind: RegistryValueKind.DWord);

    public string Id => "network.disable_qos_scheduler";
    public string Title => "禁用 QoS 数据包调度";
    public string Description => "禁用 QoS 数据包调度，释放系统默认保留的 20% 带宽";
    public string Category => "网络优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "释放 QoS 保留的带宽";
    public string? Drawback { get; init; } = "可能影响需要 QoS 的应用";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKLM";
    public string RegistryPath => QosPath;
    public string RegistryValueName => "NonBestEffortLimit";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.OpenSubKey(QosPath);
        var value = key?.GetValue("NonBestEffortLimit") as int? ?? 20;
        var disabled = value == 0;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "QoS 已禁用" : "QoS 已启用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用 QoS 调度", "QoS 调度已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复 QoS 调度", "QoS 调度已恢复");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableNetworkThrottlingCompleteOperation : IOptimizationOperation
{
    private const string MmPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const int DisabledThrottleValue = unchecked((int)0xFFFFFFFF);

    private static readonly MultiRegistryValueOperation DelegateOperation = new(
        id: "network.disable_throttling_complete",
        title: "禁用网络限流",
        description: "将 NetworkThrottlingIndex 设为最大值，SystemResponsiveness 设为 0，完全禁用网络限流",
        category: "网络优化",
        definitions: new[]
        {
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.LocalMachine, MmPath, "NetworkThrottlingIndex", DisabledThrottleValue, RegistryValueKind.DWord),
            new MultiRegistryValueOperation.RegistryValueDefinition(RegistryHive.LocalMachine, MmPath, "SystemResponsiveness", 0, RegistryValueKind.DWord),
        });

    public string Id => "network.disable_throttling_complete";
    public string Title => "禁用网络限流";
    public string Description => "将 NetworkThrottlingIndex 设为最大值，SystemResponsiveness 设为 0，完全禁用网络限流";
    public string Category => "网络优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; } = "提升网络传输性能";
    public string? Drawback { get; init; } = "可能影响系统响应";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKLM";
    public string RegistryPath => MmPath;
    public string RegistryValueName => "NetworkThrottlingIndex;SystemResponsiveness";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.OpenSubKey(MmPath);
        var throttle = key?.GetValue("NetworkThrottlingIndex") as int? ?? 10;
        var responsiveness = key?.GetValue("SystemResponsiveness") as int? ?? 20;
        var disabled = throttle == DisabledThrottleValue && responsiveness == 0;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "网络限流已禁用" : "网络限流已启用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用网络限流", "网络限流已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复网络限流", "网络限流已恢复");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableDeliveryOptimizationOperation : IOptimizationOperation
{
    private const string DoPath = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";

    private static readonly RegistryToggleOperation DelegateOperation = new(
        id: "network.disable_delivery_optimization",
        title: "禁用传递优化",
        description: "禁用 Windows 更新的 P2P 传递优化，减少后台网络占用",
        category: "网络优化",
        hive: RegistryHive.LocalMachine,
        subKeyPath: DoPath,
        valueName: "DODownloadMode",
        desiredValue: 0,
        valueKind: RegistryValueKind.DWord);

    public string Id => "network.disable_delivery_optimization";
    public string Title => "禁用传递优化";
    public string Description => "禁用 Windows 更新的 P2P 传递优化，减少后台网络占用";
    public string Category => "网络优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "减少后台网络占用";
    public string? Drawback { get; init; } = "Windows 更新下载变慢";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKLM";
    public string RegistryPath => DoPath;
    public string RegistryValueName => "DODownloadMode";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.OpenSubKey(DoPath);
        var mode = key?.GetValue("DODownloadMode") as int? ?? 1;
        var disabled = mode == 0;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "传递优化已禁用" : "传递优化已启用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用传递优化", "传递优化已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复传递优化", "传递优化已恢复");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableNetBiosOperation : IOptimizationOperation
{
    private const string TcpipInterfacesPath = @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters\Interfaces";
    private const string ValueName = "NetbiosOptions";
    private const int DisabledValue = 2;

    private sealed record InterfaceValueSnapshot(bool ValueExists, int? Value);
    private sealed record NetBiosSnapshot(IReadOnlyDictionary<string, InterfaceValueSnapshot> OriginalValues);

    public string Id => "network.disable_netbios";
    public string Title => "禁用 NetBIOS over TCP/IP";
    public string Description => "禁用 NetBIOS over TCP/IP，减少网络广播和名称解析开销";
    public string Category => "网络优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit { get; init; } = "减少网络广播开销";
    public string? Drawback { get; init; } = "可能影响依赖 NetBIOS 的旧应用";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKLM";
    public string RegistryPath => TcpipInterfacesPath;
    public string RegistryValueName => ValueName;
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var baseKey = Registry.LocalMachine.OpenSubKey(TcpipInterfacesPath);
        if (baseKey is null)
        {
            return Task.FromResult(new OperationStatus(false, "无法读取接口"));
        }

        var interfaceNames = baseKey.GetSubKeyNames();
        if (interfaceNames.Length == 0)
        {
            return Task.FromResult(new OperationStatus(false, "未找到网络接口"));
        }

        foreach (var subKeyName in interfaceNames)
        {
            using var subKey = baseKey.OpenSubKey(subKeyName, writable: false);
            var current = ReadInterfaceValue(subKey);
            if (!current.ValueExists || current.Value != DisabledValue)
            {
                return Task.FromResult(new OperationStatus(false, "存在未禁用 NetBIOS 的接口"));
            }
        }

        return Task.FromResult(new OperationStatus(true, "所有接口均已禁用 NetBIOS"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpipInterfacesPath, writable: true);
            if (baseKey is null)
            {
                return new OperationInvokeResult(false, false, "无法打开接口路径");
            }

            var snapshot = CaptureInterfaceStates(baseKey);
            if (snapshot.OriginalValues.Count == 0)
            {
                return new OperationInvokeResult(true, false, "未找到网络接口");
            }

            var alreadyDisabled = snapshot.OriginalValues.Values.All(static state => state.ValueExists && state.Value == DisabledValue);
            if (alreadyDisabled)
            {
                return new OperationInvokeResult(true, false, "NetBIOS 已全部禁用");
            }

            await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);

            try
            {
                var applyResult = ApplyDisabledState(baseKey, snapshot.OriginalValues.Keys);
                if (!applyResult.Success)
                {
                    return await BuildRollbackFailureResultAsync(context, snapshot, applyResult.Message, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return await BuildRollbackFailureResultAsync(
                    context,
                    snapshot,
                    string.Concat("Failed to apply NetBIOS optimization. ", ex.Message),
                    cancellationToken).ConfigureAwait(false);
            }

            return new OperationInvokeResult(true, true, $"已禁用 {snapshot.OriginalValues.Count} 个接口的 NetBIOS");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "网络优化");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await context.LoadSnapshotAsync<NetBiosSnapshot>(Id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null || snapshot.OriginalValues.Count == 0)
            {
                return new OperationInvokeResult(false, false, "NetBIOS 没有可恢复的快照");
            }

            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpipInterfacesPath, writable: true);
            if (baseKey is null)
            {
                return new OperationInvokeResult(false, false, "无法打开接口路径");
            }

            var restoreResult = RestoreSnapshot(baseKey, snapshot);
            if (!restoreResult.Success)
            {
                return new OperationInvokeResult(false, restoreResult.RestoredCount > 0, restoreResult.Message);
            }

            return new OperationInvokeResult(true, restoreResult.RestoredCount > 0, "已恢复 NetBIOS 设置");
        }
        catch (Exception ex)
        {
            return OperationErrorAnalyzer.HandleOperationException(ex, "网络优化");
        }
    }

    private static NetBiosSnapshot CaptureInterfaceStates(RegistryKey baseKey)
    {
        var values = new Dictionary<string, InterfaceValueSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var subKeyName in baseKey.GetSubKeyNames())
        {
            using var subKey = baseKey.OpenSubKey(subKeyName, writable: false);
            values[subKeyName] = ReadInterfaceValue(subKey);
        }

        return new NetBiosSnapshot(values);
    }

    private static InterfaceValueSnapshot ReadInterfaceValue(RegistryKey? key)
    {
        if (key is null)
        {
            return new InterfaceValueSnapshot(false, null);
        }

        var raw = key.GetValue(ValueName);
        return raw is null
            ? new InterfaceValueSnapshot(false, null)
            : new InterfaceValueSnapshot(true, Convert.ToInt32(raw));
    }

    private static (bool Success, string Message) ApplyDisabledState(RegistryKey baseKey, IEnumerable<string> interfaceNames)
    {
        foreach (var subKeyName in interfaceNames)
        {
            using var subKey = baseKey.OpenSubKey(subKeyName, writable: true);
            if (subKey is null)
            {
                return (false, $"接口 {subKeyName} 无法写入 NetBIOS 值");
            }

            subKey.SetValue(ValueName, DisabledValue, RegistryValueKind.DWord);
            var verify = ReadInterfaceValue(subKey);
            if (!verify.ValueExists || verify.Value != DisabledValue)
            {
                return (false, $"接口 {subKeyName} 的 NetBIOS 写入验证失败");
            }
        }

        return (true, "已禁用 NetBIOS");
    }

    private async Task<OperationInvokeResult> BuildRollbackFailureResultAsync(
        OperationExecutionContext context,
        NetBiosSnapshot snapshot,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var rollback = await TryRestoreOriginalStateAsync(context, snapshot, cancellationToken).ConfigureAwait(false);
        var message = rollback.Success
            ? failureMessage + " Original NetBIOS registry state remained intact or was restored."
            : failureMessage + " Automatic rollback failed. " + rollback.Message;
        return new OperationInvokeResult(false, false, message);
    }

    private Task<(bool Success, string Message)> TryRestoreOriginalStateAsync(
        OperationExecutionContext context,
        NetBiosSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        return OperationRollbackHelper.TryRestoreOriginalStateAsync(
            context,
            Id,
            () => MatchesSnapshot(snapshot),
            () => RestoreSnapshotState(snapshot),
            "Original NetBIOS registry state remained intact.",
            "Original NetBIOS registry state restored.",
            cancellationToken);
    }

    private static bool MatchesSnapshot(NetBiosSnapshot snapshot)
    {
        using var baseKey = Registry.LocalMachine.OpenSubKey(TcpipInterfacesPath, writable: false);
        if (baseKey is null)
        {
            return false;
        }

        foreach (var pair in snapshot.OriginalValues)
        {
            using var subKey = baseKey.OpenSubKey(pair.Key, writable: false);
            var current = ReadInterfaceValue(subKey);
            var matched = pair.Value.ValueExists
                ? current.ValueExists && current.Value == pair.Value.Value
                : !current.ValueExists;
            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static (bool Success, string Message) RestoreSnapshotState(NetBiosSnapshot snapshot)
    {
        using var baseKey = Registry.LocalMachine.OpenSubKey(TcpipInterfacesPath, writable: true);
        if (baseKey is null)
        {
            return (false, "Unable to open NetBIOS registry path for rollback.");
        }

        var rollback = RestoreSnapshot(baseKey, snapshot);
        return rollback.Success
            ? (true, string.Empty)
            : (false, rollback.Message);
    }

    private static (bool Success, int RestoredCount, string Message) RestoreSnapshot(RegistryKey baseKey, NetBiosSnapshot snapshot)
    {
        var restored = 0;
        var failed = new List<string>();

        foreach (var pair in snapshot.OriginalValues)
        {
            using var subKey = baseKey.OpenSubKey(pair.Key, writable: true);
            if (subKey is null)
            {
                failed.Add(pair.Key + "(无法打开)");
                continue;
            }

            if (pair.Value.ValueExists)
            {
                subKey.SetValue(ValueName, pair.Value.Value ?? 0, RegistryValueKind.DWord);
            }
            else
            {
                subKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            var verify = ReadInterfaceValue(subKey);
            var matched = pair.Value.ValueExists
                ? verify.ValueExists && verify.Value == pair.Value.Value
                : !verify.ValueExists;
            if (!matched)
            {
                failed.Add(pair.Key + "(验证失败)");
                continue;
            }

            restored++;
        }

        if (failed.Count > 0)
        {
            return (false, restored, "NetBIOS 部分接口恢复失败: " + string.Join(", ", failed.Take(5)) + "等");
        }

        return (true, restored, "已恢复 NetBIOS");
    }
}

[SupportedOSPlatform("windows")]
public sealed class DisableLmhostsLookupOperation : IOptimizationOperation
{
    private const string NetbtPath = @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters";

    private static readonly RegistryToggleOperation DelegateOperation = new(
        id: "network.disable_lmhosts",
        title: "禁用 LMHOSTS 查询",
        description: "禁用 LMHOSTS 文件查询，减少名称解析延迟",
        category: "网络优化",
        hive: RegistryHive.LocalMachine,
        subKeyPath: NetbtPath,
        valueName: "EnableLMHOSTS",
        desiredValue: 0,
        valueKind: RegistryValueKind.DWord);

    public string Id => "network.disable_lmhosts";
    public string Title => "禁用 LMHOSTS 查询";
    public string Description => "禁用 LMHOSTS 文件查询，减少名称解析延迟";
    public string Category => "网络优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "减少名称解析延迟";
    public string? Drawback { get; init; } = "依赖 LMHOSTS 的名称解析将失效";
    public OperationType OperationType => OperationType.Registry;
    public string RegistryHiveText => "HKLM";
    public string RegistryPath => NetbtPath;
    public string RegistryValueName => "EnableLMHOSTS";
    public bool RequiresAdmin => true;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        using var key = Registry.LocalMachine.OpenSubKey(NetbtPath);
        var value = key?.GetValue("EnableLMHOSTS") as int? ?? 1;
        var disabled = value == 0;
        return Task.FromResult(new OperationStatus(disabled, disabled ? "LMHOSTS 已禁用" : "LMHOSTS 已启用"));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已禁用 LMHOSTS 查询", "LMHOSTS 查询已禁用");
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await DelegateOperation.RestoreAsync(context, cancellationToken).ConfigureAwait(false);
        return DelegatedOperationResultMapper.WithMessages(result, "已恢复 LMHOSTS 查询", "LMHOSTS 查询已恢复");
    }
}
