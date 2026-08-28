using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Optimization.Operations;

internal sealed record AccessibilityCapabilityDefinition(
    string CapabilityId,
    AccessibilitySetting Setting,
    uint TargetClearMask,
    string ResourceKey)
{
    public const string CurrentVersion = "1.0.0";

    public static AccessibilityCapabilityDefinition StickyKeys { get; } = new(
        "accessibility.disable_sticky_keys",
        AccessibilitySetting.StickyKeys,
        0x04 | 0x08,
        "system-parameters:accessibility/sticky-keys");

    public static AccessibilityCapabilityDefinition FilterKeys { get; } = new(
        "accessibility.disable_filter_keys",
        AccessibilitySetting.FilterKeys,
        0x04,
        "system-parameters:accessibility/filter-keys");

    public static AccessibilityCapabilityDefinition ToggleKeys { get; } = new(
        "accessibility.disable_toggle_keys",
        AccessibilitySetting.ToggleKeys,
        0x04,
        "system-parameters:accessibility/toggle-keys");
}

[SupportedOSPlatform("windows")]
public static class AccessibilityChangeCapabilities
{
    public static IChangeCapability CreateStickyKeys(TimeProvider? timeProvider = null) =>
        new AccessibilityChangeCapability(
            AccessibilityCapabilityDefinition.StickyKeys,
            NativeAccessibilitySystemParametersPort.Instance,
            timeProvider: timeProvider);

    public static IChangeCapability CreateFilterKeys(TimeProvider? timeProvider = null) =>
        new AccessibilityChangeCapability(
            AccessibilityCapabilityDefinition.FilterKeys,
            NativeAccessibilitySystemParametersPort.Instance,
            timeProvider: timeProvider);

    public static IChangeCapability CreateToggleKeys(TimeProvider? timeProvider = null) =>
        new AccessibilityChangeCapability(
            AccessibilityCapabilityDefinition.ToggleKeys,
            NativeAccessibilitySystemParametersPort.Instance,
            timeProvider: timeProvider);
}

internal sealed class AccessibilityChangeCapability : IChangeCapability
{
    private readonly AccessibilityCapabilityDefinition _definition;
    private readonly IAccessibilitySystemParametersPort _platform;
    private readonly string _ownerIdentity;
    private readonly TimeProvider _timeProvider;

    public AccessibilityChangeCapability(
        AccessibilityCapabilityDefinition definition,
        IAccessibilitySystemParametersPort platform,
        string? ownerIdentity = null,
        TimeProvider? timeProvider = null)
    {
        _definition = definition;
        _platform = platform;
        _ownerIdentity = string.IsNullOrWhiteSpace(ownerIdentity)
            ? GetCurrentUserIdentity()
            : ownerIdentity;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = new CapabilityDescriptor(
            definition.CapabilityId,
            AccessibilityCapabilityDefinition.CurrentVersion,
            ChangeRisk.MediumRebootOrExperience,
            [TargetScopeKind.CurrentUser],
            RequiresAdministrator: false,
            RequiresExplicitConfirmation: true,
            RequiresRecoveryMaterial: true,
            RequiresRestart: false,
            definition.ResourceKey);
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
                "该辅助功能能力要求 Windows 10 2004 或更高版本。"));
        }

        if (scope.Kind != TargetScopeKind.CurrentUser ||
            !string.Equals(scope.Identity, _ownerIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new CapabilityAssessment(
                CapabilityApplicability.NotApplicable,
                false,
                "scope.invalid",
                "该辅助功能能力只能应用到当前进程对应的用户作用域。"));
        }

        return Task.FromResult(new CapabilityAssessment(
            CapabilityApplicability.Applicable,
            true,
            "applicable",
            "当前用户辅助功能作用域可用。"));
    }

    public Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnedScope(scope);
        var read = ReadParameters();
        if (!read.Success || read.Parameters is null)
        {
            throw new InvalidOperationException(FormatPlatformFailure("read", read.Win32Error));
        }

        var payload = Serialize(read.Parameters);
        return Task.FromResult(new StateObservation(
            scope,
            payload,
            Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            "SystemParametersInfo",
            "Authoritative"));
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnedScope(current.Scope);
        if (!string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("该能力只接受目标值 'apply'。", nameof(targetPayload));
        }

        var before = Deserialize(current.Payload, "当前状态");
        var target = before with { Flags = before.Flags & ~_definition.TargetClearMask };
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

    public Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        ValidatePreparedChange(change);
        cancellationToken.ThrowIfCancellationRequested();
        var current = ReadParameters();
        if (!current.Success || current.Parameters is null)
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                false,
                "accessibility.read-failed",
                FormatPlatformFailure("read", current.Win32Error)));
        }

        var currentPayload = Serialize(current.Parameters);
        if (!string.Equals(Fingerprint(currentPayload), change.BeforeFingerprint, StringComparison.Ordinal))
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                false,
                "state.drift",
                "写入前辅助功能状态已变化，拒绝执行旧计划。"));
        }

        var target = Deserialize(change.TargetPayload, "目标状态");
        cancellationToken.ThrowIfCancellationRequested();
        var write = WriteParameters(target);
        if (!write.Success)
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                true,
                "accessibility.write-failed-unknown-mutation",
                FormatPlatformFailure("write", write.Win32Error)));
        }

        var after = ReadParameters();
        if (!after.Success || after.Parameters is null)
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                true,
                "accessibility.verification-read-failed",
                FormatPlatformFailure("verification-read", after.Win32Error)));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                true,
                "execution.cancelled-after-write",
                "写入后收到取消请求；已保留恢复材料，必须执行恢复或人工确认状态。"));
        }

        var verified = string.Equals(
            Fingerprint(Serialize(after.Parameters)),
            change.TargetFingerprint,
            StringComparison.Ordinal);
        return Task.FromResult(verified
            ? new PlatformExecutionResult(true, true, "execution.verified", "辅助功能设置已写入并完整回读确认。")
            : new PlatformExecutionResult(false, true, "verification.mismatch", "写入后的辅助功能完整状态与计划目标不一致。"));
    }

    public Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        ValidateRecoveryMaterial(material);
        cancellationToken.ThrowIfCancellationRequested();
        var current = ReadParameters();
        if (!current.Success || current.Parameters is null)
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                false,
                "accessibility.restore-read-failed",
                FormatPlatformFailure("restore-read", current.Win32Error)));
        }

        var currentFingerprint = Fingerprint(Serialize(current.Parameters));
        if (!string.Equals(currentFingerprint, material.TargetFingerprint, StringComparison.Ordinal))
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                false,
                "restore.state-drift",
                "恢复前辅助功能状态已偏离该回执的目标状态，拒绝覆盖外部变更。"));
        }

        var original = Deserialize(material.BeforePayload, "恢复材料原状态");
        cancellationToken.ThrowIfCancellationRequested();
        var write = WriteParameters(original);
        if (!write.Success)
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                true,
                "accessibility.restore-write-failed-unknown-mutation",
                FormatPlatformFailure("restore-write", write.Win32Error)));
        }

        var after = ReadParameters();
        if (!after.Success || after.Parameters is null)
        {
            return Task.FromResult(new PlatformExecutionResult(
                false,
                true,
                "accessibility.restore-verification-read-failed",
                FormatPlatformFailure("restore-verification-read", after.Win32Error)));
        }

        var verified = string.Equals(
            Fingerprint(Serialize(after.Parameters)),
            material.BeforeFingerprint,
            StringComparison.Ordinal);
        return Task.FromResult(verified
            ? new PlatformExecutionResult(true, true, "restore.verified", "辅助功能完整原状态已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的辅助功能完整状态与快照不一致。"));
    }

    private AccessibilityParametersReadResult ReadParameters()
    {
        try
        {
            return _platform.ReadParameters(_definition.Setting);
        }
        catch (Exception exception)
        {
            return AccessibilityParametersReadResult.Failed(exception.HResult);
        }
    }

    private AccessibilityFlagsWriteResult WriteParameters(AccessibilityParameters parameters)
    {
        try
        {
            return _platform.WriteParameters(_definition.Setting, parameters);
        }
        catch (Exception exception)
        {
            return AccessibilityFlagsWriteResult.Failed(exception.HResult);
        }
    }

    private void ValidatePreparedChange(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal) ||
            !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal) ||
            !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException("计划与辅助功能 Owner 契约不匹配。");
        }

        EnsureOwnedScope(change.Scope);
        var before = Deserialize(change.BeforePayload, "计划原状态");
        var expectedTarget = before with { Flags = before.Flags & ~_definition.TargetClearMask };
        var expectedPayload = Serialize(expectedTarget);
        if (!string.Equals(Fingerprint(change.BeforePayload), change.BeforeFingerprint, StringComparison.Ordinal) ||
            !string.Equals(change.TargetPayload, expectedPayload, StringComparison.Ordinal) ||
            !string.Equals(Fingerprint(expectedPayload), change.TargetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("计划 payload 或 fingerprint 已损坏或被篡改。");
        }
    }

    private void ValidateRecoveryMaterial(RecoveryMaterial material)
    {
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal) ||
            !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal) ||
            !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
        {
            throw new InvalidDataException("恢复材料与辅助功能 Owner 契约不匹配。");
        }

        EnsureOwnedScope(material.Scope);
        var before = Deserialize(material.BeforePayload, "恢复材料原状态");
        var expectedTarget = before with { Flags = before.Flags & ~_definition.TargetClearMask };
        if (!string.Equals(Fingerprint(material.BeforePayload), material.BeforeFingerprint, StringComparison.Ordinal) ||
            !string.Equals(Fingerprint(Serialize(expectedTarget)), material.TargetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("恢复材料 payload 或 fingerprint 已损坏或被篡改。");
        }
    }

    private void EnsureOwnedScope(TargetScope scope)
    {
        if (scope.Kind != TargetScopeKind.CurrentUser ||
            !string.Equals(scope.Identity, _ownerIdentity, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("辅助功能 Owner 收到非当前用户作用域。");
        }
    }

    private static AccessibilityParameters Deserialize(string payload, string label) =>
        JsonSerializer.Deserialize<AccessibilityParameters>(payload)
        ?? throw new InvalidDataException($"{label}无法解析为完整辅助功能参数。");

    private static string Serialize(AccessibilityParameters parameters) => JsonSerializer.Serialize(parameters);

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static string FormatPlatformFailure(string stage, int error) =>
        error == 0
            ? $"SystemParametersInfo {stage} 失败（accessibility.spi_failed_unknown，Win32=0）。"
            : $"SystemParametersInfo {stage} 失败（accessibility.spi_failed，Win32={error}）。";

    private static string GetCurrentUserIdentity()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        return string.IsNullOrWhiteSpace(sid)
            ? throw new InvalidOperationException("无法确定当前 Windows 用户 SID，辅助功能 Owner 已安全阻断。")
            : sid;
    }
}
