using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryStringCapabilityTests
{
    [Fact]
    public async Task MissingValue_AppliesAndRestoresExactMissingKeyState()
    {
        var accessor = new FakeRegistryStringAccessor(new RegistryStringState(false, false, null, null));
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.DisablePointerTrails, accessor);
        var scope = RegistryStringCapability.CreateCurrentUserScope();

        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.Equal(new RegistryStringState(true, true, "String", "0"), accessor.State);
        Assert.Equal(1, accessor.WriteCount);

        var restored = await capability.RestoreAsync(CreateRecoveryMaterial(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal(new RegistryStringState(false, false, null, null), accessor.State);
        Assert.Equal(1, accessor.RestoreCount);
    }

    [Fact]
    public async Task ExistingString_IsRestoredWithOriginalValue()
    {
        var original = new RegistryStringState(true, true, "String", "1");
        var accessor = new FakeRegistryStringAccessor(original);
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.DisableSnapToDefaultButton, accessor);
        var scope = RegistryStringCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        Assert.True((await capability.ExecuteAsync(change, CancellationToken.None)).Success);
        Assert.Equal("0", accessor.State.Value);
        Assert.True((await capability.RestoreAsync(CreateRecoveryMaterial(capability, change), CancellationToken.None)).Success);
        Assert.Equal(original, accessor.State);
    }

    [Fact]
    public async Task WrongValueKind_IsObservedButBlockedBeforeWrite()
    {
        var accessor = new FakeRegistryStringAccessor(new RegistryStringState(true, true, "DWord", null));
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.DisablePointerTrails, accessor);
        var before = await capability.ReadAsync(RegistryStringCapability.CreateCurrentUserScope(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(before, "apply", CancellationToken.None));

        Assert.Contains("不是 REG_SZ", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, accessor.WriteCount);
        Assert.Equal(0, accessor.RestoreCount);
    }

    [Fact]
    public async Task PointerTrails_MinusOneNeverMatchesTargetZero()
    {
        var accessor = new FakeRegistryStringAccessor(new RegistryStringState(true, true, "String", "-1"));
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.DisablePointerTrails, accessor);
        var before = await capability.ReadAsync(RegistryStringCapability.CreateCurrentUserScope(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var target = JsonSerializer.Deserialize<RegistryStringState>(change.TargetPayload);

        Assert.NotEqual(before.Fingerprint, change.TargetFingerprint);
        Assert.NotNull(target);
        Assert.Equal("0", target.Value);
        Assert.NotEqual("-1", target.Value);
    }

    [Fact]
    public async Task ForeignUserScope_FailsClosedWithoutAccessorCall()
    {
        var accessor = new FakeRegistryStringAccessor(new RegistryStringState(true, true, "String", "1"));
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.DisablePointerTrails, accessor);
        var foreignScope = TargetScope.CurrentUser("S-1-5-21-foreign-user");

        var assessment = await capability.ProbeAsync(foreignScope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => capability.ReadAsync(foreignScope, CancellationToken.None));
        Assert.Equal(0, accessor.ReadCount);
    }

    [Fact]
    public async Task CancelledRead_DoesNotWriteOrRestore()
    {
        var accessor = new FakeRegistryStringAccessor(new RegistryStringState(true, true, "String", "1"));
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.DisablePointerTrails, accessor);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => capability.ReadAsync(RegistryStringCapability.CreateCurrentUserScope(), source.Token));

        Assert.Equal(0, accessor.WriteCount);
        Assert.Equal(0, accessor.RestoreCount);
    }

    [Fact]
    public async Task DynamicAllowTarget_IsFrozenAppliedAndExactlyRestored()
    {
        var original = new RegistryStringState(true, true, "String", "Deny");
        var accessor = new FakeRegistryStringAccessor(original);
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.WebcamConsent, accessor);
        var before = await capability.ReadAsync(RegistryStringCapability.CreateCurrentUserScope(), CancellationToken.None);
        var request = JsonSerializer.Serialize(RegistryStringTargetRequest.Set("Allow"));

        var change = await capability.PrepareAsync(before, request, CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);
        var restored = await capability.RestoreAsync(CreateRecoveryMaterial(capability, change), CancellationToken.None);

        Assert.True(applied.Success);
        Assert.True(restored.Success);
        Assert.Equal(original, accessor.State);
    }

    [Fact]
    public async Task DynamicDelete_IsDistinctFromSet_AndLegacyApplyRemainsCompatible()
    {
        var accessor = new FakeRegistryStringAccessor(new RegistryStringState(true, true, "String", "Allow"));
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.WebcamConsent, accessor);
        var before = await capability.ReadAsync(RegistryStringCapability.CreateCurrentUserScope(), CancellationToken.None);
        var set = await capability.PrepareAsync(before, JsonSerializer.Serialize(RegistryStringTargetRequest.Set("Deny")), CancellationToken.None);
        var delete = await capability.PrepareAsync(before, JsonSerializer.Serialize(RegistryStringTargetRequest.Delete()), CancellationToken.None);
        var legacy = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        Assert.NotEqual(set.TargetFingerprint, delete.TargetFingerprint);
        Assert.Equal(set.TargetFingerprint, legacy.TargetFingerprint);
        Assert.True((await capability.ExecuteAsync(delete, CancellationToken.None)).Success);
        Assert.False(accessor.State.ValueExists);
    }

    [Fact]
    public async Task InconsistentDynamicTarget_IsRejectedBeforeWrite()
    {
        var accessor = new FakeRegistryStringAccessor(new RegistryStringState(true, true, "String", "Deny"));
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.WebcamConsent, accessor);
        var before = await capability.ReadAsync(RegistryStringCapability.CreateCurrentUserScope(), CancellationToken.None);
        var invalid = JsonSerializer.Serialize(new RegistryStringTargetRequest(RegistryStringTargetAction.Delete, "Allow"));

        await Assert.ThrowsAsync<InvalidDataException>(() => capability.PrepareAsync(before, invalid, CancellationToken.None));
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task RequireExistingTarget_PreservesMissingConsentAsNoChange()
    {
        var accessor = new FakeRegistryStringAccessor(new RegistryStringState(false, false, null, null));
        var capability = CreateCapability(RegistryStringCapabilityDefinitions.WebcamConsent, accessor);
        var before = await capability.ReadAsync(RegistryStringCapability.CreateCurrentUserScope(), CancellationToken.None);
        var request = JsonSerializer.Serialize(new RegistryStringTargetRequest(RegistryStringTargetAction.Set, "Allow", true));

        var change = await capability.PrepareAsync(before, request, CancellationToken.None);

        Assert.Equal(before.Payload, change.TargetPayload);
        Assert.Equal(before.Fingerprint, change.TargetFingerprint);
        Assert.Equal(0, accessor.WriteCount);
    }

    private static RegistryStringCapability CreateCapability(
        RegistryStringCapabilityDefinition definition,
        IRegistryStringAccessor accessor) => new(definition, accessor);

    private static RecoveryMaterial CreateRecoveryMaterial(
        RegistryStringCapability capability,
        PreparedCapabilityChange change) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        capability.Descriptor.CapabilityId,
        capability.Descriptor.DefinitionVersion,
        change.Scope,
        change.BeforePayload,
        change.BeforeFingerprint,
        change.TargetFingerprint,
        change.ResourceKey,
        DateTimeOffset.UtcNow,
        "direct-capability-test");

    private sealed class FakeRegistryStringAccessor(RegistryStringState initial) : IRegistryStringAccessor
    {
        public RegistryStringState State { get; private set; } = initial;
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public int RestoreCount { get; private set; }

        public Task<RegistryStringState> ReadCurrentUserAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(State);
        }

        public Task WriteCurrentUserAsync(
            string subKeyPath,
            string valueName,
            string value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            State = new RegistryStringState(true, true, "String", value);
            return Task.CompletedTask;
        }

        public Task RestoreCurrentUserAsync(
            string subKeyPath,
            string valueName,
            RegistryStringState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
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
