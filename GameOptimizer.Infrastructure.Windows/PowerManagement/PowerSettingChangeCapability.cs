// File: GameOptimizer.Infrastructure.Windows/PowerManagement/PowerSettingChangeCapability.cs
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

/// <summary>
/// One AC/DC power-setting value pair that a capability owns.  A capability may own several
/// targets under a single resource key (e.g. Core Parking min+max) so the group is applied and
/// restored as one atomic contract instead of several racing writers.
/// </summary>
public sealed record PowerSettingTarget(
    string SettingId,
    Guid SubgroupGuid,
    Guid SettingGuid,
    int TargetAc,
    int? TargetDc);

public sealed record PowerSettingChangeCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string ResourceKey,
    ChangeRisk Risk,
    IReadOnlyList<PowerSettingTarget> Targets,
    bool RequiresExplicitConfirmation = true,
    bool RequiresRestart = false);

public sealed record PowerSettingValueState(
    Guid SubgroupGuid,
    Guid SettingGuid,
    int? AcValue,
    int? DcValue);

public sealed record PowerSettingSnapshot(
    Guid SchemeGuid,
    IReadOnlyList<PowerSettingValueState> Settings);

/// <summary>
/// Read-only platform facts used to gate a power-setting capability before it can be planned.
/// The default provider mirrors <c>WindowsRegistryDwordCapabilityEnvironmentProvider</c>;
/// tests inject deterministic facts without touching the host token or PowrProf.
/// </summary>
public sealed record PowerSettingEnvironment(
    bool IsWindows,
    int WindowsBuild,
    string MachineName,
    bool IsAdministrator);

public interface IPowerSettingEnvironmentProvider
{
    PowerSettingEnvironment GetCurrent();
}

public sealed class WindowsPowerSettingEnvironmentProvider : IPowerSettingEnvironmentProvider
{
    public PowerSettingEnvironment GetCurrent() => new(
        OperatingSystem.IsWindows(),
        Environment.OSVersion.Version.Build,
        Environment.MachineName,
        IsAdministrator());

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;
}

/// <summary>
/// Platform boundary for reading/writing AC/DC power-setting indexes on the ACTIVE scheme.
/// The production implementation talks to PowrProf directly; it never runs <c>powercfg</c>,
/// never activates a scheme, and never touches the machine-global active-scheme resource.
/// </summary>
public interface IPowerSettingAccessor
{
    Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken);
    Task<PowerSettingValueState> ReadValueAsync(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        CancellationToken cancellationToken);
    Task WriteValueAsync(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        int? acValue,
        int? dcValue,
        CancellationToken cancellationToken);
}

/// <summary>
/// Safety gate abstraction.  The production adapter delegates to the existing real
/// <see cref="PowerSettingCapabilityGuard"/> (WMI/powercfg based); tests MUST inject a fake.
/// </summary>
public interface IPowerSettingCapabilityGuard
{
    Task<PowerSettingGuardResult> EvaluateAsync(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        int? targetAc,
        int? targetDc,
        string? operationId = null,
        string? label = null,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsPowerSettingCapabilityGuard : IPowerSettingCapabilityGuard
{
    private readonly PowerSettingCapabilityGuard _inner = new();

    public Task<PowerSettingGuardResult> EvaluateAsync(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        int? targetAc,
        int? targetDc,
        string? operationId = null,
        string? label = null,
        CancellationToken cancellationToken = default) =>
        _inner.EvaluateAsync(scheme, subgroup, setting, targetAc, targetDc, operationId, label, cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPowerSettingAccessor : IPowerSettingAccessor
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorFileNotFound = 2;

    public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadActiveScheme());
    }

    public Task<PowerSettingValueState> ReadValueAsync(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PowerSettingValueState(
            subgroup,
            setting,
            ReadValue(scheme, subgroup, setting, ac: true),
            ReadValue(scheme, subgroup, setting, ac: false)));
    }

    public Task WriteValueAsync(
        Guid scheme,
        Guid subgroup,
        Guid setting,
        int? acValue,
        int? dcValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A null channel is "value not observable / not present" and is left untouched: we
        // never fabricate a default for a value we could not read at baseline.
        if (acValue.HasValue)
        {
            ThrowIfError(PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, checked((uint)acValue.Value)), "PowerWriteACValueIndex");
        }

        if (dcValue.HasValue)
        {
            ThrowIfError(PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, checked((uint)dcValue.Value)), "PowerWriteDCValueIndex");
        }

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

    private static int? ReadValue(Guid scheme, Guid subgroup, Guid setting, bool ac)
    {
        uint value;
        var result = ac
            ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
            : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
        if (result == ErrorSuccess)
        {
            return checked((int)value);
        }

        if (result == ErrorFileNotFound)
        {
            return null;
        }

        ThrowIfError(result, ac ? "PowerReadACValueIndex" : "PowerReadDCValueIndex");
        return null;
    }

    private static void ThrowIfError(uint result, string operation)
    {
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(unchecked((int)result), $"{operation} failed.");
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint dcValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, uint acValueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, uint dcValueIndex);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

/// <summary>
/// Typed change capability for AC/DC power-setting writes against the ACTIVE scheme.
/// It deliberately uses a distinct <c>power:setting:*</c> resource-key scheme and never issues
/// SETACTIVE, so it does not contend with the machine-global
/// <c>power:machine:display-timeout-and-active-scheme</c> resource owned by PowerManagementOwner.
/// </summary>
public sealed class PowerSettingChangeCapability : IChangeCapability
{
    private readonly PowerSettingChangeCapabilityDefinition _definition;
    private readonly IPowerSettingAccessor _accessor;
    private readonly IPowerSettingCapabilityGuard _guard;
    private readonly IPowerSettingEnvironmentProvider _environmentProvider;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<PowerSettingTarget> _targets;

    public PowerSettingChangeCapability(
        PowerSettingChangeCapabilityDefinition definition,
        IPowerSettingAccessor accessor,
        IPowerSettingCapabilityGuard guard,
        TimeProvider? timeProvider = null,
        IPowerSettingEnvironmentProvider? environmentProvider = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _environmentProvider = environmentProvider ?? new WindowsPowerSettingEnvironmentProvider();
        ValidateDefinition(definition);
        _targets = Array.AsReadOnly(definition.Targets.ToArray());
        _definition = definition with { Targets = _targets };

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
        var environment = _environmentProvider.GetCurrent();
        if (!environment.IsWindows || environment.WindowsBuild < 19041)
        {
            return new CapabilityAssessment(
                CapabilityApplicability.Unsupported,
                false,
                "os.unsupported",
                "该电源设置能力要求 Windows 10 2004 或更高版本。");
        }

        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "scope.invalid",
                "该电源设置能力只能应用到当前计算机作用域。");
        }

        if (!environment.IsAdministrator)
        {
            return new CapabilityAssessment(
                CapabilityApplicability.Applicable,
                false,
                "permission.administrator-required",
                "写入电源设置需要已提升的管理员令牌。");
        }

        Guid scheme;
        try
        {
            scheme = await _accessor.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CapabilityAssessment(
                CapabilityApplicability.Unsupported,
                true,
                "power.scheme-unavailable",
                $"无法读取活动电源方案：{ex.Message}");
        }

        foreach (var target in _targets)
        {
            PowerSettingGuardResult result;
            try
            {
                result = await _guard.EvaluateAsync(
                    scheme,
                    target.SubgroupGuid,
                    target.SettingGuid,
                    target.TargetAc,
                    target.TargetDc,
                    _definition.CapabilityId,
                    _definition.CapabilityId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new CapabilityAssessment(
                    CapabilityApplicability.Unsupported,
                    true,
                    "guard.failed",
                    $"电源设置安全门禁评估失败：{ex.Message}");
            }

            if (result.Decision == PowerSettingGuardDecision.SkippedUnsupported)
            {
                return new CapabilityAssessment(
                    CapabilityApplicability.Unsupported,
                    true,
                    "guard.unsupported",
                    result.Reason);
            }

            if (result.Decision == PowerSettingGuardDecision.BlockedRisk)
            {
                return new CapabilityAssessment(
                    CapabilityApplicability.Conflict,
                    true,
                    "guard.blocked-risk",
                    result.Reason);
            }
        }

        return new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            true,
            "applicable",
            "活动电源方案的这些设置可以安全写入。");
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        ValidateScope(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var scheme = await _accessor.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var settings = new List<PowerSettingValueState>(_targets.Count);
        foreach (var target in _targets)
        {
            settings.Add(await _accessor.ReadValueAsync(
                scheme,
                target.SubgroupGuid,
                target.SettingGuid,
                cancellationToken).ConfigureAwait(false));
        }

        var payload = JsonSerializer.Serialize(new PowerSettingSnapshot(scheme, settings));
        return new StateObservation(
            scope,
            payload,
            Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            "Windows PowrProf active-scheme read",
            "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = DeserializeSnapshot(current.Payload);
        ValidateObservedSnapshot(before);
        var canonicalTarget = string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase)
            ? BuildTargetPayload(before)
            : targetPayload;
        var target = DeserializeSnapshot(canonicalTarget);
        ValidateTargetSnapshot(target);
        if (target.SchemeGuid != before.SchemeGuid)
        {
            throw new InvalidDataException("冻结目标电源方案与当前活动方案不一致。");
        }

        var canonical = JsonSerializer.Serialize(target);
        return Task.FromResult(new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            canonical,
            Fingerprint(canonical),
            Descriptor.ResourceKey));
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        ValidateFrozenChange(change);
        var before = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(false, false, "state.drift", "电源设置在写入前已变化，拒绝执行旧计划。");
        }

        var beforeSnapshot = DeserializeSnapshot(before.Payload);
        var target = DeserializeSnapshot(change.TargetPayload);
        if (target.SchemeGuid != beforeSnapshot.SchemeGuid)
        {
            return new PlatformExecutionResult(false, false, "scheme.drift", "活动电源方案在冻结后发生变化，拒绝写入。");
        }

        var environment = _environmentProvider.GetCurrent();
        if (!environment.IsWindows || environment.WindowsBuild < 19041 || !environment.IsAdministrator)
            return new PlatformExecutionResult(false, false, "environment.changed", "执行前平台或管理员权限不再满足要求，拒绝写入。");

        foreach (var state in target.Settings)
        {
            var guard = await _guard.EvaluateAsync(beforeSnapshot.SchemeGuid, state.SubgroupGuid, state.SettingGuid,
                state.AcValue, state.DcValue, _definition.CapabilityId, _definition.CapabilityId,
                cancellationToken).ConfigureAwait(false);
            if (!guard.IsAllowed)
                return new PlatformExecutionResult(false, false, "guard.recheck-blocked", guard.Reason);
        }

        var written = new List<PowerSettingValueState>();
        try
        {
            foreach (var state in target.Settings)
            {
                // Track the attempted target before the platform call: an accessor may fail
                // after changing AC but before changing DC.
                written.Add(state);
                await _accessor.WriteValueAsync(
                    beforeSnapshot.SchemeGuid,
                    state.SubgroupGuid,
                    state.SettingGuid,
                    state.AcValue,
                    state.DcValue,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception writeError)
        {
            return await CompensateAsync(beforeSnapshot, written, writeError.Message).ConfigureAwait(false);
        }

        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal))
            return new PlatformExecutionResult(true, true, "execution.verified", "电源设置已写入活动方案并回读确认。");

        return await CompensateAsync(beforeSnapshot, written, "写后回读与冻结目标不一致。").ConfigureAwait(false);
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("恢复材料与电源设置能力身份不一致。");
        }

        ValidateScope(material.Scope);
        var original = DeserializeSnapshot(material.BeforePayload);
        ValidateObservedSnapshot(original);
        foreach (var state in original.Settings)
        {
            await _accessor.WriteValueAsync(
                original.SchemeGuid,
                state.SubgroupGuid,
                state.SettingGuid,
                state.AcValue,
                state.DcValue,
                cancellationToken).ConfigureAwait(false);
        }

        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        return string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
            ? new PlatformExecutionResult(true, true, "restore.verified", "原电源设置值已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的电源设置值与原快照不一致。");
    }

    private void ValidateFrozenChange(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("冻结计划与电源设置能力定义不一致。");
        }

        if (!string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("冻结电源设置目标与其指纹不一致。");
        }
    }

    private void ValidateObservedSnapshot(PowerSettingSnapshot snapshot)
    {
        if (snapshot.SchemeGuid == Guid.Empty)
        {
            throw new InvalidDataException("观察到的活动电源方案为空。");
        }

        if (snapshot.Settings.Count != _targets.Count)
        {
            throw new InvalidDataException("观察到的电源设置数量与能力定义不一致。");
        }

        for (var i = 0; i < _targets.Count; i++)
        {
            var state = snapshot.Settings[i];
            var target = _targets[i];
            if (state.SubgroupGuid != target.SubgroupGuid || state.SettingGuid != target.SettingGuid)
            {
                throw new InvalidDataException("观察到的电源设置 GUID 与能力定义不一致。");
            }
        }
    }

    private void ValidateTargetSnapshot(PowerSettingSnapshot snapshot)
    {
        if (snapshot.SchemeGuid == Guid.Empty)
        {
            throw new InvalidDataException("目标电源方案为空。");
        }

        if (snapshot.Settings.Count != _targets.Count)
        {
            throw new InvalidDataException("目标电源设置数量与能力定义不一致。");
        }

        for (var i = 0; i < _targets.Count; i++)
        {
            var state = snapshot.Settings[i];
            var target = _targets[i];
            if (state.SubgroupGuid != target.SubgroupGuid || state.SettingGuid != target.SettingGuid)
            {
                throw new InvalidDataException("目标电源设置 GUID 与能力定义不一致。");
            }

            if (state.AcValue is not >= 0 || state.DcValue is not >= 0)
            {
                throw new InvalidDataException("目标电源设置必须提供非负的 AC/DC 值。");
            }
        }
    }

    private string BuildTargetPayload(PowerSettingSnapshot before)
    {
        var settings = _targets.Select((target, index) => new PowerSettingValueState(
                target.SubgroupGuid,
                target.SettingGuid,
                target.TargetAc,
                target.TargetDc ?? before.Settings[index].DcValue))
            .ToArray();
        return JsonSerializer.Serialize(new PowerSettingSnapshot(before.SchemeGuid, settings));
    }

    private async Task<PlatformExecutionResult> CompensateAsync(
        PowerSettingSnapshot before,
        IReadOnlyCollection<PowerSettingValueState> written,
        string failure)
    {
        try
        {
            foreach (var original in before.Settings.Where(item => written.Any(done =>
                         done.SubgroupGuid == item.SubgroupGuid && done.SettingGuid == item.SettingGuid)).Reverse())
            {
                await _accessor.WriteValueAsync(before.SchemeGuid, original.SubgroupGuid, original.SettingGuid,
                    original.AcValue, original.DcValue, CancellationToken.None).ConfigureAwait(false);
            }

            var scope = TargetScope.Machine(_environmentProvider.GetCurrent().MachineName);
            var restored = await ReadAsync(scope, CancellationToken.None).ConfigureAwait(false);
            var expected = JsonSerializer.Serialize(before);
            if (string.Equals(restored.Fingerprint, Fingerprint(expected), StringComparison.Ordinal))
                return new PlatformExecutionResult(false, written.Count > 0, "execution.compensated", $"{failure} 已自动恢复并回读确认。");

            return new PlatformExecutionResult(false, true, "compensation.mismatch", $"{failure} 自动恢复后的回读不一致。");
        }
        catch (Exception compensationError)
        {
            return new PlatformExecutionResult(false, written.Count > 0, "compensation.failed",
                $"{failure} 自动恢复失败：{compensationError.Message}");
        }
    }

    private static PowerSettingSnapshot DeserializeSnapshot(string payload) =>
        JsonSerializer.Deserialize<PowerSettingSnapshot>(payload)
        ?? throw new InvalidDataException("电源设置快照无法解析。");

    private void ValidateScope(TargetScope scope)
    {
        var machineName = _environmentProvider.GetCurrent().MachineName;
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, machineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("电源设置能力只能使用当前计算机作用域。");
        }
    }

    private static void ValidateDefinition(PowerSettingChangeCapabilityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ResourceKey);
        if (definition.Targets is null || definition.Targets.Count == 0)
        {
            throw new ArgumentException("电源设置能力必须至少声明一个目标设置。", nameof(definition));
        }

        foreach (var target in definition.Targets)
        {
            if (string.IsNullOrWhiteSpace(target.SettingId))
            {
                throw new ArgumentException("目标设置缺少 SettingId。", nameof(definition));
            }

            if (target.SubgroupGuid == Guid.Empty || target.SettingGuid == Guid.Empty)
            {
                throw new ArgumentException("目标设置包含空 GUID。", nameof(definition));
            }

            if (target.TargetAc < 0 || target.TargetDc is < 0)
            {
                throw new ArgumentException("目标设置的 AC/DC 值必须非负。", nameof(definition));
            }
        }
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
