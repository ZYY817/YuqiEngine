using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public enum RegistryDwordHive
{
    CurrentUser,
    LocalMachine
}

public sealed record RegistryDwordCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string SubKeyPath,
    string ValueName,
    int TargetValue,
    ChangeRisk Risk,
    bool RequiresExplicitConfirmation = false,
    RegistryDwordHive Hive = RegistryDwordHive.CurrentUser,
    bool RequiresRestart = false);

public enum RegistryDwordTargetAction
{
    Set,
    Delete
}

public sealed record RegistryDwordTargetRequest(RegistryDwordTargetAction Action, int? Value)
{
    public static RegistryDwordTargetRequest Set(int value) => new(RegistryDwordTargetAction.Set, value);
    public static RegistryDwordTargetRequest Delete() => new(RegistryDwordTargetAction.Delete, null);
}

/// <summary>
/// Read-only platform facts required to decide whether a registry capability can be planned.
/// The default provider keeps the former Windows 10 2004+ gate; tests may inject deterministic
/// facts without touching the real registry or host token.
/// </summary>
public sealed record RegistryDwordCapabilityEnvironment(
    bool IsWindows,
    int WindowsBuild,
    string Edition,
    string MachineName,
    string CurrentUserIdentity,
    bool IsAdministrator);

public interface IRegistryDwordCapabilityEnvironmentProvider
{
    RegistryDwordCapabilityEnvironment GetCurrent();
}

public sealed class WindowsRegistryDwordCapabilityEnvironmentProvider : IRegistryDwordCapabilityEnvironmentProvider
{
    public RegistryDwordCapabilityEnvironment GetCurrent() => new(
        OperatingSystem.IsWindows(),
        Environment.OSVersion.Version.Build,
        GetEdition(),
        Environment.MachineName,
        GetCurrentUserIdentity(),
        IsAdministrator());

    private static string GetEdition()
    {
        var productName = Environment.OSVersion.VersionString;
        return string.IsNullOrWhiteSpace(productName) ? "unknown" : productName;
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

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;
}

public class RegistryDwordCapability : IChangeCapability
{
    private readonly RegistryDwordCapabilityDefinition _definition;
    private readonly IRegistryDwordAccessor _registry;
    private readonly TimeProvider _timeProvider;
    private readonly IRegistryDwordCapabilityEnvironmentProvider _environmentProvider;

    public RegistryDwordCapability(
        RegistryDwordCapabilityDefinition definition,
        IRegistryDwordAccessor registry,
        TimeProvider? timeProvider = null,
        IRegistryDwordCapabilityEnvironmentProvider? environmentProvider = null)
    {
        _definition = definition;
        _registry = registry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _environmentProvider = environmentProvider ?? new WindowsRegistryDwordCapabilityEnvironmentProvider();
        var isMachine = definition.Hive == RegistryDwordHive.LocalMachine;
        Descriptor = new CapabilityDescriptor(
            definition.CapabilityId,
            definition.DefinitionVersion,
            definition.Risk,
            [isMachine ? TargetScopeKind.Machine : TargetScopeKind.CurrentUser],
            RequiresAdministrator: isMachine,
            definition.RequiresExplicitConfirmation,
            RequiresRecoveryMaterial: true,
            definition.RequiresRestart,
            ResourceKey: $"registry:{(isMachine ? "HKLM" : "HKCU")}/{definition.SubKeyPath}/{definition.ValueName}");
    }

    public CapabilityDescriptor Descriptor { get; }

    public Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var environment = _environmentProvider.GetCurrent();
        if (!environment.IsWindows || environment.WindowsBuild < 19041)
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
                || !string.Equals(scope.Identity, environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CapabilityAssessment(
                    CapabilityApplicability.NotApplicable,
                    false,
                    "scope.invalid",
                    "该注册表能力只能应用到当前计算机作用域。"));
            }

            var isAdministrator = environment.IsAdministrator;
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.Applicable,
                isAdministrator,
                isAdministrator ? "applicable" : "permission.administrator-required",
                isAdministrator ? "本机注册表作用域可用。" : "修改 HKLM 需要已提升的管理员令牌。"));
        }

        var currentUserIdentity = environment.CurrentUserIdentity;
        if (scope.Kind != TargetScopeKind.CurrentUser
            || !string.Equals(scope.Identity, currentUserIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "scope.invalid",
                "该注册表能力只能应用到当前进程对应的用户作用域。"));
        }

        return Task.FromResult(new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            true,
            "applicable",
            "当前用户注册表作用域可用。"));
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        var state = _definition.Hive == RegistryDwordHive.LocalMachine
            ? await _registry.ReadLocalMachineAsync(
                _definition.SubKeyPath,
                _definition.ValueName,
                cancellationToken).ConfigureAwait(false)
            : await _registry.ReadCurrentUserAsync(
                _definition.SubKeyPath,
                _definition.ValueName,
                cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(state);
        return new StateObservation(
            scope,
            payload,
            Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            _definition.Hive == RegistryDwordHive.LocalMachine ? "HKLM registry read" : "HKCU registry read",
            "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase)
            ? RegistryDwordTargetRequest.Set(_definition.TargetValue)
            : JsonSerializer.Deserialize<RegistryDwordTargetRequest>(targetPayload)
              ?? throw new InvalidDataException("DWORD target request cannot be parsed.");
        var observed = JsonSerializer.Deserialize<RegistryDwordState>(current.Payload)
            ?? throw new InvalidDataException("当前 DWORD 注册表状态无法解析。");
        ValidateObservedState(observed);
        ValidateTargetRequest(request);
        var canonicalTarget = JsonSerializer.Serialize(request.Action == RegistryDwordTargetAction.Delete
            ? new RegistryDwordState(true, false, null)
            : new RegistryDwordState(true, true, request.Value, "DWord"));
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
            return new PlatformExecutionResult(false, false, "state.drift", "写入前状态已变化，拒绝执行旧计划。");
        }

        if (!string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Frozen DWORD target payload does not match its fingerprint.");
        }

        var target = JsonSerializer.Deserialize<RegistryDwordState>(change.TargetPayload)
            ?? throw new InvalidDataException("Frozen DWORD target cannot be parsed.");
        if (target.ValueExists && target.Value is null)
        {
            throw new InvalidDataException("Frozen DWORD Set target is missing its value.");
        }
        ValidateObservedState(target);

        if (_definition.Hive == RegistryDwordHive.LocalMachine)
        {
            if (target.ValueExists)
            {
                await _registry.WriteLocalMachineAsync(
                    _definition.SubKeyPath, _definition.ValueName, target.Value!.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _registry.RestoreLocalMachineAsync(
                    _definition.SubKeyPath, _definition.ValueName, target, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            if (target.ValueExists)
            {
                await _registry.WriteCurrentUserAsync(
                    _definition.SubKeyPath, _definition.ValueName, target.Value!.Value, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _registry.RestoreCurrentUserAsync(
                    _definition.SubKeyPath, _definition.ValueName, target, cancellationToken).ConfigureAwait(false);
            }
        }
        var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        var verified = string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal);
        return verified
            ? new PlatformExecutionResult(true, true, "execution.verified", "注册表值已写入并回读确认。")
            : new PlatformExecutionResult(false, true, "verification.mismatch", "注册表写入后的状态与目标不一致。");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        var original = JsonSerializer.Deserialize<RegistryDwordState>(material.BeforePayload)
            ?? throw new InvalidDataException("恢复材料中的原注册表状态无法解析。");
        if (original.ValueExists && original.Value is null)
        {
            throw new InvalidDataException("恢复材料声明原值存在，但未包含 DWORD 数值。");
        }
        ValidateObservedState(original);

        if (_definition.Hive == RegistryDwordHive.LocalMachine)
        {
            await _registry.RestoreLocalMachineAsync(
                _definition.SubKeyPath,
                _definition.ValueName,
                original,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _registry.RestoreCurrentUserAsync(
                _definition.SubKeyPath,
                _definition.ValueName,
                original,
                cancellationToken).ConfigureAwait(false);
        }
        var after = await ReadAsync(material.Scope, cancellationToken).ConfigureAwait(false);
        var verified = string.Equals(after.Fingerprint, material.BeforeFingerprint, StringComparison.Ordinal);
        return verified
            ? new PlatformExecutionResult(true, true, "restore.verified", "原注册表状态已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的注册表状态与快照不一致。");
    }

    public static TargetScope CreateCurrentUserScope() => TargetScope.CurrentUser(GetCurrentUserIdentity());

    public static TargetScope CreateMachineScope() => TargetScope.Machine(Environment.MachineName);

    private static string GetCurrentUserIdentity()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
        {
            throw new InvalidOperationException("无法确定当前 Windows 用户 SID，已阻断注册表能力。");
        }

        return sid;
    }

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static void ValidateTargetRequest(RegistryDwordTargetRequest request)
    {
        var valid = request.Action switch
        {
            RegistryDwordTargetAction.Set => request.Value is not null,
            RegistryDwordTargetAction.Delete => request.Value is null,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException("DWORD target action and payload are inconsistent.");
        }
    }

    private static void ValidateObservedState(RegistryDwordState state)
    {
        if (state.ValueExists && !string.Equals(state.ValueKind, "DWord", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("注册表值不是 DWORD，拒绝隐式转换或覆盖。");
        }
    }
}
