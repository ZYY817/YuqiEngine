using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Memory;

namespace GameOptimizer.Tests.Changes;

public sealed class MmAgentChangeCapabilityTests
{
    private static readonly TargetScope MachineScope = TargetScope.Machine(Environment.MachineName);

    private static MmAgentChangeCapability CreateCapability(FakeMmAgentAccessor accessor) =>
        new(
            new MmAgentChangeCapabilityDefinition("memory.disable_compression", "1.0.0"),
            accessor);

    private static RecoveryMaterial CreateMaterial(MmAgentChangeCapability capability, PreparedCapabilityChange change) =>
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

    [Fact]
    public async Task ApplyAndRestore_RoundTripsExactOriginalState()
    {
        var accessor = new FakeMmAgentAccessor(enabled: true);
        var capability = CreateCapability(accessor);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        Assert.True(before.Fingerprint.Length > 0);

        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.True(applied.MutationOccurred);
        Assert.False(accessor.MemoryCompressionEnabled);

        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.True(accessor.MemoryCompressionEnabled);
    }

    [Fact]
    public async Task Execute_DriftBetweenPrepareAndWriteIsRejectedWithoutMutation()
    {
        var accessor = new FakeMmAgentAccessor(enabled: true);
        var capability = CreateCapability(accessor);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.SetState(enabled: false);

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.False(result.MutationOccurred);
        Assert.Equal(0, accessor.ApplyCount);
    }

    [Fact]
    public async Task Apply_NoChangeWhenAlreadyAtTarget()
    {
        var accessor = new FakeMmAgentAccessor(enabled: false);
        var result = await accessor.ApplyAsync(memoryCompressionEnabled: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("no-change", result.ReasonCode);
    }

    [Fact]
    public async Task Prepare_NonApplyPayloadIsRejected()
    {
        var accessor = new FakeMmAgentAccessor(enabled: true);
        var capability = CreateCapability(accessor);
        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(before, "{\"custom\":true}", CancellationToken.None));
    }

    [Fact]
    public void Descriptor_RequiresMachineScopeAdministratorAndExplicitConfirmation()
    {
        var capability = CreateCapability(new FakeMmAgentAccessor(enabled: true));

        Assert.Equal("memory.disable_compression", capability.Descriptor.CapabilityId);
        Assert.True(capability.Descriptor.RequiresAdministrator);
        Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
        Assert.True(capability.Descriptor.RequiresRecoveryMaterial);
        Assert.True(capability.Descriptor.RequiresRestart);
        Assert.Single(capability.Descriptor.SupportedScopes, TargetScopeKind.Machine);
    }

    private sealed class FakeMmAgentAccessor(bool enabled) : IMmAgentAccessor
    {
        public bool MemoryCompressionEnabled { get; private set; } = enabled;
        public int ReadCount { get; private set; }
        public int ApplyCount { get; private set; }

        public void SetState(bool enabled) => MemoryCompressionEnabled = enabled;

        public Task<MmAgentPlatformAssessment> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new MmAgentPlatformAssessment(true, "applicable", "fake"));

        public Task<MmAgentStateSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(new MmAgentStateSnapshot(MemoryCompressionEnabled));
        }

        public async Task<MmAgentMutationResult> ApplyAsync(bool memoryCompressionEnabled, CancellationToken cancellationToken)
        {
            ApplyCount++;
            var before = await ReadAsync(cancellationToken);
            if (before.MemoryCompressionEnabled == memoryCompressionEnabled)
                return new MmAgentMutationResult(true, false, "no-change", "already at target");

            MemoryCompressionEnabled = memoryCompressionEnabled;
            return new MmAgentMutationResult(true, true, "ok", "switched");
        }

        public Task<MmAgentMutationResult> RestoreAsync(MmAgentStateSnapshot original, CancellationToken cancellationToken) =>
            ApplyAsync(original.MemoryCompressionEnabled, cancellationToken);
    }
}
