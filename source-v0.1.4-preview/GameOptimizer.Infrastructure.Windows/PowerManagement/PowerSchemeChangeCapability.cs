// File: GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSchemeChangeCapability.cs
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

public sealed record PowerSchemeChangeCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string ResourceKey,
    Guid TargetSchemeGuid,
    ChangeRisk Risk = ChangeRisk.MediumRebootOrExperience,
    bool RequiresExplicitConfirmation = true,
    bool RequiresRestart = false);

public sealed record PowerSchemeState(Guid ActiveSchemeGuid);

public sealed record PowerPlatformFacts(
    bool IsWindows,
    int WindowsBuild,
    string MachineName,
    bool IsAdministrator,
    bool HasBattery,
    bool IsOnAcPower,
    bool IsModernStandby,
    bool IsPolicyRestricted,
    bool IsOemRestricted,
    int ProcessorGroupCount,
    bool IsDetectionReliable);

public sealed record PowerSchemeCatalogEntry(Guid Id, string Name);

public interface IPowerPlatformFactsProvider
{
    PowerPlatformFacts GetCurrent();
}

public interface IPowerSchemeCatalog
{
    Task<IReadOnlyList<PowerSchemeCatalogEntry>> EnumerateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Read-only platform facts used to gate the scheme-switch capability.  Tests inject
/// deterministic facts without touching the host token or PowrProf.
/// </summary>
public sealed record PowerSchemeEnvironment(bool IsWindows, int WindowsBuild, string MachineName, bool IsAdministrator);

public interface IPowerSchemeEnvironmentProvider
{
    PowerSchemeEnvironment GetCurrent();
}

public sealed class WindowsPowerSchemeEnvironmentProvider : IPowerSchemeEnvironmentProvider, IPowerPlatformFactsProvider
{
    public PowerSchemeEnvironment GetCurrent() => new(
        OperatingSystem.IsWindows(),
        Environment.OSVersion.Version.Build,
        Environment.MachineName,
        IsAdministrator());

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;

    PowerPlatformFacts IPowerPlatformFactsProvider.GetCurrent()
    {
        var basic = GetCurrent();
        if (!basic.IsWindows || !GetPwrCapabilities(out var capabilities) || !GetSystemPowerStatus(out var status))
        {
            return new(basic.IsWindows, basic.WindowsBuild, basic.MachineName, basic.IsAdministrator,
                false, false, false, true, true, 0, false);
        }

        var groups = GetActiveProcessorGroupCount();
        var sourceKnown = status.ACLineStatus is 0 or 1;
        var policyResult = PowerSettingAccessCheck(19, IntPtr.Zero); // ACCESS_ACTIVE_SCHEME
        var policyKnown = policyResult is 0 or 1260;
        return new(basic.IsWindows, basic.WindowsBuild, basic.MachineName, basic.IsAdministrator,
            capabilities.SystemBatteriesPresent != 0,
            status.ACLineStatus == 1,
            capabilities.AoAc != 0,
            policyResult != 0,
            false,
            groups,
            sourceKnown && policyKnown && groups > 0);
    }

    [StructLayout(LayoutKind.Explicit, Size = 76)]
    private struct SystemPowerCapabilities
    {
        [FieldOffset(20)] public byte AoAc;
        [FieldOffset(30)] public byte SystemBatteriesPresent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPwrCapabilities(out SystemPowerCapabilities capabilities);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    [DllImport("kernel32.dll")]
    private static extern ushort GetActiveProcessorGroupCount();
    [DllImport("powrprof.dll")]
    private static extern uint PowerSettingAccessCheck(uint accessFlags, IntPtr powerGuid);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPowerSchemeCatalog : IPowerSchemeCatalog
{
    private const uint AccessScheme = 16;
    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint ErrorNoMoreItems = 259;

    public Task<IReadOnlyList<PowerSchemeCatalogEntry>> EnumerateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<PowerSchemeCatalogEntry>();
        for (uint index = 0; ; index++)
        {
            var id = Guid.Empty;
            uint size = 16;
            var error = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, ref id, ref size);
            if (error == ErrorNoMoreItems) break;
            if (error != ErrorSuccess) throw new System.ComponentModel.Win32Exception(unchecked((int)error), "PowerEnumerate failed.");
            result.Add(new(id, ReadFriendlyName(id)));
        }
        return Task.FromResult<IReadOnlyList<PowerSchemeCatalogEntry>>(result);
    }

    private static string ReadFriendlyName(Guid scheme)
    {
        uint size = 0;
        var first = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
        if (first is not (ErrorSuccess or ErrorMoreData) || size == 0) return scheme.ToString("D");
        var buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            var error = PowerReadFriendlyName(IntPtr.Zero, ref scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size);
            if (error != ErrorSuccess) throw new System.ComponentModel.Win32Exception(unchecked((int)error), "PowerReadFriendlyName failed.");
            return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') ?? scheme.ToString("D");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint flags, uint index, ref Guid buffer, ref uint size);
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, IntPtr buffer, ref uint size);
}

/// <summary>
/// Platform boundary for switching the ACTIVE power scheme.  It only ever switches to the
/// preflight-qualified built-in high-performance scheme and back to the original scheme read at baseline.
/// </summary>
public interface IPowerSchemeAccessor
{
    Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken);
    Task SetActiveSchemeAsync(Guid scheme, CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPowerSchemeAccessor : IPowerSchemeAccessor
{
    private const uint ErrorSuccess = 0;

    public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadActiveScheme());
    }

    public Task SetActiveSchemeAsync(Guid scheme, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (scheme == Guid.Empty)
        {
            throw new ArgumentException("目标电源方案为空。", nameof(scheme));
        }

        ThrowIfError(PowerSetActiveScheme(IntPtr.Zero, ref scheme), "PowerSetActiveScheme");
        return Task.CompletedTask;
    }

    private static Guid ReadActiveScheme()
    {
        ThrowIfError(PowerGetActiveScheme(IntPtr.Zero, out var pointer), "PowerGetActiveScheme");
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            _ = LocalFree(pointer);
        }
    }

    private static void ThrowIfError(uint result, string operation)
    {
        if (result != ErrorSuccess)
        {
            throw new System.ComponentModel.Win32Exception(unchecked((int)result), $"{operation} failed.");
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid activePolicyGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

/// <summary>
/// Typed change capability for switching the ACTIVE power scheme to the built-in High
/// Performance plan (<c>8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c</c>) and back to the exact scheme
/// observed at baseline. It shares the editor owner's machine-global active-scheme resource and
/// never mutates individual CPU setting values.
/// </summary>
public sealed class PowerSchemeChangeCapability : IChangeCapability
{
    private readonly PowerSchemeChangeCapabilityDefinition _definition;
    private readonly IPowerSchemeAccessor _accessor;
    private readonly IPowerPlatformFactsProvider _factsProvider;
    private readonly IPowerSchemeCatalog _catalog;
    private readonly TimeProvider _timeProvider;

    public PowerSchemeChangeCapability(
        PowerSchemeChangeCapabilityDefinition definition,
        IPowerSchemeAccessor accessor,
        TimeProvider? timeProvider = null,
        IPowerSchemeEnvironmentProvider? environmentProvider = null,
        IPowerPlatformFactsProvider? factsProvider = null,
        IPowerSchemeCatalog? catalog = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _timeProvider = timeProvider ?? TimeProvider.System;
        var windowsProvider = new WindowsPowerSchemeEnvironmentProvider();
        _factsProvider = factsProvider ?? environmentProvider as IPowerPlatformFactsProvider
            ?? (environmentProvider is null ? windowsProvider : new LegacyFactsAdapter(environmentProvider));
        _catalog = catalog ?? new WindowsPowerSchemeCatalog();
        ValidateDefinition(definition);
        Descriptor = new CapabilityDescriptor(
            _definition.CapabilityId,
            _definition.DefinitionVersion,
            _definition.Risk,
            [TargetScopeKind.Machine],
            RequiresAdministrator: true,
            _definition.RequiresExplicitConfirmation,
            RequiresRecoveryMaterial: true,
            _definition.RequiresRestart,
            _definition.ResourceKey);
    }

    public CapabilityDescriptor Descriptor { get; }

    public async Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var facts = _factsProvider.GetCurrent();
        if (!facts.IsWindows || facts.WindowsBuild < 19041)
        {
            return new CapabilityAssessment(
                CapabilityApplicability.Unsupported,
                false,
                "os.unsupported",
                "该电源方案切换能力要求 Windows 10 2004 或更高版本。");
        }

        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, facts.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "scope.invalid",
                "该电源方案切换能力只能应用到当前计算机作用域。");
        }

        if (!facts.IsDetectionReliable)
        {
            return new CapabilityAssessment(CapabilityApplicability.Unsupported, false,
                "power.unavailable.detection", "Unavailable：无法可靠检测供电、Modern Standby、策略或处理器组；未应用任何更改。");
        }
        if (facts.IsModernStandby)
        {
            return new CapabilityAssessment(CapabilityApplicability.NotApplicable, false,
                "power.guidance.best-performance", "GuidanceOnly：此设备使用 Modern Standby。请在 Windows 设置 > 系统 > 电源和电池 > 电源模式中选择“最佳性能”；本工具不会创建或强制切换计划，也不会记为已优化。");
        }
        if (facts.IsPolicyRestricted || facts.IsOemRestricted)
        {
            return new CapabilityAssessment(CapabilityApplicability.Unsupported, false,
                "power.unavailable.managed", "Unavailable：电源策略由组织或 OEM 管理；未应用任何更改。");
        }
        if (facts.HasBattery || !facts.IsOnAcPower)
        {
            return new CapabilityAssessment(CapabilityApplicability.NotApplicable, false,
                "power.guidance.ac-required", "GuidanceOnly：笔记本或电池供电场景默认不自动切换计划。接通交流电后可在 Windows 电源设置中选择性能模式；这可能增加温度、风扇噪声并缩短续航。未应用且不会记为已优化。");
        }
        if (facts.ProcessorGroupCount < 1)
        {
            return new CapabilityAssessment(CapabilityApplicability.Unsupported, false,
                "power.unavailable.topology", "Unavailable：处理器组拓扑不可用；未应用任何更改。");
        }
        if (!facts.IsAdministrator)
        {
            return new CapabilityAssessment(
                CapabilityApplicability.Applicable,
                false,
                "permission.administrator-required",
                "切换电源方案需要已提升的管理员令牌。");
        }

        IReadOnlyList<PowerSchemeCatalogEntry> schemes;
        try { schemes = await _catalog.EnumerateAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CapabilityAssessment(CapabilityApplicability.Unsupported, false,
                "power.unavailable.enumeration-failed", "Unavailable：无法可靠枚举 Windows 电源计划；未应用任何更改。");
        }
        if (!schemes.Any(item => item.Id == _definition.TargetSchemeGuid))
        {
            return new CapabilityAssessment(CapabilityApplicability.NotApplicable, false,
                "power.guidance.plan-missing", "GuidanceOnly：系统未公开内置“高性能”计划。可打开控制面板 > 电源选项 > 显示附加计划；本工具不会复制、导入或创建计划，也不会记为已优化。");
        }

        return new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            true,
            "applicable",
            "已确认传统桌面平台、交流供电、无策略限制，且 Windows 实际枚举到内置“高性能”计划。切换可能增加功耗、温度和风扇噪声。");
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var active = await _accessor.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new PowerSchemeState(active));
        return new StateObservation(
            scope,
            payload,
            Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            "Windows PowrProf active-scheme read",
            "Authoritative");
    }

    public async Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eligibility = await ProbeAsync(current.Scope, cancellationToken).ConfigureAwait(false);
        if (!CanApply(eligibility)) throw new InvalidOperationException($"{eligibility.ReasonCode}: {eligibility.Details}");
        var before = DeserializeState(current.Payload);
        if (before.ActiveSchemeGuid == Guid.Empty)
        {
            throw new InvalidDataException("观察到的活动电源方案为空。");
        }

        var targetScheme = string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase)
            ? _definition.TargetSchemeGuid
            : DeserializeState(targetPayload).ActiveSchemeGuid;
        if (targetScheme == Guid.Empty)
        {
            throw new InvalidDataException("目标电源方案为空。");
        }

        var canonical = JsonSerializer.Serialize(new PowerSchemeState(targetScheme));
        return new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            canonical,
            Fingerprint(canonical),
            Descriptor.ResourceKey);
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        ValidateFrozenChange(change);
        var eligibility = await ProbeAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!CanApply(eligibility))
            return new PlatformExecutionResult(false, false, eligibility.ReasonCode, eligibility.Details);
        var before = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(false, false, "state.drift", "活动电源方案在写入前已变化，拒绝执行旧计划。");
        }

        var target = DeserializeState(change.TargetPayload);
        await _accessor.SetActiveSchemeAsync(target.ActiveSchemeGuid, cancellationToken).ConfigureAwait(false);
        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, true, "execution.verified", "Applied：活动电源方案已切换并回读确认；恢复材料已绑定原计划。")
            : new PlatformExecutionResult(false, true, "verification.mismatch", "切换后的活动电源方案与冻结目标不一致。");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("恢复材料与电源方案切换能力身份不一致。");
        }

        ValidateScope(material.Scope);
        var original = DeserializeState(material.BeforePayload);
        if (original.ActiveSchemeGuid == Guid.Empty)
        {
            throw new InvalidDataException("恢复材料中的原活动电源方案为空。");
        }

        await _accessor.SetActiveSchemeAsync(original.ActiveSchemeGuid, cancellationToken).ConfigureAwait(false);
        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, true, "restore.verified", "原活动电源方案已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的活动电源方案与原快照不一致。");
    }

    private void ValidateFrozenChange(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("冻结计划与电源方案切换能力定义不一致。");
        }

        if (!string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("冻结电源方案目标与其指纹不一致。");
        }
    }

    private static PowerSchemeState DeserializeState(string payload) =>
        JsonSerializer.Deserialize<PowerSchemeState>(payload)
        ?? throw new InvalidDataException("电源方案状态无法解析。");

    private void ValidateScope(TargetScope scope)
    {
        var machineName = _factsProvider.GetCurrent().MachineName;
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, machineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("电源方案切换能力只能使用当前计算机作用域。");
        }
    }

    private static void ValidateDefinition(PowerSchemeChangeCapabilityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ResourceKey);
        if (definition.TargetSchemeGuid == Guid.Empty)
        {
            throw new ArgumentException("目标电源方案不能为空。", nameof(definition));
        }
    }

    private static bool CanApply(CapabilityAssessment assessment) =>
        assessment.Applicability == CapabilityApplicability.Applicable && assessment.HasRequiredPermission;

    private sealed class LegacyFactsAdapter(IPowerSchemeEnvironmentProvider provider) : IPowerPlatformFactsProvider
    {
        public PowerPlatformFacts GetCurrent()
        {
            var value = provider.GetCurrent();
            return new(value.IsWindows, value.WindowsBuild, value.MachineName, value.IsAdministrator,
                false, true, false, false, false, 1, true);
        }
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
