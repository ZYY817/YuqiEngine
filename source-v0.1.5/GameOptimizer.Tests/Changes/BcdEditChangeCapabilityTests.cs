using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Bcd;

namespace GameOptimizer.Tests.Changes;

public sealed class BcdEditChangeCapabilityTests
{
    private static readonly TargetScope MachineScope = TargetScope.Machine(Environment.MachineName);

    [Fact]
    public async Task ApplyAndRestore_RoundTripsExactOriginalValue()
    {
        var accessor = new FakeBcdEditAccessor("Yes");
        var capability = CreateCapability(accessor);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.Equal("Yes", accessor.ElementValue);

        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal("Yes", accessor.ElementValue);
    }

    [Fact]
    public async Task RestoreOfOriginallyUnsetElement_DeletesTheValue()
    {
        var accessor = new FakeBcdEditAccessor(originalValue: null);
        var capability = new BcdEditChangeCapability(
            new BcdEditChangeCapabilityDefinition(
                "cpu.configure_hpet",
                "1.0.0",
                "useplatformclock",
                TargetValue: null,
                "未绑定 HPET"),
            accessor);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        Assert.Null(
            System.Text.Json.JsonSerializer.Deserialize<BcdStateSnapshot>(change.TargetPayload)!.ElementValue);

        var applied = await capability.ExecuteAsync(change, CancellationToken.None);
        Assert.True(applied.Success);
        Assert.Null(accessor.ElementValue);

        // Simulate an external drift back to a set value before restore.
        accessor.ElementValue = "Yes";
        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.True(accessor.DeleteCalled);
        Assert.Null(accessor.ElementValue);
    }

    [Fact]
    public async Task Probe_SecureBootEnabledIsUnsupported()
    {
        var accessor = new FakeBcdEditAccessor("Yes") { SecureBootEnabled = true };
        var capability = CreateCapability(accessor);

        var assessment = await capability.ProbeAsync(MachineScope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Unsupported, assessment.Applicability);
        Assert.Equal("secureboot.enabled", assessment.ReasonCode);
    }

    [Fact]
    public async Task Execute_DriftBetweenPrepareAndWriteIsRejectedWithoutMutation()
    {
        var accessor = new FakeBcdEditAccessor(originalValue: null);
        var capability = CreateCapability(accessor);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.ElementValue = "No";

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.False(result.MutationOccurred);
        Assert.Equal(0, accessor.ApplyCount);
    }

    [Fact]
    public void Descriptor_RequiresMachineScopeAdministratorExplicitConfirmationAndRestart()
    {
        var capability = CreateCapability(new FakeBcdEditAccessor(null));

        Assert.Equal(ChangeRisk.HighSystem, capability.Descriptor.Risk);
        Assert.StartsWith("bcd:", capability.Descriptor.ResourceKey, StringComparison.Ordinal);
        Assert.True(capability.Descriptor.RequiresAdministrator);
        Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
        Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
        Assert.True(capability.Descriptor.RequiresRestart);
    }

    private static BcdEditChangeCapability CreateCapability(FakeBcdEditAccessor accessor) =>
        new(
            new BcdEditChangeCapabilityDefinition(
                "cpu.disable_dynamic_tick",
                "1.0.0",
                "disabledynamictick",
                "Yes",
                "Yes"),
            accessor);

    private static RecoveryMaterial CreateMaterial(BcdEditChangeCapability capability, PreparedCapabilityChange change) =>
        new(
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
            "test");

    private sealed class FakeBcdEditAccessor(string? originalValue) : IBcdEditAccessor
    {
        public string? ElementValue { get; set; } = originalValue;
        public bool SecureBootEnabled { get; set; }
        public int ApplyCount { get; private set; }
        public bool DeleteCalled { get; private set; }

        public Task<BcdPlatformAssessment> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BcdPlatformAssessment(
                true,
                SecureBootEnabled,
                true,
                SecureBootEnabled ? "secureboot.enabled" : "applicable",
                "fake"));

        public Task<BcdStateSnapshot> ReadAsync(string elementName, CancellationToken cancellationToken) =>
            Task.FromResult(new BcdStateSnapshot(ReadSucceeded: true, ElementValue));

        public Task<BcdMutationResult> ApplyAsync(string elementName, string? targetValue, CancellationToken cancellationToken)
        {
            ApplyCount++;
            if (targetValue is null)
            {
                DeleteCalled = true;
                ElementValue = null;
            }
            else
            {
                ElementValue = targetValue;
            }

            return Task.FromResult(new BcdMutationResult(true, true, "ok", "fake"));
        }

        public async Task<BcdMutationResult> RestoreAsync(string elementName, BcdStateSnapshot original, CancellationToken cancellationToken)
        {
            var current = await ReadAsync(elementName, cancellationToken);
            if (AreEquivalent(current.ElementValue, original.ElementValue))
                return new BcdMutationResult(true, false, "no-change", "already original");

            return await ApplyAsync(elementName, original.ElementValue, cancellationToken);
        }
    }

    private static bool AreEquivalent(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
