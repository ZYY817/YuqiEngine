using System.Security.Principal;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public sealed class RegistryValueSetCapability : IChangeCapability
{
    private static readonly TimeSpan DefaultSafetyCompensationTimeout = TimeSpan.FromSeconds(10);
    private readonly RegistryValueSetCapabilityDefinition _definition;
    private readonly RegistryTypedTargetValue[] _targets;
    private readonly string[] _valueNames;
    private readonly string _targetPayload;
    private readonly string _targetFingerprint;
    private readonly RegistryValueSetPayloadCodec _codec;
    private readonly IRegistryValueSetAccessor _registry;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _safetyCompensationTimeout;

    public RegistryValueSetCapability(
        RegistryValueSetCapabilityDefinition definition,
        IRegistryValueSetAccessor registry,
        TimeProvider? timeProvider = null,
        TimeSpan? safetyCompensationTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.SubKeyPath);
        _codec = new RegistryValueSetPayloadCodec(definition.Targets, definition.Constraints);
        _targets = _codec.Targets.ToArray();
        _definition = definition with
        {
            Targets = Array.AsReadOnly(_targets),
            Constraints = Array.AsReadOnly(_codec.Constraints.ToArray())
        };
        _registry = registry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _safetyCompensationTimeout = safetyCompensationTimeout ?? DefaultSafetyCompensationTimeout;
        if (_safetyCompensationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(safetyCompensationTimeout));
        }

        _valueNames = _codec.ValueNames.ToArray();
        _targetPayload = _codec.DefaultTargetPayload;
        _targetFingerprint = _codec.DefaultTargetFingerprint;
        var isMachine = _definition.Hive == RegistryDwordHive.LocalMachine;
        Descriptor = new CapabilityDescriptor(
            _definition.CapabilityId,
            _definition.DefinitionVersion,
            _definition.Risk,
            [isMachine ? TargetScopeKind.Machine : TargetScopeKind.CurrentUser],
            RequiresAdministrator: isMachine,
            _definition.RequiresExplicitConfirmation,
            RequiresRecoveryMaterial: true,
            _definition.RequiresRestart,
            ResourceKey: $"registry:{(isMachine ? "HKLM" : "HKCU")}/{_definition.SubKeyPath}");
    }

    public CapabilityDescriptor Descriptor { get; }

    public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.Unsupported,
                false,
                "os.unsupported",
                "该功能要求 Windows 10 2004 或更高版本。"));
        }

        if (_definition.Hive == RegistryDwordHive.LocalMachine)
        {
            if (scope.Kind != TargetScopeKind.Machine
                || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CapabilityAssessment(
                    CapabilityApplicability.NotApplicable,
                    false,
                    "scope.invalid",
                    "该注册表能力只能应用到当前计算机作用域。"));
            }

            var isAdministrator = IsAdministrator();
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.Applicable,
                isAdministrator,
                isAdministrator ? "applicable" : "permission.administrator-required",
                isAdministrator ? "本机注册表作用域可用。" : "修改 HKLM 需要已提升的管理员令牌。"));
        }

        var validScope = IsCurrentUserScope(scope);
        return Task.FromResult(new CapabilityAssessment(
            validScope ? CapabilityApplicability.Applicable : CapabilityApplicability.NotApplicable,
            validScope,
            validScope ? "applicable" : "scope.invalid",
            validScope ? "当前用户注册表作用域可用。" : "该能力只能应用到当前进程对应的用户 SID。"));
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        EnsureScope(scope);
        var observed = _definition.Hive == RegistryDwordHive.LocalMachine
            ? await _registry.ReadLocalMachineAsync(
                _definition.SubKeyPath,
                _valueNames,
                cancellationToken).ConfigureAwait(false)
            : await _registry.ReadCurrentUserAsync(
                _definition.SubKeyPath,
                _valueNames,
                cancellationToken).ConfigureAwait(false);
        var payload = _codec.NormalizeAndValidatePayload(new RegistryValueSetPayload(observed));
        return new StateObservation(
            scope,
            payload,
            RegistryValueSetPayloadCodec.Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            _definition.Hive == RegistryDwordHive.LocalMachine
                ? "HKLM typed registry value-set read"
                : "HKCU typed registry value-set read",
            "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureScope(current.Scope);
        var currentPayload = _codec.ParseCanonicalPayload(current.Payload);
        _codec.EnsureObservedKindsMatchDefinition(currentPayload);
        var frozenTarget = string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase)
            ? (_targetPayload, _targetFingerprint)
            : _codec.CreateFrozenTarget(targetPayload);
        return Task.FromResult(new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            frozenTarget.Item1,
            frozenTarget.Item2,
            Descriptor.ResourceKey));
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        EnsureScope(change.Scope);
        var frozenTargets = _codec.ValidateFrozenTarget(change.TargetPayload, change.TargetFingerprint);
        var before = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(false, false, "state.drift", "写入前任一注册表值已变化，拒绝执行旧计划。");
        }

        if (string.Equals(before.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(true, false, "execution.no-change", "全部注册表值已经处于目标状态。");
        }

        var originals = _codec.ParseCanonicalPayload(before.Payload).Values
            .ToDictionary(value => value.ValueName, StringComparer.OrdinalIgnoreCase);
        var attempted = new List<string>(_targets.Length);
        try
        {
            foreach (var target in frozenTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempted.Add(target.ValueName);
                if (target.Action == RegistryTargetAction.Delete)
                {
                    await RestoreValueAsync(
                        _definition.SubKeyPath,
                        new RegistryValueSetEntryState(target.ValueName, true, false, null, null, null),
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteValueAsync(
                        _definition.SubKeyPath,
                        target,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
            if (string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal))
            {
                return new PlatformExecutionResult(true, true, "execution.verified", "同键多值已逐项写入并整体回读确认。");
            }

            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                originals,
                attempted,
                before.Fingerprint,
                "execution.verification-mismatch",
                "写入后整体回读与目标不一致").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                originals,
                attempted,
                before.Fingerprint,
                "execution.cancelled",
                "逐值写入期间取消").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                originals,
                attempted,
                before.Fingerprint,
                "execution.permission-denied",
                "逐值写入权限不足").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                originals,
                attempted,
                before.Fingerprint,
                "execution.failed",
                $"逐值写入异常：{ex.GetType().Name}").ConfigureAwait(false);
        }
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        EnsureScope(material.Scope);
        if (!string.Equals(RegistryValueSetPayloadCodec.Fingerprint(material.BeforePayload), material.BeforeFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("恢复材料中的原状态 payload 与 fingerprint 不一致。");
        }

        var originals = _codec.ParseCanonicalPayload(material.BeforePayload).Values
            .ToDictionary(value => value.ValueName, StringComparer.OrdinalIgnoreCase);
        _codec.EnsureObservedKindsMatchDefinition(new RegistryValueSetPayload(originals.Values.ToArray()));
        var current = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        if (string.Equals(current.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(true, false, "restore.no-change", "当前状态已经等于恢复前状态。");
        }

        if (!string.Equals(current.Fingerprint, material.TargetFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(false, false, "restore.state-drift", "恢复前任一注册表值已偏离执行目标，拒绝覆盖外部变更。");
        }

        var targetStates = _codec.ParseCanonicalPayload(current.Payload).Values
            .ToDictionary(value => value.ValueName, StringComparer.OrdinalIgnoreCase);
        var attempted = new List<string>(_targets.Length);
        try
        {
            foreach (var target in _targets.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempted.Add(target.ValueName);
                await RestoreValueAsync(
                    _definition.SubKeyPath,
                    originals[target.ValueName],
                    cancellationToken).ConfigureAwait(false);
            }

            var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
            if (string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal))
            {
                return new PlatformExecutionResult(true, true, "restore.verified", "同键多值已逆序恢复并整体回读确认。");
            }

            return await BuildRestoreFailureAfterCompensationAsync(
                material.Scope,
                targetStates,
                attempted,
                material.TargetFingerprint,
                "restore.verification-mismatch",
                "恢复后整体回读与原状态不一致").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await BuildRestoreFailureAfterCompensationAsync(
                material.Scope,
                targetStates,
                attempted,
                material.TargetFingerprint,
                "restore.cancelled",
                "逐值恢复期间取消").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return await BuildRestoreFailureAfterCompensationAsync(
                material.Scope,
                targetStates,
                attempted,
                material.TargetFingerprint,
                "restore.permission-denied",
                "逐值恢复权限不足").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await BuildRestoreFailureAfterCompensationAsync(
                material.Scope,
                targetStates,
                attempted,
                material.TargetFingerprint,
                "restore.failed",
                $"逐值恢复异常：{ex.GetType().Name}").ConfigureAwait(false);
        }
    }

    public static TargetScope CreateCurrentUserScope() => TargetScope.CurrentUser(GetCurrentUserIdentity());

    public static TargetScope CreateMachineScope() => TargetScope.Machine(Environment.MachineName);

    private Task WriteValueAsync(
        string subKeyPath,
        RegistryTypedTargetValue target,
        CancellationToken cancellationToken) =>
        _definition.Hive == RegistryDwordHive.LocalMachine
            ? _registry.WriteLocalMachineValueAsync(subKeyPath, target, cancellationToken)
            : _registry.WriteCurrentUserValueAsync(subKeyPath, target, cancellationToken);

    private Task RestoreValueAsync(
        string subKeyPath,
        RegistryValueSetEntryState original,
        CancellationToken cancellationToken) =>
        _definition.Hive == RegistryDwordHive.LocalMachine
            ? _registry.RestoreLocalMachineValueAsync(subKeyPath, original, cancellationToken)
            : _registry.RestoreCurrentUserValueAsync(subKeyPath, original, cancellationToken);

    private void EnsureScope(TargetScope scope)
    {
        if (_definition.Hive == RegistryDwordHive.LocalMachine)
        {
            if (scope.Kind != TargetScopeKind.Machine
                || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("目标作用域不是当前计算机，已阻断 HKLM 同键多值注册表能力。");
            }

            return;
        }

        if (!IsCurrentUserScope(scope))
        {
            throw new InvalidOperationException("目标作用域不是当前用户 SID，已阻断同键多值注册表能力。");
        }
    }

    private static bool IsCurrentUserScope(TargetScope scope) =>
        scope.Kind == TargetScopeKind.CurrentUser
        && string.Equals(scope.Identity, GetCurrentUserIdentity(), StringComparison.OrdinalIgnoreCase);

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentUserIdentity()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
        {
            throw new InvalidOperationException("无法确定当前 Windows 用户 SID，已阻断注册表能力。");
        }

        return sid;
    }

    private async Task<PlatformExecutionResult> BuildExecuteFailureAfterCompensationAsync(
        TargetScope scope,
        IReadOnlyDictionary<string, RegistryValueSetEntryState> originals,
        IReadOnlyList<string> attempted,
        string expectedFingerprint,
        string reasonCode,
        string message)
    {
        var compensated = await RestoreEntriesAndVerifyAsync(
            scope,
            attempted.Reverse(),
            originals,
            expectedFingerprint).ConfigureAwait(false);
        return compensated
            ? new PlatformExecutionResult(false, false, $"{reasonCode}.compensated", $"{message}；已逆序补偿并确认原状态。")
            : new PlatformExecutionResult(false, true, $"{reasonCode}.compensation-failed", $"{message}；补偿或补偿回读失败，需要恢复核查。");
    }

    private async Task<PlatformExecutionResult> BuildRestoreFailureAfterCompensationAsync(
        TargetScope scope,
        IReadOnlyDictionary<string, RegistryValueSetEntryState> targets,
        IReadOnlyList<string> attempted,
        string expectedFingerprint,
        string reasonCode,
        string message)
    {
        var compensated = await WriteEntriesAndVerifyAsync(
            scope,
            attempted.Reverse(),
            targets,
            expectedFingerprint).ConfigureAwait(false);
        return compensated
            ? new PlatformExecutionResult(false, false, $"{reasonCode}.compensated", $"{message}；已逆序补偿并确认恢复前目标状态。")
            : new PlatformExecutionResult(false, true, $"{reasonCode}.compensation-failed", $"{message}；补偿或补偿回读失败，需要人工恢复。");
    }

    private async Task<bool> RestoreEntriesAndVerifyAsync(
        TargetScope scope,
        IEnumerable<string> valueNames,
        IReadOnlyDictionary<string, RegistryValueSetEntryState> states,
        string expectedFingerprint)
    {
        using var safetySource = new CancellationTokenSource(_safetyCompensationTimeout);
        var allWritesSucceeded = true;
        foreach (var valueName in valueNames)
        {
            try
            {
                await RestoreValueAsync(
                    _definition.SubKeyPath,
                    states[valueName],
                    safetySource.Token).ConfigureAwait(false);
            }
            catch
            {
                allWritesSucceeded = false;
            }
        }

        return allWritesSucceeded
            && !safetySource.IsCancellationRequested
            && await HasFingerprintAsync(scope, expectedFingerprint, safetySource.Token).ConfigureAwait(false);
    }

    private async Task<bool> WriteEntriesAndVerifyAsync(
        TargetScope scope,
        IEnumerable<string> valueNames,
        IReadOnlyDictionary<string, RegistryValueSetEntryState> states,
        string expectedFingerprint)
    {
        using var safetySource = new CancellationTokenSource(_safetyCompensationTimeout);
        var allWritesSucceeded = true;
        foreach (var valueName in valueNames)
        {
            try
            {
                var state = states[valueName];
                if (state.ValueExists)
                {
                    await WriteValueAsync(
                        _definition.SubKeyPath,
                        RegistryValueSetPayloadCodec.ToTargetValue(state),
                        safetySource.Token).ConfigureAwait(false);
                }
                else
                {
                    await RestoreValueAsync(
                        _definition.SubKeyPath,
                        state,
                        safetySource.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                allWritesSucceeded = false;
            }
        }

        return allWritesSucceeded
            && !safetySource.IsCancellationRequested
            && await HasFingerprintAsync(scope, expectedFingerprint, safetySource.Token).ConfigureAwait(false);
    }

    private async Task<bool> HasFingerprintAsync(
        TargetScope scope,
        string expectedFingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            var observation = await ReadAsync(scope, cancellationToken).ConfigureAwait(false);
            return string.Equals(observation.Fingerprint, expectedFingerprint, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
