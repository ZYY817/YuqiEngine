using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using GameOptimizer.Core.PowerManagement;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

public sealed class WindowsPowerPlatform : IPowerPlatform
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint ErrorNoMoreItems = 259;
    private const uint AccessScheme = 16;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private readonly IPowerCfgProcessRunner _runner;

    public WindowsPowerPlatform(IPowerCfgProcessRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Task<PowerEnvironment> GetEnvironmentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return Task.FromResult(new PowerEnvironment(false, 0, false, false, false, false, "Windows is required."));

        var build = Environment.OSVersion.Version.Build;
        var administrator = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        if (!GetPwrCapabilities(out var capabilities))
            return Task.FromResult(new PowerEnvironment(true, build, administrator, false, false, true, "Windows power capabilities could not be read; write is blocked."));
        var hasBattery = capabilities.SystemBatteriesPresent != 0;
        var policyManaged = IsDisplayTimeoutPolicyManaged();
        var modernStandby = capabilities.AoAc != 0;
        return Task.FromResult(new PowerEnvironment(true, build, administrator, hasBattery, policyManaged, modernStandby,
            policyManaged ? "Display timeout is managed under HKLM policy." : modernStandby ? "Modern Standby power semantics are not supported by this owner." : null));
    }

    public Task<IReadOnlyList<PowerScheme>> EnumerateSchemesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = ReadActiveScheme();
        var schemes = new List<PowerScheme>();
        for (uint index = 0; ; index++)
        {
            uint size = 16;
            var scheme = Guid.Empty;
            var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, ref scheme, ref size);
            if (result == ErrorNoMoreItems) break;
            ThrowIfError(result, "PowerEnumerate");
            schemes.Add(new PowerScheme(scheme, ReadFriendlyName(scheme), scheme == active));
        }
        if (schemes.Count == 0) throw new InvalidOperationException("No power schemes were returned by PowrProf.");
        return Task.FromResult<IReadOnlyList<PowerScheme>>(schemes);
    }

    public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadActiveScheme());
    }

    public Task<PowerSettingCapability> GetDisplayTimeoutCapabilityAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _ = ReadValue(schemeId, true);
            return Task.FromResult(new PowerSettingCapability(true, IsDisplayTimeoutPolicyManaged(), 0, 86_400,
                IsDisplayTimeoutPolicyManaged() ? "Display timeout is policy managed." : null));
        }
        catch (Win32Exception ex)
        {
            return Task.FromResult(new PowerSettingCapability(false, false, 0, 0, ex.Message));
        }
    }

    public Task<PowerSettingValues> ReadDisplayTimeoutAsync(Guid schemeId, bool includeDc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ac = ReadValue(schemeId, true);
        uint? dc = includeDc ? ReadValue(schemeId, false) : null;
        return Task.FromResult(new PowerSettingValues(ac, dc));
    }

    public async Task<PowerMutationResult> ApplyDisplayTimeoutAsync(
        Guid schemeId,
        PowerSettingValues values,
        Guid schemeToActivate,
        CancellationToken cancellationToken)
    {
        var commands = new List<PowerCommandResult>();
        var ac = await RunAsync("/SETACVALUEINDEX", schemeId, values.AcSeconds, cancellationToken).ConfigureAwait(false);
        commands.Add(ac);
        if (ac.Outcome != PowerCommandOutcome.Success) return new PowerMutationResult(ac.Outcome, commands, ac.Detail);
        if (values.DcSeconds.HasValue)
        {
            var dc = await RunAsync("/SETDCVALUEINDEX", schemeId, values.DcSeconds.Value, cancellationToken).ConfigureAwait(false);
            commands.Add(dc);
            if (dc.Outcome != PowerCommandOutcome.Success) return new PowerMutationResult(dc.Outcome, commands, dc.Detail);
        }
        var activate = await _runner.RunAsync(new[] { "/SETACTIVE", schemeToActivate.ToString("D") }, CommandTimeout, cancellationToken).ConfigureAwait(false);
        commands.Add(activate);
        return new PowerMutationResult(activate.Outcome, commands, activate.Detail);
    }

    private Task<PowerCommandResult> RunAsync(string verb, Guid schemeId, uint seconds, CancellationToken cancellationToken) =>
        _runner.RunAsync(new[]
        {
            verb,
            schemeId.ToString("D"),
            PowerManagementGuids.DisplaySubgroup.ToString("D"),
            PowerManagementGuids.DisplayIdleTimeout.ToString("D"),
            seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }, CommandTimeout, cancellationToken);

    private static Guid ReadActiveScheme()
    {
        ThrowIfError(PowerGetActiveScheme(IntPtr.Zero, out var pointer), "PowerGetActiveScheme");
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { _ = LocalFree(pointer); }
    }

    private static uint ReadValue(Guid scheme, bool ac)
    {
        var subgroup = PowerManagementGuids.DisplaySubgroup;
        var setting = PowerManagementGuids.DisplayIdleTimeout;
        uint value;
        var result = ac
            ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
            : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
        ThrowIfError(result, ac ? "PowerReadACValueIndex" : "PowerReadDCValueIndex");
        return value;
    }

    private static string ReadFriendlyName(Guid scheme)
    {
        uint size = 0;
        var first = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (first is not (ErrorSuccess or ErrorMoreData) || size == 0) return scheme.ToString("D");
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            ThrowIfError(PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size), "PowerReadFriendlyName");
            return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') ?? scheme.ToString("D");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool IsDisplayTimeoutPolicyManaged()
    {
        var path = $@"SOFTWARE\Policies\Microsoft\Power\PowerSettings\{PowerManagementGuids.DisplaySubgroup:D}\{PowerManagementGuids.DisplayIdleTimeout:D}";
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(path, writable: false);
        return key?.GetValue("ACSettingIndex") is not null || key?.GetValue("DCSettingIndex") is not null;
    }

    private static void ThrowIfError(uint result, string operation)
    {
        if (result != ErrorSuccess) throw new Win32Exception(unchecked((int)result), $"{operation} failed.");
    }

    // SYSTEM_POWER_CAPABILITIES from WinNT.h (Windows 10/11 ABI, 76 bytes).
    [StructLayout(LayoutKind.Explicit, Size = 76)]
    private struct SystemPowerCapabilities
    {
        [FieldOffset(20)] public byte AoAc;
        [FieldOffset(30)] public byte SystemBatteriesPresent;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);
    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subgroupGuid, uint accessFlags, uint index, ref Guid buffer, ref uint bufferSize);
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subgroupGuid, IntPtr settingGuid, IntPtr buffer, ref uint bufferSize);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint acValueIndex);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint dcValueIndex);
    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPwrCapabilities(out SystemPowerCapabilities systemPowerCapabilities);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
