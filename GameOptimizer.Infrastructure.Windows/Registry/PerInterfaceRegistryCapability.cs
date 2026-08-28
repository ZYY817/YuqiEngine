using System.Security.Principal;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

/// <summary>
/// Machine-scope capability that enumerates the direct sub-keys of a fixed HKLM root (network
/// interface GUIDs, NetBT interface keys, etc.) and applies a fixed set of typed values to each.
///
/// Safety contract:
///  - Only values under already-existing enumerated sub-keys are written; no keys are created or
///    deleted, so the blast radius is bounded to entries the OS already owns.
///  - The full enumerated value set is fingerprinted before and after. Any drift in the sub-key
///    set or in an observed value between prepare and execute (or between apply and restore)
///    causes a fail-closed refusal instead of a blind overwrite.
///  - Unsupported value kinds are observed read-only and never implicitly converted.
/// </summary>
public sealed class PerInterfaceRegistryCapability : IChangeCapability
{
    private readonly PerInterfaceRegistryCapabilityDefinition _definition;
    private readonly PerInterfaceRegistryCodec _codec;
    private readonly IPerInterfaceRegistryAccessor _registry;
    private readonly TimeProvider _timeProvider;

    public PerInterfaceRegistryCapability(
        PerInterfaceRegistryCapabilityDefinition definition,
        IPerInterfaceRegistryAccessor registry,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.RootPath);
        _codec = new PerInterfaceRegistryCodec(definition.Targets);
        _definition = definition with { Targets = Array.AsReadOnly(_codec.Targets.ToArray()) };
        _registry = registry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = new CapabilityDescriptor(
            _definition.CapabilityId,
            _definition.DefinitionVersion,
            _definition.Risk,
            [TargetScopeKind.Machine],
            RequiresAdministrator: true,
            _definition.RequiresExplicitConfirmation,
            RequiresRecoveryMaterial: true,
            _definition.RequiresRestart,
            ResourceKey: _definition.ResourceKey ?? $"registry:HKLM/{_definition.RootPath}");
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

        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "scope.invalid",
                "该每接口注册表能力只能应用到当前计算机作用域。"));
        }

        var isAdministrator = IsAdministrator();
        return Task.FromResult(new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            isAdministrator,
            isAdministrator ? "applicable" : "permission.administrator-required",
            isAdministrator ? "本机每接口注册表作用域可用。" : "修改 HKLM 需要已提升的管理员令牌。"));
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        EnsureScope(scope);
        var subKeys = await _registry.EnumerateSubKeysAsync(_definition.RootPath, cancellationToken).ConfigureAwait(false);
        var matchingSubKeys = await FilterVendorSubKeysAsync(subKeys, cancellationToken).ConfigureAwait(false);
        var values = new List<PerInterfaceValueState>(matchingSubKeys.Count * _codec.Targets.Count);
        foreach (var subKey in matchingSubKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var target in _codec.Targets)
            {
                values.Add(await _registry.ReadValueAsync(
                    _definition.RootPath,
                    subKey,
                    target.ValueName,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        var payload = _codec.NormalizeAndValidatePayload(new PerInterfaceRegistryPayload(values));
        return new StateObservation(
            scope,
            payload,
            PerInterfaceRegistryCodec.Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            "HKLM per-interface registry read",
            "Authoritative");
    }

    private async Task<IReadOnlyList<string>> FilterVendorSubKeysAsync(
        IReadOnlyList<string> subKeys,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_definition.VendorSelectorValueName)
            || _definition.VendorSelectorContains is null
            || _definition.VendorSelectorContains.Count == 0)
        {
            return subKeys;
        }

        var matches = new List<string>(subKeys.Count);
        foreach (var subKey in subKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await _registry.ReadValueAsync(
                _definition.RootPath,
                subKey,
                _definition.VendorSelectorValueName,
                cancellationToken).ConfigureAwait(false);
            var text = state.StringValue;
            if (text is not null
                && _definition.VendorSelectorContains.Any(
                    needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(subKey);
            }
        }

        return matches;
    }

    public async Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureScope(current.Scope);
        var currentPayload = _codec.ParseCanonicalPayload(current.Payload);
        _codec.EnsureObservedKindsMatchDefinition(currentPayload);

        string frozenPayload;
        if (string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase))
        {
            var subKeys = currentPayload.Values
                .Select(value => value.SubKeyName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            frozenPayload = _codec.BuildTargetPayload(subKeys);
        }
        else
        {
            frozenPayload = _codec.NormalizeAndValidatePayload(
                JsonSerializer.Deserialize<PerInterfaceRegistryPayload>(targetPayload)
                ?? throw new InvalidDataException("每接口注册表目标请求无法解析。"));
        }

        return new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            frozenPayload,
            PerInterfaceRegistryCodec.Fingerprint(frozenPayload),
            Descriptor.ResourceKey);
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        EnsureScope(change.Scope);
        if (!string.Equals(PerInterfaceRegistryCodec.Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("执行计划中的每接口目标载荷与 fingerprint 不一致。");
        }

        var before = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(false, false, "state.drift", "写入前任一接口注册表值已变化，拒绝执行旧计划。");
        }

        if (string.Equals(before.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(true, false, "execution.no-change", "全部接口注册表值已经处于目标状态。");
        }

        var originals = _codec.ParseCanonicalPayload(before.Payload).Values;
        var targets = _codec.ParseCanonicalPayload(change.TargetPayload).Values;
        var attempted = new List<PerInterfaceValueState>(targets.Count);
        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempted.Add(target);
                await _registry.WriteValueAsync(
                    _definition.RootPath,
                    target.SubKeyName,
                    target.ValueName,
                    ToScalarKind(target.ValueKind),
                    target.StringValue,
                    target.DwordValue,
                    cancellationToken).ConfigureAwait(false);
            }

            var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
            if (string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal))
            {
                return new PlatformExecutionResult(true, true, "execution.verified", "每接口注册表值已逐项写入并整体回读确认。");
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
                "逐接口写入期间取消").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                originals,
                attempted,
                before.Fingerprint,
                "execution.permission-denied",
                "逐接口写入权限不足").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                originals,
                attempted,
                before.Fingerprint,
                "execution.failed",
                $"逐接口写入异常：{ex.GetType().Name}").ConfigureAwait(false);
        }
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        EnsureScope(material.Scope);
        if (!string.Equals(PerInterfaceRegistryCodec.Fingerprint(material.BeforePayload), material.BeforeFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("恢复材料中的每接口原状态 payload 与 fingerprint 不一致。");
        }

        var originals = _codec.ParseCanonicalPayload(material.BeforePayload).Values;
        _codec.EnsureObservedKindsMatchDefinition(new PerInterfaceRegistryPayload(originals));

        var current = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        if (string.Equals(current.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(true, false, "restore.no-change", "当前状态已经等于恢复前状态。");
        }

        if (!string.Equals(current.Fingerprint, material.TargetFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(false, false, "restore.state-drift", "恢复前任一接口注册表值已偏离执行目标，拒绝覆盖外部变更。");
        }

        var targets = _codec.ParseCanonicalPayload(current.Payload).Values;
        var attempted = new List<PerInterfaceValueState>(targets.Count);
        try
        {
            foreach (var target in targets.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempted.Add(target);
                await _registry.RestoreValueAsync(
                    _definition.RootPath,
                    target.SubKeyName,
                    FindOriginal(originals, target.SubKeyName, target.ValueName),
                    cancellationToken).ConfigureAwait(false);
            }

            var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
            if (string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal))
            {
                return new PlatformExecutionResult(true, true, "restore.verified", "每接口注册表值已逆序恢复并整体回读确认。");
            }

            return new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后整体回读与原状态不一致。");
        }
        catch (OperationCanceledException)
        {
            return new PlatformExecutionResult(false, true, "restore.cancelled", "逐接口恢复期间取消。");
        }
        catch (UnauthorizedAccessException)
        {
            return new PlatformExecutionResult(false, true, "restore.permission-denied", "逐接口恢复权限不足。");
        }
        catch (Exception ex)
        {
            return new PlatformExecutionResult(false, true, "restore.failed", $"逐接口恢复异常：{ex.GetType().Name}。");
        }
    }

    public static TargetScope CreateMachineScope() => TargetScope.Machine(Environment.MachineName);

    private async Task<PlatformExecutionResult> BuildExecuteFailureAfterCompensationAsync(
        TargetScope scope,
        IReadOnlyList<PerInterfaceValueState> originals,
        IReadOnlyList<PerInterfaceValueState> attempted,
        string expectedFingerprint,
        string reasonCode,
        string message)
    {
        var allRestored = true;
        foreach (var entry in attempted.Reverse())
        {
            try
            {
                await _registry.RestoreValueAsync(
                    _definition.RootPath,
                    entry.SubKeyName,
                    FindOriginal(originals, entry.SubKeyName, entry.ValueName),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                allRestored = false;
            }
        }

        var compensated = allRestored && await HasFingerprintAsync(scope, expectedFingerprint, CancellationToken.None).ConfigureAwait(false);
        return compensated
            ? new PlatformExecutionResult(false, false, $"{reasonCode}.compensated", $"{message}；已逆序补偿并确认原状态。")
            : new PlatformExecutionResult(false, true, $"{reasonCode}.compensation-failed", $"{message}；补偿或补偿回读失败，需要恢复核查。");
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

    private static PerInterfaceValueState FindOriginal(
        IReadOnlyList<PerInterfaceValueState> originals,
        string subKeyName,
        string valueName) =>
        originals.First(value =>
            string.Equals(value.SubKeyName, subKeyName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.ValueName, valueName, StringComparison.OrdinalIgnoreCase));

    private static PerInterfaceScalarValueKind ToScalarKind(string? valueKind) =>
        string.Equals(valueKind, "String", StringComparison.Ordinal)
            ? PerInterfaceScalarValueKind.String
            : string.Equals(valueKind, "DWord", StringComparison.Ordinal)
                ? PerInterfaceScalarValueKind.DWord
                : throw new InvalidDataException($"不支持的每接口目标类型：{valueKind}。");

    private void EnsureScope(TargetScope scope)
    {
        if (scope.Kind != TargetScopeKind.Machine
            || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("目标作用域不是当前计算机，已阻断 HKLM 每接口注册表能力。");
        }
    }

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
}
