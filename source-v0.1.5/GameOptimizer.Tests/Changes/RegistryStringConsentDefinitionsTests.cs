using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryStringConsentDefinitionsTests
{
    public static IEnumerable<object[]> Definitions()
    {
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppCalendarAccess,
            "privacy.deny_app_access_to_calendar",
            "appointments"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppContactsAccess,
            "privacy.deny_app_access_to_contacts",
            "contacts"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppTasksAccess,
            "privacy.deny_app_access_to_tasks",
            "userDataTasks"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppNotificationsAccess,
            "privacy.deny_app_access_to_notifications",
            "userNotificationListener"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppRadioAccess,
            "privacy.deny_app_radio_access",
            "radios"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppMessagingAccess,
            "privacy.deny_app_access_to_messaging",
            "chat"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppEmailAccess,
            "privacy.deny_app_access_to_email",
            "email"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppAccountInfoAccess,
            "privacy.deny_app_access_to_account_info",
            "userAccountInformation"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppDiagnosticInfoAccess,
            "privacy.deny_app_access_to_diagnostic_info",
            "appDiagnostics"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppPhoneCallsAccess,
            "privacy.deny_app_access_to_phone_calls",
            "phoneCall"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppCallHistoryAccess,
            "privacy.deny_app_access_to_call_history",
            "phoneCallHistory"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppTrustedDevicesAccess,
            "privacy.deny_app_access_to_trusted_devices",
            "trustedDevices"
        ];
        yield return
        [
            RegistryStringCapabilityDefinitions.DenyAppMotionAccess,
            "privacy.deny_app_access_to_motion",
            "activity"
        ];
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public void Definition_FreezesExactConsentStoreRegSzSemantics(
        RegistryStringCapabilityDefinition definition,
        string capabilityId,
        string consentCapability)
    {
        Assert.Equal(capabilityId, definition.CapabilityId);
        Assert.Equal(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{consentCapability}",
            definition.SubKeyPath);
        Assert.Equal("Value", definition.ValueName);
        Assert.Equal("Deny", definition.TargetValue);
        Assert.Equal(ChangeRisk.MediumRebootOrExperience, definition.Risk);
        Assert.True(definition.RequiresExplicitConfirmation);
        Assert.False(definition.RequiresRestart);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task MissingValue_ApplyAndRestorePreservesExactMissingKeyState(
        RegistryStringCapabilityDefinition definition,
        string capabilityId,
        string consentCapability)
    {
        Assert.Equal(capabilityId, definition.CapabilityId);
        Assert.EndsWith(consentCapability, definition.SubKeyPath, StringComparison.Ordinal);
        var original = new RegistryStringState(false, false, null, null);
        var accessor = new FakeAccessor(original);
        var capability = new RegistryStringCapability(definition, accessor);

        var (change, applied) = await PrepareAndExecuteAsync(capability);

        Assert.True(applied.Success);
        Assert.Equal(new RegistryStringState(true, true, "String", "Deny"), accessor.State);
        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);
        Assert.True(restored.Success);
        Assert.Equal(original, accessor.State);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task WrongKind_BlocksBeforeWrite(
        RegistryStringCapabilityDefinition definition,
        string capabilityId,
        string consentCapability)
    {
        Assert.Equal(capabilityId, definition.CapabilityId);
        Assert.EndsWith(consentCapability, definition.SubKeyPath, StringComparison.Ordinal);
        var accessor = new FakeAccessor(new RegistryStringState(true, true, "DWord", null));
        var capability = new RegistryStringCapability(definition, accessor);
        var before = await capability.ReadAsync(RegistryStringCapability.CreateCurrentUserScope(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(before, "apply", CancellationToken.None));

        Assert.Equal(0, accessor.WriteCount);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task ExternalDrift_BlocksWithoutWrite(
        RegistryStringCapabilityDefinition definition,
        string capabilityId,
        string consentCapability)
    {
        Assert.Equal(capabilityId, definition.CapabilityId);
        Assert.EndsWith(consentCapability, definition.SubKeyPath, StringComparison.Ordinal);
        var accessor = new FakeAccessor(new RegistryStringState(true, true, "String", "Allow"));
        var capability = new RegistryStringCapability(definition, accessor);
        var scope = RegistryStringCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.State = new RegistryStringState(true, true, "String", "Prompt");

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task ExistingAllowValue_ApplyAndRestorePreservesOriginalRegSz(
        RegistryStringCapabilityDefinition definition,
        string capabilityId,
        string consentCapability)
    {
        Assert.Equal(capabilityId, definition.CapabilityId);
        Assert.EndsWith(consentCapability, definition.SubKeyPath, StringComparison.Ordinal);
        var original = new RegistryStringState(true, true, "String", "Allow");
        var accessor = new FakeAccessor(original);
        var capability = new RegistryStringCapability(definition, accessor);
        var (change, applied) = await PrepareAndExecuteAsync(capability);
        Assert.True(applied.Success);
        Assert.Equal("Deny", accessor.State.Value);

        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal(original, accessor.State);
    }

    [Theory]
    [MemberData(nameof(Definitions))]
    public async Task WriteReadbackMismatch_FailsWithoutFalseSuccess(
        RegistryStringCapabilityDefinition definition,
        string capabilityId,
        string consentCapability)
    {
        Assert.Equal(capabilityId, definition.CapabilityId);
        Assert.EndsWith(consentCapability, definition.SubKeyPath, StringComparison.Ordinal);
        var accessor = new FakeAccessor(new RegistryStringState(true, true, "String", "Allow"))
        {
            IgnoreWrites = true
        };
        var capability = new RegistryStringCapability(definition, accessor);

        var (_, result) = await PrepareAndExecuteAsync(capability);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("verification.mismatch.compensated", result.ReasonCode);
    }

    [Fact]
    public async Task ForeignSid_FailsClosedBeforeAccessorCall()
    {
        var accessor = new FakeAccessor(new RegistryStringState(true, true, "String", "Allow"));
        var capability = new RegistryStringCapability(
            RegistryStringCapabilityDefinitions.DenyAppCalendarAccess,
            accessor);
        var foreign = TargetScope.CurrentUser("S-1-5-21-foreign-consent-test");

        var assessment = await capability.ProbeAsync(foreign, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => capability.ReadAsync(foreign, CancellationToken.None));
        Assert.Equal(0, accessor.ReadCount);
    }

    private static async Task<(PreparedCapabilityChange Change, PlatformExecutionResult Result)> PrepareAndExecuteAsync(
        RegistryStringCapability capability)
    {
        var scope = RegistryStringCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        return (change, await capability.ExecuteAsync(change, CancellationToken.None));
    }

    private static RecoveryMaterial Material(
        RegistryStringCapability capability,
        PreparedCapabilityChange change) => new(
        Guid.NewGuid(), Guid.NewGuid(), capability.Descriptor.CapabilityId,
        capability.Descriptor.DefinitionVersion, change.Scope, change.BeforePayload,
        change.BeforeFingerprint, change.TargetFingerprint, change.ResourceKey,
        DateTimeOffset.UtcNow, "consent-regsz-fake-test");

    private sealed class FakeAccessor(RegistryStringState initial) : IRegistryStringAccessor
    {
        public RegistryStringState State { get; set; } = initial;
        public bool IgnoreWrites { get; set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }

        public Task<RegistryStringState> ReadCurrentUserAsync(
            string subKeyPath, string valueName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(State);
        }

        public Task WriteCurrentUserAsync(
            string subKeyPath, string valueName, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            if (!IgnoreWrites)
            {
                State = new RegistryStringState(true, true, "String", value);
            }

            return Task.CompletedTask;
        }

        public Task RestoreCurrentUserAsync(
            string subKeyPath,
            string valueName,
            RegistryStringState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = original;
            return Task.CompletedTask;
        }

        public Task<RegistryStringState> ReadLocalMachineAsync(
            string subKeyPath, string valueName, CancellationToken cancellationToken) =>
            throw new NotSupportedException("HKLM must not be used by this HKCU-only test fake.");

        public Task WriteLocalMachineAsync(
            string subKeyPath, string valueName, string value, CancellationToken cancellationToken) =>
            throw new NotSupportedException("HKLM must not be used by this HKCU-only test fake.");

        public Task RestoreLocalMachineAsync(
            string subKeyPath, string valueName, RegistryStringState original, CancellationToken cancellationToken) =>
            throw new NotSupportedException("HKLM must not be used by this HKCU-only test fake.");
    }
}
