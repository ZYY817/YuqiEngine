// File: GameOptimizer.Core/Services/Appx/AppxRemovalChangeCapability.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Appx;

public sealed record AppxRecoveryClassResolution(
    AppxRecoveryClass RecoveryClass,
    bool DegradedToStoreOnly);

public sealed record AppxRemovalChangeCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    AppxRemovalEntry Entry);

/// <summary>
/// A4 当前用户 AppX 包移除能力。五要素：Probe 作用域与拒绝名单校验、Read 快照+指纹、
/// Prepare 目标解析与 fail-closed 拒绝、Execute 漂移阻断+回读确认、Restore 精确恢复或仅指引。
/// StoreOnly 类恢复永不自标成功，只产出手动重装指引。
/// </summary>
public sealed class AppxRemovalChangeCapability : IChangeCapability
{
    private const string TargetPayloadRemove = "remove";

    private readonly AppxRemovalChangeCapabilityDefinition _definition;
    private readonly IAppxRemovalOwner _owner;
    private readonly TimeProvider _timeProvider;

    public AppxRemovalChangeCapability(
        AppxRemovalChangeCapabilityDefinition definition,
        IAppxRemovalOwner owner,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(owner);
        _definition = definition;
        _owner = owner;
        _timeProvider = timeProvider ?? TimeProvider.System;
        ValidateDefinition(definition);
        Descriptor = new CapabilityDescriptor(
            definition.CapabilityId,
            definition.DefinitionVersion,
            ChangeRisk.HighSystem,
            [TargetScopeKind.Machine],
            RequiresAdministrator: true,
            RequiresExplicitConfirmation: true,
            RequiresRecoveryMaterial: true,
            RequiresRestart: false,
            $"appx:{definition.Entry.FamilyName}");
    }

    public CapabilityDescriptor Descriptor { get; }

    public async Task<CapabilityAssessment> ProbeAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsMachineScope(scope))
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable, false, "scope.invalid", "AppX 当前用户移除能力只能应用到当前计算机作用域。");

        var platform = await _owner.ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (!platform.HasRequiredPermission)
            return new CapabilityAssessment(CapabilityApplicability.NotApplicable, false, platform.ReasonCode, platform.Details);

        if (!_definition.Entry.Allowed)
            return new CapabilityAssessment(
                CapabilityApplicability.NotApplicable, true, "appx.entry-not-allowed", "该包不在已批准的允许名单内，禁止移除。");

        var state = await _owner.ReadStateAsync(_definition.Entry.FamilyName, cancellationToken).ConfigureAwait(false);
        var denial = EvaluateDenial(state);
        if (denial is not null)
            return new CapabilityAssessment(CapabilityApplicability.NotApplicable, true, denial.Value.ReasonCode, denial.Value.Details);

        var resolution = ResolveRecoveryClass(state.IsStaged);
        var details = resolution.DegradedToStoreOnly
            ? "包可移除，但 Staged 载荷不可用，恢复类别已降级为 StoreOnly（需从 Microsoft Store 手动重装）。"
            : $"包可移除；恢复类别 {resolution.RecoveryClass}。";
        return new CapabilityAssessment(CapabilityApplicability.Applicable, true, "applicable", details);
    }

    public async Task<StateObservation> ReadAsync(TargetScope scope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateScope(scope);
        var state = await _owner.ReadStateAsync(_definition.Entry.FamilyName, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(CreateSnapshot(state));
        return new StateObservation(
            scope, payload, Fingerprint(payload), _timeProvider.GetUtcNow(),
            "Windows.Management.Deployment.PackageManager", "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(current);
        var before = DeserializeSnapshot(current.Payload);
        if (!string.Equals(targetPayload, TargetPayloadRemove, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AppX 移除能力只接受固定目标 \"remove\"。");

        if (!ValidateDenial(before, out var violation))
            throw new InvalidDataException($"目标包被拒绝名单阻断（{violation}），fail-closed。");
        if (!before.Installed)
            throw new InvalidDataException("目标包当前未注册到当前用户，无可移除状态。");
        if (!_definition.Entry.Allowed)
            throw new InvalidDataException("该包不在已批准的允许名单内，禁止移除。");

        var target = before with { Installed = false };
        var canonicalTarget = JsonSerializer.Serialize(target);
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
        cancellationToken.ThrowIfCancellationRequested();
        ValidateFrozenChange(change);
        var fresh = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(fresh.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
            return new PlatformExecutionResult(false, false, "state.drift", "AppX 包状态在写入前发生变化，已拒绝旧计划。");

        var before = DeserializeSnapshot(fresh.Payload);
        var target = DeserializeSnapshot(change.TargetPayload);
        if (target.RecoveryClass == AppxRecoveryClass.StagedRegister && !before.IsStaged)
            return new PlatformExecutionResult(false, false, "appx.staged-required", "Staged 载荷缺失，禁止按 StagedRegister 语义执行移除计划。");

        var mutation = await _owner.RemoveForCurrentUserAsync(before.PackageFullName, cancellationToken).ConfigureAwait(false);
        if (!mutation.Success)
            return new PlatformExecutionResult(false, mutation.MutationOccurred, mutation.ReasonCode, mutation.Message);

        var afterState = await ReadInstalledAsync(cancellationToken).ConfigureAwait(false);
        if (afterState)
            return await RetryAndVerifyAsync(before.PackageFullName, mutation.MutationOccurred, cancellationToken).ConfigureAwait(false);

        return new PlatformExecutionResult(true, mutation.MutationOccurred, "execution.verified", "包已完成当前用户移除且回读确认不再可见。");
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(material);
        if (!string.Equals(material.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(material.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(material.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("恢复材料与 AppX 移除能力身份不一致。");
        ValidateScope(material.Scope);

        var original = DeserializeSnapshot(material.BeforePayload);
        var currentSid = AppxRemovalEnvironment.GetCurrentUserSid();
        if (string.IsNullOrWhiteSpace(original.ScopeSid) || !string.Equals(original.ScopeSid, currentSid, StringComparison.Ordinal))
            throw new InvalidDataException("恢复材料来自其他用户作用域，跨用户恢复被拒绝。");
        if (!string.Equals(Fingerprint(material.BeforePayload), material.BeforeFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("恢复材料快照与其指纹不一致。");

        if (original.RecoveryClass == AppxRecoveryClass.StagedRegister
            && !string.IsNullOrWhiteSpace(original.InstallLocation))
        {
            var register = await _owner
                .RegisterFromInstallLocationAsync(original.InstallLocation, cancellationToken)
                .ConfigureAwait(false);
            if (!register.Success)
                return new PlatformExecutionResult(false, register.MutationOccurred, register.ReasonCode, register.Message);

            var afterState = await _owner
                .ReadStateAsync(original.FamilyName, cancellationToken)
                .ConfigureAwait(false);
            var verified = afterState.Installed
                && TryParseVersion(afterState.Version, out var restoredVersion)
                && TryParseVersion(original.Version, out var originalVersion)
                && restoredVersion >= originalVersion;
            return verified
                ? new PlatformExecutionResult(true, true, "restore.verified", "包已从 Staged 载荷重新注册并回读确认可见。")
                : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "重新注册后的包状态与原快照不一致。");
        }

        return new PlatformExecutionResult(
            false, false, "manual-compensation-required",
            $"该包没有可自动执行的恢复材料：需要用户从 Microsoft Store 手动重装"
            + $"（ProductId：{_definition.Entry.ProductId}），本能力不会自动下载安装，也不会伪造恢复成功。");
    }

    private async Task<bool> ReadInstalledAsync(CancellationToken cancellationToken)
    {
        var state = await _owner
            .ReadStateAsync(_definition.Entry.FamilyName, cancellationToken)
            .ConfigureAwait(false);
        return state.Installed;
    }

    /// <summary>Remove 成功但回读仍在时按失败补偿路径重试一次；仍异常则报 applied-but-unverified。</summary>
    private async Task<PlatformExecutionResult> RetryAndVerifyAsync(
        string packageFullName,
        bool mutationOccurred,
        CancellationToken cancellationToken)
    {
        var retry = await _owner.RemoveForCurrentUserAsync(packageFullName, cancellationToken).ConfigureAwait(false);
        if (!retry.Success)
            return new PlatformExecutionResult(false, mutationOccurred, "applied-but-unverified", $"移除后回读仍可见且重试失败：{retry.Message}");

        var verifiedAfterRetry = !(await ReadInstalledAsync(cancellationToken).ConfigureAwait(false));
        return verifiedAfterRetry
            ? new PlatformExecutionResult(true, true, "execution.verified", "首次回读异常，重试后回读确认移除完成。")
            : new PlatformExecutionResult(false, true, "applied-but-unverified", "移除调用返回成功但回读仍可见，请手动核查该包状态。");
    }

    private AppxRemovalSnapshot CreateSnapshot(AppxPackageState state) =>
        new(
            state.Name,
            state.FullName,
            state.FamilyName,
            state.Version,
            state.InstallLocation,
            state.Installed,
            state.IsStaged,
            state.NonRemovable,
            state.IsFramework,
            state.DependencyCount > 0,
            state.SignatureKind,
            AppxRemovalEnvironment.GetCurrentUserSid(),
            ResolveRecoveryClass(state.IsStaged).RecoveryClass);

    /// <summary>
    /// StagedRegister 仅在移除前确认包载荷保留于 WindowsApps（IsStaged）时成立；
    /// 非 Staged 一律降级为 StoreOnly 并固化进快照，Restore 按此分派。
    /// </summary>
    internal AppxRecoveryClassResolution ResolveRecoveryClass(bool isStaged)
    {
        if (_definition.Entry.RecoveryClass == AppxRecoveryClass.StagedRegister && isStaged)
            return new AppxRecoveryClassResolution(AppxRecoveryClass.StagedRegister, DegradedToStoreOnly: false);
        return new AppxRecoveryClassResolution(AppxRecoveryClass.StoreOnly, DegradedToStoreOnly: true);
    }

    private static (string ReasonCode, string Details)? EvaluateDenial(AppxPackageState state)
    {
        if (state.ErrorCode is not null)
            return ("appx.read-failed", $"读取包状态失败（{state.ErrorCode}）。");
        if (!state.Installed)
            return ("appx.package-not-installed", "该包未注册到当前用户，无需移除。");
        if (AppxRemovalDenyList.IsProtected(state.Name) || AppxRemovalDenyList.IsProtected(state.FamilyName))
            return ("appx.package-protected", "该包属于系统核心或 Store 自身等受保护类别，目录硬拒绝移除。");
        if (state.NonRemovable || AppxRemovalDenyList.IsSystemAppsLocation(state.InstallLocation))
            return ("appx.package-nonremovable", "该包被系统标记为不可移除（收件箱/SystemApps 类别）。");
        if (state.IsFramework)
            return ("appx.package-framework", "Framework 包不是用户可见的移除目标。");
        if (state.DependencyCount > 0)
            return ("appx.package-has-dependents", "存在依赖该包的其他应用，移除会破坏依赖方。");
        return null;
    }

    private static bool ValidateDenial(AppxRemovalSnapshot snapshot, out string violation)
    {
        violation = string.Empty;
        if (AppxRemovalDenyList.IsProtected(snapshot.Name) || AppxRemovalDenyList.IsProtected(snapshot.FamilyName))
            violation = "appx.package-protected";
        else if (snapshot.NonRemovable)
            violation = "appx.package-nonremovable";
        else if (snapshot.IsFramework)
            violation = "appx.package-framework";
        else if (snapshot.HasDependency)
            violation = "appx.package-has-dependents";
        else if (AppxRemovalDenyList.IsSystemAppsLocation(snapshot.InstallLocation))
            violation = "appx.package-nonremovable";
        return violation.Length == 0;
    }

    private void ValidateFrozenChange(PreparedCapabilityChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal))
            throw new InvalidDataException("冻结计划与 AppX 移除能力定义不一致。");
        if (!string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("冻结 AppX 移除目标与其指纹不一致。");
    }

    private static void ValidateScope(TargetScope scope)
    {
        if (!IsMachineScope(scope))
            throw new InvalidDataException("AppX 移除能力只能使用当前计算机作用域。");
    }

    private static bool IsMachineScope(TargetScope scope) =>
        scope.Kind == TargetScopeKind.Machine
        && !string.IsNullOrWhiteSpace(scope.Identity)
        && string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

    private static AppxRemovalSnapshot DeserializeSnapshot(string payload) =>
        JsonSerializer.Deserialize<AppxRemovalSnapshot>(payload)
        ?? throw new InvalidDataException("AppX 包状态快照无法解析。");

    private static bool TryParseVersion(string? text, out Version version) =>
        Version.TryParse(text ?? string.Empty, out version!);

    private static void ValidateDefinition(AppxRemovalChangeCapabilityDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentNullException.ThrowIfNull(definition.Entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Entry.FamilyName);
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private sealed record AppxRemovalSnapshot(
        string Name,
        string PackageFullName,
        string FamilyName,
        string Version,
        string InstallLocation,
        bool Installed,
        bool IsStaged,
        bool NonRemovable,
        bool IsFramework,
        bool HasDependency,
        string SignatureKind,
        string ScopeSid,
        AppxRecoveryClass RecoveryClass);
}
