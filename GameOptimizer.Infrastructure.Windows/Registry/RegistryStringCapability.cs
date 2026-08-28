using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public sealed record RegistryStringCapabilityDefinition(
    string CapabilityId,
    string DefinitionVersion,
    string SubKeyPath,
    string ValueName,
    string TargetValue,
    ChangeRisk Risk,
    bool RequiresExplicitConfirmation = false,
    bool RequiresRestart = false,
    RegistryDwordHive Hive = RegistryDwordHive.CurrentUser);

public enum RegistryStringTargetAction
{
    Set,
    Delete
}

public sealed record RegistryStringTargetRequest(
    RegistryStringTargetAction Action,
    string? Value,
    bool RequireExistingValue = false)
{
    public static RegistryStringTargetRequest Set(string value) => new(RegistryStringTargetAction.Set, value);
    public static RegistryStringTargetRequest Delete() => new(RegistryStringTargetAction.Delete, null);
}

public static class RegistryStringCapabilityDefinitions
{
    public static RegistryStringCapabilityDefinition DisablePointerTrails { get; } = new(
        "input.disable_pointer_trails",
        "1.0.0",
        @"Control Panel\Mouse",
        "MouseTrails",
        "0",
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    public static RegistryStringCapabilityDefinition DisableSnapToDefaultButton { get; } = new(
        "input.disable_snap_to",
        "1.0.0",
        @"Control Panel\Mouse",
        "SnapToDefaultButton",
        "0",
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    public static RegistryStringCapabilityDefinition DenyAppCalendarAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_calendar",
        "appointments");

    public static RegistryStringCapabilityDefinition DenyAppContactsAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_contacts",
        "contacts");

    public static RegistryStringCapabilityDefinition DenyAppTasksAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_tasks",
        "userDataTasks");

    public static RegistryStringCapabilityDefinition DenyAppNotificationsAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_notifications",
        "userNotificationListener");

    public static RegistryStringCapabilityDefinition LocationConsent { get; } = DenyConsentStore(
        "privacy.location.consent",
        "location");

    public static RegistryStringCapabilityDefinition WebcamConsent { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_camera",
        "webcam");

    public static RegistryStringCapabilityDefinition MicrophoneConsent { get; } = DenyConsentStore(
        "privacy.microphone.consent",
        "microphone");

    public static RegistryStringCapabilityDefinition DenyAppRadioAccess { get; } = DenyConsentStore(
        "privacy.deny_app_radio_access",
        "radios");

    public static RegistryStringCapabilityDefinition DenyAppMessagingAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_messaging",
        "chat");

    public static RegistryStringCapabilityDefinition DenyAppEmailAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_email",
        "email");

    public static RegistryStringCapabilityDefinition DenyAppAccountInfoAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_account_info",
        "userAccountInformation");

    public static RegistryStringCapabilityDefinition DenyAppDiagnosticInfoAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_diagnostic_info",
        "appDiagnostics");

    public static RegistryStringCapabilityDefinition DenyAppPhoneCallsAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_phone_calls",
        "phoneCall");

    public static RegistryStringCapabilityDefinition DenyAppCallHistoryAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_call_history",
        "phoneCallHistory");

    public static RegistryStringCapabilityDefinition DenyAppTrustedDevicesAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_trusted_devices",
        "trustedDevices");

    public static RegistryStringCapabilityDefinition DenyAppMotionAccess { get; } = DenyConsentStore(
        "privacy.deny_app_access_to_motion",
        "activity");

    public static RegistryStringCapabilityDefinition PrioritizeGames { get; } = new(
        "system.prioritize_games",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
        "Scheduling Category",
        "High",
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    // 备注：本项把 Games 任务的 Latency Sensitive 写为 True 以降低音频缓冲延迟；
    // 只改变多媒体任务延迟敏感标记，不删除系统保护。写入前捕获原值，恢复时按原值回写。
    public static RegistryStringCapabilityDefinition OptimizeAudioBuffer { get; } = new(
        "audio.optimize_buffer",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
        "Latency Sensitive",
        "True",
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    // 备注：本项把 TCP 拥塞提供程序（CongestionProvider）写为 ctcp 以启用复合 TCP（Compound TCP），
    // 在宽带链路上更快从拥塞中恢复；只改变 TCP/IP 栈参数，不删除系统保护。写入前捕获原值，
    // 恢复时按原值回写；缺失值恢复为删除。重启后由 TCP/IP 栈重新读取。
    public static RegistryStringCapabilityDefinition SetCongestionCtcp { get; } = new(
        "network.set_congestion_ctcp",
        "1.0.0",
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters",
        "CongestionProvider",
        "ctcp",
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        RequiresRestart: true,
        Hive: RegistryDwordHive.LocalMachine);

    // 备注：本项把 DirectX 全局 GPU 首选项（DirectXUserGlobalSettings）写为 GpuPreference=2;
    // 让系统默认使用高性能 GPU；这是 Windows 官方「图形性能首选项」全局设置，非显卡驱动私有值。
    // 写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。对新启动应用生效。
    public static RegistryStringCapabilityDefinition ForceHighPerformanceGpu { get; } = new(
        "gpu.force_high_performance_gpu",
        "1.0.0",
        @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences",
        "DirectXUserGlobalSettings",
        "GpuPreference=2;",
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);

    // 备注：本项把 Games 多媒体任务的 SFIO Priority 写为 High 以提升游戏流式 I/O 优先级；
    // 这是标准 Windows MMCSS 调度设置（Tasks\Games），非显卡驱动私有值。写入前捕获原值，
    // 恢复时按原值回写；缺失值恢复为删除。
    public static RegistryStringCapabilityDefinition SetSfioPriority { get; } = new(
        "gpu.set_sfio_priority",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
        "SFIO Priority",
        "High",
        ChangeRisk.LowReversible,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    // 备注：本项把 Explorer 的 SmartScreenEnabled 写为 Off 以关闭 SmartScreen 应用信誉检查；
    // 这是官方 Explorer 设置值，非驱动私有值。写入前捕获原值，恢复时按原值回写；缺失值恢复为删除。
    public static RegistryStringCapabilityDefinition DisableSmartScreen { get; } = new(
        "security.disable_smartscreen",
        "1.0.0",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer",
        "SmartScreenEnabled",
        "Off",
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true,
        Hive: RegistryDwordHive.LocalMachine);

    private static RegistryStringCapabilityDefinition DenyConsentStore(
        string capabilityId,
        string consentCapability) => new(
        capabilityId,
        "1.0.0",
        $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{consentCapability}",
        "Value",
        "Deny",
        ChangeRisk.MediumRebootOrExperience,
        RequiresExplicitConfirmation: true);
}

public sealed class RegistryStringCapability : IChangeCapability
{
    private const string StringValueKind = "String";
    private static readonly TimeSpan DefaultSafetyCompensationTimeout = TimeSpan.FromSeconds(10);
    private readonly RegistryStringCapabilityDefinition _definition;
    private readonly IRegistryStringAccessor _registry;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _safetyCompensationTimeout;
    private readonly string _targetPayload;
    private readonly string _targetFingerprint;

    public RegistryStringCapability(
        RegistryStringCapabilityDefinition definition,
        IRegistryStringAccessor registry,
        TimeProvider? timeProvider = null,
        TimeSpan? safetyCompensationTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.CapabilityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.SubKeyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.ValueName);
        ArgumentNullException.ThrowIfNull(definition.TargetValue);
        _definition = definition;
        _registry = registry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _safetyCompensationTimeout = safetyCompensationTimeout ?? DefaultSafetyCompensationTimeout;
        if (_safetyCompensationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(safetyCompensationTimeout));
        }

        _targetPayload = JsonSerializer.Serialize(
            new RegistryStringState(true, true, StringValueKind, _definition.TargetValue));
        _targetFingerprint = Fingerprint(_targetPayload);
        var isMachine = _definition.Hive == RegistryDwordHive.LocalMachine;
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
        var state = _definition.Hive == RegistryDwordHive.LocalMachine
            ? await _registry.ReadLocalMachineAsync(
                _definition.SubKeyPath,
                _definition.ValueName,
                cancellationToken).ConfigureAwait(false)
            : await _registry.ReadCurrentUserAsync(
                _definition.SubKeyPath,
                _definition.ValueName,
                cancellationToken).ConfigureAwait(false);
        ValidateObservedState(state);
        var payload = JsonSerializer.Serialize(state);
        return new StateObservation(
            scope,
            payload,
            Fingerprint(payload),
            _timeProvider.GetUtcNow(),
            _definition.Hive == RegistryDwordHive.LocalMachine ? "HKLM REG_SZ registry read" : "HKCU REG_SZ registry read",
            "Authoritative");
    }

    public Task<PreparedCapabilityChange> PrepareAsync(
        StateObservation current,
        string targetPayload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureScope(current.Scope);
        var currentState = DeserializeAndValidate(current.Payload);
        EnsureStringOrMissing(currentState);
        var request = string.Equals(targetPayload, "apply", StringComparison.OrdinalIgnoreCase)
            ? RegistryStringTargetRequest.Set(_definition.TargetValue)
            : JsonSerializer.Deserialize<RegistryStringTargetRequest>(targetPayload)
              ?? throw new InvalidDataException("REG_SZ target request cannot be parsed.");
        ValidateTargetRequest(request);
        var frozenTarget = request.RequireExistingValue && !currentState.ValueExists
            ? current.Payload
            : JsonSerializer.Serialize(request.Action == RegistryStringTargetAction.Delete
                ? new RegistryStringState(true, false, null, null)
                : new RegistryStringState(true, true, StringValueKind, request.Value));
        return Task.FromResult(new PreparedCapabilityChange(
            Descriptor.CapabilityId,
            Descriptor.DefinitionVersion,
            current.Scope,
            current.Payload,
            current.Fingerprint,
            frozenTarget,
            Fingerprint(frozenTarget),
            Descriptor.ResourceKey));
    }

    public async Task<PlatformExecutionResult> ExecuteAsync(
        PreparedCapabilityChange change,
        CancellationToken cancellationToken)
    {
        EnsureScope(change.Scope);
        EnsureFrozenTarget(change);
        var before = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(before.Fingerprint, change.BeforeFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(false, false, "state.drift", "写入前状态已变化，拒绝执行旧计划。");
        }

        var original = DeserializeAndValidate(before.Payload);
        EnsureStringOrMissing(original);
        var target = DeserializeAndValidate(change.TargetPayload);
        EnsureStringOrMissing(target);
        if (string.Equals(before.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal))
        {
            return new PlatformExecutionResult(true, false, "execution.no-change", "REG_SZ 已经处于目标状态。");
        }

        try
        {
            if (target.ValueExists)
            {
                if (_definition.Hive == RegistryDwordHive.LocalMachine)
                {
                    await _registry.WriteLocalMachineAsync(
                        _definition.SubKeyPath, _definition.ValueName, target.Value!, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _registry.WriteCurrentUserAsync(
                        _definition.SubKeyPath, _definition.ValueName, target.Value!, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                if (_definition.Hive == RegistryDwordHive.LocalMachine)
                {
                    await _registry.RestoreLocalMachineAsync(
                        _definition.SubKeyPath, _definition.ValueName, target, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _registry.RestoreCurrentUserAsync(
                        _definition.SubKeyPath, _definition.ValueName, target, cancellationToken).ConfigureAwait(false);
                }
            }
            var after = await ReadAsync(change.Scope, cancellationToken).ConfigureAwait(false);
            if (string.Equals(after.Fingerprint, change.TargetFingerprint, StringComparison.Ordinal))
            {
                return new PlatformExecutionResult(true, true, "execution.verified", "REG_SZ 已写入并回读确认。");
            }

            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                original,
                before.Fingerprint,
                "verification.mismatch",
                "REG_SZ 写入后的状态与目标不一致").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                original,
                before.Fingerprint,
                "execution.cancelled",
                "REG_SZ 写入期间取消").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                original,
                before.Fingerprint,
                "execution.permission-denied",
                "REG_SZ 写入权限不足").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await BuildExecuteFailureAfterCompensationAsync(
                change.Scope,
                original,
                before.Fingerprint,
                "execution.failed",
                $"REG_SZ 写入异常：{ex.GetType().Name}").ConfigureAwait(false);
        }
    }

    public async Task<PlatformExecutionResult> RestoreAsync(
        RecoveryMaterial material,
        CancellationToken cancellationToken)
    {
        EnsureScope(material.Scope);
        var original = DeserializeAndValidate(material.BeforePayload);
        EnsureStringOrMissing(original);
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
            ? new PlatformExecutionResult(true, true, "restore.verified", "原 REG_SZ 状态已恢复并回读确认。")
            : new PlatformExecutionResult(false, true, "restore.verification-mismatch", "恢复后的 REG_SZ 状态与快照不一致。");
    }

    public static TargetScope CreateCurrentUserScope() => TargetScope.CurrentUser(GetCurrentUserIdentity());

    public static TargetScope CreateMachineScope() => TargetScope.Machine(Environment.MachineName);

    private async Task<PlatformExecutionResult> BuildExecuteFailureAfterCompensationAsync(
        TargetScope scope,
        RegistryStringState original,
        string expectedFingerprint,
        string reasonCode,
        string message)
    {
        var compensated = await RestoreOriginalAndVerifyAsync(
            scope,
            original,
            expectedFingerprint).ConfigureAwait(false);
        return compensated
            ? new PlatformExecutionResult(
                false,
                false,
                $"{reasonCode}.compensated",
                $"{message}；已恢复并回读确认原 REG_SZ 状态。")
            : new PlatformExecutionResult(
                false,
                true,
                $"{reasonCode}.compensation-failed",
                $"{message}；补偿或补偿回读失败，需要恢复核查。");
    }

    private async Task<bool> RestoreOriginalAndVerifyAsync(
        TargetScope scope,
        RegistryStringState original,
        string expectedFingerprint)
    {
        using var safetySource = new CancellationTokenSource(_safetyCompensationTimeout);
        try
        {
            if (_definition.Hive == RegistryDwordHive.LocalMachine)
            {
                await _registry.RestoreLocalMachineAsync(
                    _definition.SubKeyPath,
                    _definition.ValueName,
                    original,
                    safetySource.Token).ConfigureAwait(false);
            }
            else
            {
                await _registry.RestoreCurrentUserAsync(
                    _definition.SubKeyPath,
                    _definition.ValueName,
                    original,
                    safetySource.Token).ConfigureAwait(false);
            }
            if (safetySource.IsCancellationRequested)
            {
                return false;
            }

            var after = await ReadAsync(scope, safetySource.Token).ConfigureAwait(false);
            return !safetySource.IsCancellationRequested
                && string.Equals(after.Fingerprint, expectedFingerprint, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private void EnsureFrozenTarget(PreparedCapabilityChange change)
    {
        if (!string.Equals(change.CapabilityId, Descriptor.CapabilityId, StringComparison.Ordinal)
            || !string.Equals(change.DefinitionVersion, Descriptor.DefinitionVersion, StringComparison.Ordinal)
            || !string.Equals(change.ResourceKey, Descriptor.ResourceKey, StringComparison.Ordinal)
            || !string.Equals(Fingerprint(change.TargetPayload), change.TargetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("准备计划中的 REG_SZ 目标、版本或资源绑定无效。");
        }
    }

    private static RegistryStringState DeserializeAndValidate(string payload)
    {
        var state = JsonSerializer.Deserialize<RegistryStringState>(payload)
            ?? throw new InvalidDataException("注册表状态无法解析。");
        ValidateObservedState(state);
        return state;
    }

    private static void ValidateObservedState(RegistryStringState state)
    {
        if (!state.KeyExists && state.ValueExists)
        {
            throw new InvalidDataException("注册表状态声明键不存在但值存在。");
        }

        if (!state.ValueExists && (state.ValueKind is not null || state.Value is not null))
        {
            throw new InvalidDataException("注册表状态声明值不存在但仍包含类型或内容。");
        }

        if (state.ValueExists && string.IsNullOrWhiteSpace(state.ValueKind))
        {
            throw new InvalidDataException("注册表值存在但缺少值类型。");
        }

        if (state.ValueExists && string.Equals(state.ValueKind, StringValueKind, StringComparison.Ordinal) && state.Value is null)
        {
            throw new InvalidDataException("REG_SZ 状态缺少字符串内容。");
        }
    }

    private static void EnsureStringOrMissing(RegistryStringState state)
    {
        if (state.ValueExists && !string.Equals(state.ValueKind, StringValueKind, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"目标注册表值类型为 {state.ValueKind}，不是 REG_SZ，已阻断隐式覆盖。");
        }
    }

    private static void EnsureCurrentUserScope(TargetScope scope)
    {
        if (!IsCurrentUserScope(scope))
        {
            throw new InvalidOperationException("目标作用域不是当前用户 SID，已阻断 REG_SZ 能力。");
        }
    }

    private void EnsureScope(TargetScope scope)
    {
        if (_definition.Hive == RegistryDwordHive.LocalMachine)
        {
            if (scope.Kind != TargetScopeKind.Machine
                || !string.Equals(scope.Identity, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("目标作用域不是当前计算机，已阻断 HKLM REG_SZ 能力。");
            }

            return;
        }

        EnsureCurrentUserScope(scope);
    }

    private static bool IsCurrentUserScope(TargetScope scope) =>
        scope.Kind == TargetScopeKind.CurrentUser
        && string.Equals(scope.Identity, GetCurrentUserIdentity(), StringComparison.OrdinalIgnoreCase);

    private static string GetCurrentUserIdentity()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
        {
            throw new InvalidOperationException("无法确定当前 Windows 用户 SID，已阻断注册表能力。");
        }

        return sid;
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string Fingerprint(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private static void ValidateTargetRequest(RegistryStringTargetRequest request)
    {
        var valid = request.Action switch
        {
            RegistryStringTargetAction.Set => request.Value is not null,
            RegistryStringTargetAction.Delete => request.Value is null,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException("REG_SZ target action and payload are inconsistent.");
        }
    }
}
