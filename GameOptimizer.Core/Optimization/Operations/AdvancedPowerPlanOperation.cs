using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// 高级电源计划 GUID 常量
/// </summary>
public static class AdvancedPowerGuids
{
    // 子组 GUID
    public static readonly Guid ProcessorSubgroup = new("54533251-82BE-4824-96C1-47B60B740D00");
    public static readonly Guid PciExpressSubgroup = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    public static readonly Guid UsbSubgroup = new("2A737441-1930-4402-8D77-B2BEBBA308A3");
    
    // 处理器性能设置
    public static readonly Guid ProcessorMinState = new("893DEE8E-2BEF-41E0-89C6-B55D0929964C");
    public static readonly Guid ProcessorMaxState = new("BC5038F7-23E0-4960-96DA-33ABAF5935EC");
    public static readonly Guid ProcessorBoostMode = new("BE337238-0D82-4146-A960-4F3749D470C7");
    
    // 处理器性能提升阈值 - 触发睿频的 CPU 使用率阈值
    public static readonly Guid ProcessorPerfIncreaseThreshold = new("06cadf0e-64ed-448a-8927-ce7bf90eb35d");
    
    // 处理器闲置降级阈值 - 防止 CPU 降频
    public static readonly Guid ProcessorIdleDemoteThreshold = new("4b92d758-5a24-4851-a470-815d78aee119");
    
    // 处理器性能时间检查间隔 - 频率切换检查间隔
    public static readonly Guid ProcessorPerfTimeCheck = new("4d2b0152-7d5c-498b-88e2-34345392a2c5");
    
    // 允许节流状态 - 禁止 CPU 节流
    public static readonly Guid ProcessorThrottlePolicy = new("3b04d4fd-1cc7-4f23-ab1c-d1337819c4bb");
    
    // 异类线程调度策略 - Intel 大小核调度
    public static readonly Guid HeteroThreadSchedulingPolicy = new("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
    
    // 异类短运行线程调度策略
    public static readonly Guid HeteroShortThreadSchedulingPolicy = new("bae08b81-2d5e-4688-ad6a-13243356654b");
    
    // 第一类处理器电源效率阈值（大核）
    public static readonly Guid ProcessorClass1EfficiencyThreshold = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    
    // 第一类处理器性能级别（大核性能）
    public static readonly Guid ProcessorClass1PerfLevel = new("619b7505-003b-4e82-b7a6-4dd29c300971");
    
    // PCIe Link State Power Management (ASPM)
    public static readonly Guid PcieLinkStatePowerManagement = new("ee12f906-d166-11d0-87cf-00a0c9110001");
    
    // USB 选择性暂停
    public static readonly Guid UsbSelectiveSuspend = new("48E6B7A6-50F5-4782-A5D4-53BB8F07E226");
}

/// <summary>
/// 电源设置快照
/// </summary>
public sealed class PowerSettingSnapshot
{
    public Guid? Scheme { get; init; }
    public int? Ac { get; init; }
    public int? Dc { get; init; }
}

/// <summary>
/// 电源操作基类 - 消除重复代码，集成诊断功能
/// </summary>
[SupportedOSPlatform("windows")]
public abstract class BasePowerPlanOperation : IOptimizationOperation
{
    private static PowerOperationDiagnostics? _diagnostics;
    private static ILogger? _logger;

    public static void SetDiagnostics(PowerOperationDiagnostics diagnostics, ILogger logger)
    {
        _diagnostics = diagnostics;
        _logger = logger;
    }

    protected abstract Guid SubgroupGuid { get; }
    protected abstract Guid SettingGuid { get; }
    protected abstract int TargetValue { get; }
    protected abstract int DefaultValue { get; }

    public abstract string Id { get; }
    public abstract string Title { get; }
    public abstract string Description { get; }
    public virtual string Category => "电源高级优化";
    public virtual OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public virtual string? Benefit => null;
    public virtual string? Drawback => null;
    public virtual bool RequiresAdmin => true;
    public virtual bool RequiresSnapshotForRestore => true;
    public virtual OperationType OperationType => OperationType.PowerPlan;

    public async Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        var (scheme, ac, dc) = await ReadCurrentValuesAsync(ct).ConfigureAwait(false);
        if (scheme == null)
        {
            return new OperationStatus(false, "无法读取");
        }

        var compliant = IsCompliant(ac, dc);
        return new OperationStatus(compliant, GetStatusText(ac, dc));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken ct)
    {
        try
        {
            var result = await CreateDelegate().ApplyAsync(context, ct).ConfigureAwait(false);
            if (result.Success && result.Changed)
            {
                _logger?.LogInformation("[{OperationId}] 应用成功: {Title}", Id, Title);
                return new OperationInvokeResult(true, true, GetApplySuccessText());
            }

            if (!result.Success)
            {
                _logger?.LogWarning("[{OperationId}] 应用失败: {Message}", Id, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[{OperationId}] 应用异常: {Title}", Id, Title);
            return new OperationInvokeResult(false, false, $"{Title} 应用失败: {ex.Message}");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken ct)
    {
        try
        {
            var result = await CreateDelegate().RestoreAsync(context, ct).ConfigureAwait(false);
            if (result.Success && result.Changed)
            {
                _logger?.LogInformation("[{OperationId}] 恢复成功: {Title}", Id, Title);
                return new OperationInvokeResult(true, true, "已恢复");
            }

            if (!result.Success)
            {
                _logger?.LogWarning("[{OperationId}] 恢复失败: {Message}", Id, result.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[{OperationId}] 恢复异常: {Title}", Id, Title);
            return new OperationInvokeResult(false, false, $"{Title} 恢复失败: {ex.Message}");
        }
    }

    protected virtual bool IsCompliant(int? ac, int? dc)
    {
        if (ac == null && dc == null)
        {
            return false;
        }

        if (ac == null)
        {
            return dc == TargetValue;
        }

        if (dc == null)
        {
            return ac == TargetValue;
        }

        if (SubgroupGuid == AdvancedPowerGuids.ProcessorSubgroup)
        {
            return ac == TargetValue && dc == TargetValue;
        }

        return ac == TargetValue || dc == TargetValue;
    }

    protected virtual string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? $"{TargetValue}" : $"AC={ac}/DC={dc}";
    protected virtual string GetApplySuccessText() => $"已设置 {TargetValue}";

    protected virtual PowerSettingIndexOperation CreateDelegate()
    {
        return new PowerSettingIndexOperation(
            Id,
            Title,
            Description,
            Category,
            SubgroupGuid,
            SettingGuid,
            TargetValue,
            TargetValue)
        {
            RiskLevel = RiskLevel,
            Benefit = Benefit,
            Drawback = Drawback
        };
    }

    protected async Task<(Guid? Scheme, int? Ac, int? Dc)> ReadCurrentValuesAsync(CancellationToken ct)
    {
        var scheme = await PowerCfgUtility.GetActiveSchemeWithFallbackAsync(ct).ConfigureAwait(false);
        if (scheme == null)
        {
            return (null, null, null);
        }

        var (ac, dc) = await PowerCfgUtility.QuerySettingValueAsync(scheme.Value, SubgroupGuid, SettingGuid, ct).ConfigureAwait(false);
        if (ac is null && dc is null)
        {
            Debug.WriteLine($"[AdvancedPowerPlan] {Id}: powercfg /QUERY 返回 null，尝试从注册表读取");
            var path = $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{scheme.Value:D}\{SubgroupGuid:D}\{SettingGuid:D}".ToUpperInvariant();
            using var key = Registry.LocalMachine.OpenSubKey(path);
            ac = ReadIndex(key, "ACSettingIndex");
            dc = ReadIndex(key, "DCSettingIndex");
        }

        return (scheme, ac, dc);
    }

    protected static int? ReadIndex(RegistryKey? key, string name)
    {
        var value = key?.GetValue(name);
        return value switch
        {
            int i => i,
            byte[] b when b.Length >= 4 => BitConverter.ToInt32(b, 0),
            _ => null
        };
    }
}

/// <summary>
/// 异类线程调度策略枚举
/// </summary>
public enum HeteroThreadPolicy
{
    AllProcessors = 0,           // 所有处理器（AMD 推荐）
    HighPerformance = 1,         // 高性能处理器（Intel 仅大核）
    PreferHighPerformance = 2,   // 首选高性能处理器（Intel 大小核混合）
    EfficientProcessors = 3,     // 高效处理器（小核优先）
    PreferEfficient = 4,         // 首选高效处理器
    Automatic = 5                // 自动
}

/// <summary>
/// 禁用 USB 选择性暂停 - 防止 USB 设备休眠导致延迟
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisableUsbSelectiveSuspendOperation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.UsbSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.UsbSelectiveSuspend;
    protected override int TargetValue => 0;
    protected override int DefaultValue => 1;

    public override string Id => "power.disable_usb_suspend";
    public override string Title => "禁用 USB 选择性暂停";
    public override string Description => "防止 USB 设备休眠导致鼠标键盘延迟，游戏必备优化。";
    public override OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public override string? Benefit => "可能防止鼠标键盘延迟，外设响应更快";
    public override string? Drawback => "USB 设备不休眠，轻微增加功耗";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "已禁用" : $"AC={ac}/DC={dc}";
    protected override string GetApplySuccessText() => "已禁用 USB 选择性暂停";
}

/// <summary>
/// 禁用 PCIe ASPM - 关闭 PCIe 节能，降低延迟
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisableAspmOperation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.PciExpressSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.PcieLinkStatePowerManagement;
    protected override int TargetValue => 0;
    protected override int DefaultValue => 1;

    public override string Id => "power.disable_aspm";
    public override string Title => "禁用 PCIe 节能 (ASPM)";
    public override string Description => "关闭 PCIe Link State Power Management，降低显卡和 NVMe 延迟。";
    public override string? Benefit => "可能降低显卡和 NVMe 延迟";
    public override string? Drawback => "增加功耗，PCIe 设备不休眠";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "已禁用 (Off)" : $"AC={ac}/DC={dc}";
    protected override string GetApplySuccessText() => "已禁用 ASPM";
}


/// <summary>
/// 处理器最小状态 100% - CPU 始终满频运行
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessorMinState100Operation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.ProcessorSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.ProcessorMinState;
    protected override int TargetValue => 100;
    protected override int DefaultValue => 5;

    public override string Id => "power.processor_min_state_100";
    public override string Title => "处理器最小状态 100%";
    public override string Description => "CPU 始终保持最高频率运行，不降频，游戏性能最大化。";
    public override string? Benefit => "CPU 始终满频，可能提升游戏性能";
    public override string? Drawback => "大幅增加功耗和发热";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "100%" : $"AC={ac}%/DC={dc}%";
    protected override string GetApplySuccessText() => "已设置为 100%";
}

/// <summary>
/// 处理器性能提升阈值 = 1 - 更快触发睿频
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessorPerfIncreaseThresholdOperation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.ProcessorSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.ProcessorPerfIncreaseThreshold;
    protected override int TargetValue => 1;
    protected override int DefaultValue => 10;

    public override string Id => "power.perf_increase_threshold";
    public override string Title => "处理器性能提升阈值";
    public override string Description => "设为 1%，CPU 使用率稍有提升就立即触发睿频，响应更快。";
    public override string? Benefit => "CPU 可能更快触发睿频";
    public override string? Drawback => "频繁切换频率，轻微增加功耗";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "1% (最快响应)" : $"AC={ac}%/DC={dc}%";
    protected override string GetApplySuccessText() => "已设置为 1%";
}

/// <summary>
/// 处理器闲置降级阈值 = 100 - 防止 CPU 降频
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessorIdleDemoteThresholdOperation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.ProcessorSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.ProcessorIdleDemoteThreshold;
    protected override int TargetValue => 100;
    protected override int DefaultValue => 40;

    public override string Id => "power.idle_demote_threshold";
    public override string Title => "处理器闲置降级阈值";
    public override string Description => "设为 100%，防止 CPU 因短暂空闲而降频，保持高性能状态。";
    public override string? Benefit => "可能防止 CPU 降频，保持高性能状态";
    public override string? Drawback => "增加功耗，CPU 不会节能";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "100% (不降频)" : $"AC={ac}%/DC={dc}%";
    protected override string GetApplySuccessText() => "已设置为 100%";
}

/// <summary>
/// 异类线程调度策略 - Intel 大小核调度优化（特殊处理，不使用基类）
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HeteroThreadSchedulingOperation : BasePowerPlanOperation
{
    private readonly HeteroThreadPolicy _targetPolicy;

    protected override Guid SubgroupGuid => AdvancedPowerGuids.ProcessorSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.HeteroThreadSchedulingPolicy;
    protected override int TargetValue => (int)_targetPolicy;
    protected override int DefaultValue => 5;

    public HeteroThreadSchedulingOperation(HeteroThreadPolicy targetPolicy = HeteroThreadPolicy.PreferHighPerformance)
    {
        _targetPolicy = targetPolicy;
    }

    public override string Id => "power.hetero_thread_scheduling";
    public override string Title => "异类线程调度策略";
    public override string Description => "Intel 大小核调度优化：大核优先处理游戏线程，小核处理后台任务。AMD 用户选择「所有处理器」。";
    public override OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public override string? Benefit => "大核优先处理游戏，可能提升单线程性能";
    public override string? Drawback => "可能影响多任务效率，需根据 CPU 类型选择";

    protected override string GetStatusText(int? ac, int? dc) => GetPolicyName(ac ?? 5);
    protected override string GetApplySuccessText() => $"已设置为 {GetPolicyName(TargetValue)}";

    private static string GetPolicyName(int value) => value switch
    {
        0 => "所有处理器 (AMD)",
        1 => "高性能处理器 (Intel 仅大核)",
        2 => "首选高性能处理器 (Intel 大小核)",
        3 => "高效处理器 (小核优先)",
        4 => "首选高效处理器",
        5 => "自动",
        _ => $"未知 ({value})"
    };
}

/// <summary>
/// 处理器性能时间检查间隔 - 设为最大值减少频率切换
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessorPerfTimeCheckOperation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.ProcessorSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.ProcessorPerfTimeCheck;
    protected override int TargetValue => 5000;
    protected override int DefaultValue => 15;

    public override string Id => "power.perf_time_check";
    public override string Title => "处理器性能时间检查间隔";
    public override string Description => "设为最大值，减少 CPU 频率切换次数，保持稳定高频。";
    public override string? Benefit => "减少频率切换，可能提升帧率稳定性";
    public override string? Drawback => "CPU 响应负载变化变慢";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "5000ms (最大)" : $"AC={ac}ms/DC={dc}ms";
    protected override string GetApplySuccessText() => "已设置为 5000ms";
}

/// <summary>
/// 第一类处理器电源效率阈值 = 1 - 大核更快响应
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessorClass1EfficiencyThresholdOperation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.ProcessorSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.ProcessorClass1EfficiencyThreshold;
    protected override int TargetValue => 1;
    protected override int DefaultValue => 10;

    public override string Id => "power.class1_efficiency_threshold";
    public override string Title => "第一类处理器效率阈值";
    public override string Description => "设为 1%，大核（P-Core）更快响应负载变化，提升游戏帧率稳定性。";
    public override string? Benefit => "大核可能更快响应负载变化";
    public override string? Drawback => "大核频繁激活，增加功耗";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "1% (最快响应)" : $"AC={ac}%/DC={dc}%";
    protected override string GetApplySuccessText() => "已设置为 1%";
}

/// <summary>
/// 第一类处理器性能级别 = 100 - 大核始终满性能
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessorClass1PerfLevelOperation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.ProcessorSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.ProcessorClass1PerfLevel;
    protected override int TargetValue => 100;
    protected override int DefaultValue => 100;

    public override string Id => "power.class1_perf_level";
    public override string Title => "第一类处理器性能级别";
    public override string Description => "设为 100%，大核（P-Core）始终保持最高性能，不降频。";
    public override string? Benefit => "大核始终满性能，可能提升游戏帧率";
    public override string? Drawback => "增加功耗和发热";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "100% (满性能)" : $"AC={ac}%/DC={dc}%";
    protected override string GetApplySuccessText() => "已设置为 100%";
}

/// <summary>
/// 禁止 CPU 节流 - 关闭允许节流状态
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisableProcessorThrottleOperation : BasePowerPlanOperation
{
    protected override Guid SubgroupGuid => AdvancedPowerGuids.ProcessorSubgroup;
    protected override Guid SettingGuid => AdvancedPowerGuids.ProcessorThrottlePolicy;
    protected override int TargetValue => 0;
    protected override int DefaultValue => 1;

    public override string Id => "power.disable_throttle";
    public override string Title => "禁止 CPU 节流";
    public override string Description => "关闭允许节流状态，防止 CPU 因温度或功耗限制而降频。";
    public override string? Benefit => "可能防止 CPU 降频，性能更稳定";
    public override string? Drawback => "高温时 CPU 不降频，可能过热";

    protected override string GetStatusText(int? ac, int? dc) => IsCompliant(ac, dc) ? "已禁用节流" : $"AC={ac}/DC={dc}";
    protected override string GetApplySuccessText() => "已禁用 CPU 节流";
}




