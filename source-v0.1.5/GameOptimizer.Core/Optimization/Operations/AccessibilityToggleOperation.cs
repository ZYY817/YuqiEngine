using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GameOptimizer.Core.Optimization.Operations;

internal enum AccessibilitySetting
{
    StickyKeys,
    FilterKeys,
    ToggleKeys
}

internal sealed record AccessibilityParameters(
    uint Flags,
    uint WaitMilliseconds = 0,
    uint DelayMilliseconds = 0,
    uint RepeatMilliseconds = 0,
    uint BounceMilliseconds = 0);

internal readonly record struct AccessibilityParametersReadResult(
    bool Success,
    AccessibilityParameters? Parameters,
    int Win32Error)
{
    public static AccessibilityParametersReadResult Succeeded(AccessibilityParameters parameters) =>
        new(true, parameters, 0);

    public static AccessibilityParametersReadResult Failed(int win32Error) => new(false, null, win32Error);
}

internal readonly record struct AccessibilityFlagsWriteResult(bool Success, int Win32Error)
{
    public static AccessibilityFlagsWriteResult Succeeded() => new(true, 0);

    public static AccessibilityFlagsWriteResult Failed(int win32Error) => new(false, win32Error);
}

internal interface IAccessibilitySystemParametersPort
{
    AccessibilityParametersReadResult ReadParameters(AccessibilitySetting setting);

    AccessibilityFlagsWriteResult WriteParameters(AccessibilitySetting setting, AccessibilityParameters parameters);
}

[SupportedOSPlatform("windows")]
internal sealed class NativeAccessibilitySystemParametersPort : IAccessibilitySystemParametersPort
{
    private const uint SpiGetStickyKeys = 0x003A;
    private const uint SpiSetStickyKeys = 0x003B;
    private const uint SpiGetFilterKeys = 0x0032;
    private const uint SpiSetFilterKeys = 0x0033;
    private const uint SpiGetToggleKeys = 0x0034;
    private const uint SpiSetToggleKeys = 0x0035;
    private const uint PersistAndBroadcast = 0x01 | 0x02;

    public static NativeAccessibilitySystemParametersPort Instance { get; } = new();

    private NativeAccessibilitySystemParametersPort()
    {
    }

    internal static int GetNativeStructureSize(AccessibilitySetting setting) => setting switch
    {
        AccessibilitySetting.StickyKeys => Marshal.SizeOf<StickyKeysData>(),
        AccessibilitySetting.FilterKeys => Marshal.SizeOf<FilterKeysData>(),
        AccessibilitySetting.ToggleKeys => Marshal.SizeOf<ToggleKeysData>(),
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null)
    };

    public AccessibilityParametersReadResult ReadParameters(AccessibilitySetting setting) => setting switch
    {
        AccessibilitySetting.StickyKeys => ReadStickyKeys(),
        AccessibilitySetting.FilterKeys => ReadFilterKeys(),
        AccessibilitySetting.ToggleKeys => ReadToggleKeys(),
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null)
    };

    public AccessibilityFlagsWriteResult WriteParameters(
        AccessibilitySetting setting,
        AccessibilityParameters parameters) => setting switch
    {
        AccessibilitySetting.StickyKeys => WriteStickyKeys(parameters),
        AccessibilitySetting.FilterKeys => WriteFilterKeys(parameters),
        AccessibilitySetting.ToggleKeys => WriteToggleKeys(parameters),
        _ => throw new ArgumentOutOfRangeException(nameof(setting), setting, null)
    };

    private static AccessibilityParametersReadResult ReadStickyKeys()
    {
        var data = new StickyKeysData { Size = (uint)Marshal.SizeOf<StickyKeysData>() };
        var success = SystemParametersInfo(SpiGetStickyKeys, data.Size, ref data, 0);
        return success
            ? AccessibilityParametersReadResult.Succeeded(new AccessibilityParameters(data.Flags))
            : AccessibilityParametersReadResult.Failed(Marshal.GetLastWin32Error());
    }

    private static AccessibilityParametersReadResult ReadFilterKeys()
    {
        var data = new FilterKeysData { Size = (uint)Marshal.SizeOf<FilterKeysData>() };
        var success = SystemParametersInfo(SpiGetFilterKeys, data.Size, ref data, 0);
        return success
            ? AccessibilityParametersReadResult.Succeeded(new AccessibilityParameters(
                data.Flags,
                data.WaitMilliseconds,
                data.DelayMilliseconds,
                data.RepeatMilliseconds,
                data.BounceMilliseconds))
            : AccessibilityParametersReadResult.Failed(Marshal.GetLastWin32Error());
    }

    private static AccessibilityParametersReadResult ReadToggleKeys()
    {
        var data = new ToggleKeysData { Size = (uint)Marshal.SizeOf<ToggleKeysData>() };
        var success = SystemParametersInfo(SpiGetToggleKeys, data.Size, ref data, 0);
        return success
            ? AccessibilityParametersReadResult.Succeeded(new AccessibilityParameters(data.Flags))
            : AccessibilityParametersReadResult.Failed(Marshal.GetLastWin32Error());
    }

    private static AccessibilityFlagsWriteResult WriteStickyKeys(AccessibilityParameters parameters)
    {
        var data = new StickyKeysData
        {
            Size = (uint)Marshal.SizeOf<StickyKeysData>(),
            Flags = parameters.Flags
        };
        var success = SystemParametersInfo(SpiSetStickyKeys, data.Size, ref data, PersistAndBroadcast);
        return success
            ? AccessibilityFlagsWriteResult.Succeeded()
            : AccessibilityFlagsWriteResult.Failed(Marshal.GetLastWin32Error());
    }

    private static AccessibilityFlagsWriteResult WriteFilterKeys(AccessibilityParameters parameters)
    {
        var data = new FilterKeysData
        {
            Size = (uint)Marshal.SizeOf<FilterKeysData>(),
            Flags = parameters.Flags,
            WaitMilliseconds = parameters.WaitMilliseconds,
            DelayMilliseconds = parameters.DelayMilliseconds,
            RepeatMilliseconds = parameters.RepeatMilliseconds,
            BounceMilliseconds = parameters.BounceMilliseconds
        };
        var success = SystemParametersInfo(SpiSetFilterKeys, data.Size, ref data, PersistAndBroadcast);
        return success
            ? AccessibilityFlagsWriteResult.Succeeded()
            : AccessibilityFlagsWriteResult.Failed(Marshal.GetLastWin32Error());
    }

    private static AccessibilityFlagsWriteResult WriteToggleKeys(AccessibilityParameters parameters)
    {
        var data = new ToggleKeysData
        {
            Size = (uint)Marshal.SizeOf<ToggleKeysData>(),
            Flags = parameters.Flags
        };
        var success = SystemParametersInfo(SpiSetToggleKeys, data.Size, ref data, PersistAndBroadcast);
        return success
            ? AccessibilityFlagsWriteResult.Succeeded()
            : AccessibilityFlagsWriteResult.Failed(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref StickyKeysData data, uint options);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref FilterKeysData data, uint options);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref ToggleKeysData data, uint options);

    [StructLayout(LayoutKind.Sequential)]
    private struct StickyKeysData
    {
        public uint Size;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterKeysData
    {
        public uint Size;
        public uint Flags;
        public uint WaitMilliseconds;
        public uint DelayMilliseconds;
        public uint RepeatMilliseconds;
        public uint BounceMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ToggleKeysData
    {
        public uint Size;
        public uint Flags;
    }
}

internal static class AccessibilityToggleOperationHelper
{
    public static async Task<OperationInvokeResult> ApplyWithSnapshotAsync<TSnapshot>(
        OperationExecutionContext context,
        string operationId,
        TSnapshot snapshot,
        Func<OperationInvokeResult> applyTarget,
        Func<bool> tryRollback,
        CancellationToken cancellationToken)
    {
        await context.SaveSnapshotAsync(operationId, snapshot, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            await context.TryDeleteSnapshotAsync(operationId, CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var result = applyTarget();
        if (result.Success)
        {
            return result;
        }

        if (tryRollback())
        {
            await context.TryDeleteSnapshotAsync(operationId, CancellationToken.None).ConfigureAwait(false);
            return new OperationInvokeResult(false, false, string.Concat(result.Message, " 已自动回滚为原始设置。"));
        }

        return new OperationInvokeResult(false, false, string.Concat(result.Message, " 自动回滚失败，请手动恢复。"));
    }
}

internal sealed record AccessibilityParametersSnapshot(
    uint Flags,
    uint? WaitMilliseconds = null,
    uint? DelayMilliseconds = null,
    uint? RepeatMilliseconds = null,
    uint? BounceMilliseconds = null)
{
    public static AccessibilityParametersSnapshot FromParameters(AccessibilityParameters parameters) =>
        new(
            parameters.Flags,
            parameters.WaitMilliseconds,
            parameters.DelayMilliseconds,
            parameters.RepeatMilliseconds,
            parameters.BounceMilliseconds);

    public bool TryGetParameters(AccessibilitySetting setting, out AccessibilityParameters? parameters)
    {
        if (setting == AccessibilitySetting.FilterKeys &&
            (WaitMilliseconds is null ||
             DelayMilliseconds is null ||
             RepeatMilliseconds is null ||
             BounceMilliseconds is null))
        {
            parameters = null;
            return false;
        }

        parameters = new AccessibilityParameters(
            Flags,
            WaitMilliseconds ?? 0,
            DelayMilliseconds ?? 0,
            RepeatMilliseconds ?? 0,
            BounceMilliseconds ?? 0);
        return true;
    }
}

internal sealed class AccessibilityHotkeyDisableOperationCore
{
    private readonly IAccessibilitySystemParametersPort _platform;
    private readonly AccessibilitySetting _setting;
    private readonly uint _targetClearMask;
    private readonly uint _complianceMask;
    private readonly string _operationId;
    private readonly string _displayName;

    public AccessibilityHotkeyDisableOperationCore(
        IAccessibilitySystemParametersPort platform,
        AccessibilitySetting setting,
        uint targetClearMask,
        uint complianceMask,
        string operationId,
        string displayName)
    {
        _platform = platform;
        _setting = setting;
        _targetClearMask = targetClearMask;
        _complianceMask = complianceMask;
        _operationId = operationId;
        _displayName = displayName;
    }

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = ReadParameters();
        if (!current.Success || current.Parameters is null)
        {
            return Task.FromResult(new OperationStatus(false, FormatReadFailure(current.Win32Error)));
        }

        var disabled = (current.Parameters.Flags & _complianceMask) == 0;
        return Task.FromResult(new OperationStatus(
            disabled,
            disabled ? $"{_displayName}热键已禁用。" : $"{_displayName}热键已启用。"));
    }

    public Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = ReadParameters();
        if (!current.Success || current.Parameters is null)
        {
            return Task.FromResult(new OperationInvokeResult(false, false, FormatReadFailure(current.Win32Error)));
        }

        var targetParameters = current.Parameters with
        {
            Flags = current.Parameters.Flags & ~_targetClearMask
        };
        if (current.Parameters == targetParameters)
        {
            return Task.FromResult(new OperationInvokeResult(true, false, $"{_displayName}热键已经处于禁用状态。"));
        }

        return AccessibilityToggleOperationHelper.ApplyWithSnapshotAsync(
            context,
            _operationId,
            AccessibilityParametersSnapshot.FromParameters(current.Parameters),
            () => SetAndVerify(targetParameters, $"已禁用{_displayName}热键。", $"设置{_displayName}热键失败。"),
            () => TryRestoreParameters(current.Parameters),
            cancellationToken);
    }

    public async Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        var snapshot = await context.LoadSnapshotAsync<AccessibilityParametersSnapshot>(
            _operationId,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return new OperationInvokeResult(false, false, $"{_displayName}恢复快照缺失或损坏。");
        }

        if (!snapshot.TryGetParameters(_setting, out var snapshotParameters) || snapshotParameters is null)
        {
            return new OperationInvokeResult(
                false,
                false,
                $"{_displayName}恢复快照不完整，无法安全恢复全部参数。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var current = ReadParameters();
        if (!current.Success || current.Parameters is null)
        {
            return new OperationInvokeResult(false, false, FormatReadFailure(current.Win32Error));
        }

        if (current.Parameters == snapshotParameters)
        {
            return new OperationInvokeResult(true, false, $"{_displayName}设置已经处于快照状态。");
        }

        return SetAndVerify(snapshotParameters, $"已恢复{_displayName}设置。", $"恢复{_displayName}设置失败。");
    }

    private AccessibilityParametersReadResult ReadParameters()
    {
        try
        {
            return _platform.ReadParameters(_setting);
        }
        catch (Exception exception)
        {
            return AccessibilityParametersReadResult.Failed(exception.HResult);
        }
    }

    private OperationInvokeResult SetAndVerify(
        AccessibilityParameters parameters,
        string successMessage,
        string failureMessage)
    {
        AccessibilityFlagsWriteResult write;
        try
        {
            write = _platform.WriteParameters(_setting, parameters);
        }
        catch (Exception exception)
        {
            write = AccessibilityFlagsWriteResult.Failed(exception.HResult);
        }

        if (!write.Success)
        {
            return new OperationInvokeResult(false, false, string.Concat(failureMessage, " ", FormatWriteFailure(write.Win32Error)));
        }

        var verified = ReadParameters();
        if (!verified.Success || verified.Parameters is null)
        {
            return new OperationInvokeResult(false, false, string.Concat($"{_displayName}写入后校验失败。 ", FormatReadFailure(verified.Win32Error)));
        }

        return verified.Parameters == parameters
            ? new OperationInvokeResult(true, true, successMessage)
            : new OperationInvokeResult(false, false, $"{_displayName}写入后校验失败：回读值与目标值不一致。");
    }

    private bool TryRestoreParameters(AccessibilityParameters originalParameters)
    {
        var current = ReadParameters();
        if (current.Success && current.Parameters == originalParameters)
        {
            return true;
        }

        return SetAndVerify(originalParameters, string.Empty, string.Empty).Success;
    }

    private string FormatReadFailure(int win32Error) =>
        win32Error == 0
            ? $"读取{_displayName}状态失败（accessibility.spi_get_failed_unknown，Win32=0），已安全阻断。"
            : $"读取{_displayName}状态失败（accessibility.spi_get_failed，Win32={win32Error}），已安全阻断。";

    private string FormatWriteFailure(int win32Error) =>
        win32Error == 0
            ? $"写入{_displayName}状态失败（accessibility.spi_set_failed_unknown，Win32=0）。"
            : $"写入{_displayName}状态失败（accessibility.spi_set_failed，Win32={win32Error}）。";
}

[SupportedOSPlatform("windows")]
public sealed class StickyKeysDisableOperation : IOptimizationOperation
{
    private const uint HotkeyActive = 0x04;
    private const uint ConfirmHotkey = 0x08;
    private readonly AccessibilityHotkeyDisableOperationCore _core;

    public StickyKeysDisableOperation()
        : this(NativeAccessibilitySystemParametersPort.Instance)
    {
    }

    internal StickyKeysDisableOperation(IAccessibilitySystemParametersPort platform)
    {
        _core = new AccessibilityHotkeyDisableOperationCore(
            platform,
            AccessibilitySetting.StickyKeys,
            HotkeyActive | ConfirmHotkey,
            HotkeyActive,
            Id,
            "粘滞键");
    }

    public string Id => "accessibility.disable_sticky_keys";
    public string Title => "禁用粘滞键";
    public string Description => "禁用粘滞键热键，避免连按 Shift 时弹出系统提示。";
    public string Category => "辅助功能";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "避免游戏中因连按 Shift 触发系统提示。";
    public string? Drawback { get; init; } = "依赖粘滞键辅助功能的用户将无法通过热键启用它。";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _core.GetStatusAsync(cancellationToken);

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _core.ApplyAsync(context, cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _core.RestoreAsync(context, cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class FilterKeysDisableOperation : IOptimizationOperation
{
    private const uint HotkeyActive = 0x04;
    private readonly AccessibilityHotkeyDisableOperationCore _core;

    public FilterKeysDisableOperation()
        : this(NativeAccessibilitySystemParametersPort.Instance)
    {
    }

    internal FilterKeysDisableOperation(IAccessibilitySystemParametersPort platform)
    {
        _core = new AccessibilityHotkeyDisableOperationCore(
            platform,
            AccessibilitySetting.FilterKeys,
            HotkeyActive,
            HotkeyActive,
            Id,
            "筛选键");
    }

    public string Id => "accessibility.disable_filter_keys";
    public string Title => "禁用筛选键";
    public string Description => "禁用筛选键热键，避免长按右 Shift 时弹出系统提示。";
    public string Category => "辅助功能";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "避免长按右 Shift 触发系统提示。";
    public string? Drawback { get; init; } = "依赖筛选键辅助功能的用户将无法通过热键启用它。";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _core.GetStatusAsync(cancellationToken);

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _core.ApplyAsync(context, cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _core.RestoreAsync(context, cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class ToggleKeysDisableOperation : IOptimizationOperation
{
    private const uint HotkeyActive = 0x04;
    private readonly AccessibilityHotkeyDisableOperationCore _core;

    public ToggleKeysDisableOperation()
        : this(NativeAccessibilitySystemParametersPort.Instance)
    {
    }

    internal ToggleKeysDisableOperation(IAccessibilitySystemParametersPort platform)
    {
        _core = new AccessibilityHotkeyDisableOperationCore(
            platform,
            AccessibilitySetting.ToggleKeys,
            HotkeyActive,
            HotkeyActive,
            Id,
            "切换键");
    }

    public string Id => "accessibility.disable_toggle_keys";
    public string Title => "禁用切换键";
    public string Description => "禁用切换键热键，避免按 Num Lock 等键时出现提示音。";
    public string Category => "辅助功能";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit { get; init; } = "避免按 Num Lock 等键时出现提示音。";
    public string? Drawback { get; init; } = "依赖切换键辅助功能的用户将无法通过热键启用它。";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        _core.GetStatusAsync(cancellationToken);

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _core.ApplyAsync(context, cancellationToken);

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken) =>
        _core.RestoreAsync(context, cancellationToken);
}
