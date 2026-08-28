using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public sealed record RegistryValueDeleteCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string SubKeyPath,
    IReadOnlyList<string> ValueNames,
    ChangeRisk Risk,
    bool RequiresExplicitConfirmation = true,
    bool RequiresRestart = false);

public sealed class RegistryValueDeleteCapability : IChangeCapability
{
    private readonly RegistryValueDeleteCapabilityDefinition _definition;
    private readonly IRegistryValueSetAccessor _registry;
    private readonly TimeProvider _timeProvider;
    private readonly string[] _valueNames;

    public RegistryValueDeleteCapability(
        RegistryValueDeleteCapabilityDefinition definition,
        IRegistryValueSetAccessor registry,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.SubKeyPath);
        _valueNames = definition.ValueNames
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_valueNames.Length == 0 || _valueNames.Length != definition.ValueNames.Count)
        {
            throw new ArgumentException("Registry deletion capability requires unique non-empty value names.", nameof(definition));
        }

        _definition = definition with { ValueNames = Array.AsReadOnly(_valueNames) };
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _timeProvider = timeProvider ?? TimeProvider.System;
        var suffix = _valueNames.Length == 1 ? $"/{_valueNames[0]}" : string.Empty;
        Descriptor = new CapabilityDescriptor(
            definition.CapabilityId,
            definition.DefinitionVersion,
            definition.Risk,
            [TargetScopeKind.CurrentUser],
            RequiresAdministrator: false,
            definition.RequiresExplicitConfirmation,
            RequiresRecoveryMaterial: true,
            definition.RequiresRestart,
            $"registry:HKCU/{definition.SubKeyPath}{suffix}");
    }

    public CapabilityDescriptor Descriptor { get; }

    public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var applicable = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && IsCurrentUser(scope);
        return Task.FromResult(new CapabilityAssessment(
            applicable ? CapabilityApplicability.Applicable : CapabilityApplicability.NotApplicable,
            applicable,
            applicable ? "applicable" : "scope.invalid-or-os-unsupported",
            applicable ? "当前用户注册表删除能力可用。" : "当前作用域或系统版本不支持该修复。"));
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        EnsureCurrentUser(scope);
        var values = await _registry.ReadCurrentUserAsync(
            _definition.SubKeyPath,
            _valueNames,
            cancellationToken).ConfigureAwait(false);
        var payload = Serialize(values);
        return new StateObservation(
            scope,
            payload,
            Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            "HKCU typed registry deletion read",
            "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Deletion capability only accepts 'apply'.", nameof(targetPayload));
        }

        var before = Parse(current.Payload);
        EnsureNames(before);
        var keyExists = before[0].KeyExists;
        var target = before.Select(value => new RegistryValueSetEntryState(
            value.ValueName,
            keyExists,
            false,
            null,
            null,
            null)).ToArray();
        var canonicalTarget = Serialize(target);
        return Task.FromResult(new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            canonicalTarget,
            Fingerprint(canonicalTarget),
            Descriptor.ResourceKey));
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        var before = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(false, false, "state.drift", "写入前状态已变化，拒绝旧计划。 ");
        }

        var target = Parse(change.TargetPayload);
        EnsureNames(target);
        var originals = Parse(change.BeforePayload).ToDictionary(value => value.ValueName, StringComparer.OrdinalIgnoreCase);
        var changedNames = new List<string>(target.Length);
        try
        {
            foreach (var value in target)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _registry.RestoreCurrentUserValueAsync(
                    _definition.SubKeyPath,
                    value,
                    cancellationToken).ConfigureAwait(false);
                changedNames.Add(value.ValueName);
            }
        }
        catch
        {
            using var safetyCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var valueName in changedNames.AsEnumerable().Reverse())
            {
                try
                {
                    await _registry.RestoreCurrentUserValueAsync(
                        _definition.SubKeyPath,
                        originals[valueName],
                        safetyCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // The coordinator will verify current state and retain durable recovery on uncertainty.
                }
            }

            throw;
        }

        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        var verified = string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal);
        return verified
            ? new PlatformExecutionResult(true, changedNames.Count > 0, "execution.verified", "注册表覆盖值已删除并回读验证。")
            : new PlatformExecutionResult(false, changedNames.Count > 0, "verification.mismatch", "删除后的状态与目标不一致。 ");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        var originals = Parse(material.BeforePayload);
        EnsureNames(originals);
        var changed = false;
        foreach (var original in originals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _registry.RestoreCurrentUserValueAsync(
                _definition.SubKeyPath,
                original,
                cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        var verified = string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal);
        return verified
            ? new PlatformExecutionResult(true, changed, "restore.verified", "原注册表状态已恢复并回读验证。")
            : new PlatformExecutionResult(false, changed, "restore.verification-mismatch", "恢复后的状态与快照不一致。 ");
    }

    private static string Serialize(IEnumerable<RegistryValueSetEntryState> values) =>
        JsonSerializer.Serialize(new RegistryValueSetPayload(values
            .OrderBy(value => value.ValueName, StringComparer.OrdinalIgnoreCase)
            .ToArray()));

    private static RegistryValueSetEntryState[] Parse(string payload) =>
        JsonSerializer.Deserialize<RegistryValueSetPayload>(payload)?.Values.ToArray()
        ?? throw new InvalidDataException("Registry deletion payload is invalid.");

    private void EnsureNames(IReadOnlyList<RegistryValueSetEntryState> values)
    {
        var actual = values.Select(value => value.ValueName).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!actual.SequenceEqual(_valueNames, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Registry deletion payload value names do not match the capability definition.");
        }
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static bool IsCurrentUser(TargetScope scope)
    {
        using var identity = WindowsIdentity.GetCurrent();
        return scope.Kind == TargetScopeKind.CurrentUser
            && !string.IsNullOrWhiteSpace(identity.User?.Value)
            && string.Equals(scope.Identity, identity.User.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureCurrentUser(TargetScope scope)
    {
        if (!IsCurrentUser(scope))
        {
            throw new InvalidOperationException("Registry deletion scope does not match the current Windows user.");
        }
    }
}
