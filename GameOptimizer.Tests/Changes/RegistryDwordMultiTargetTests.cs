using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryDwordMultiTargetTests
{
    [Fact]
    public async Task DynamicSet_IsFrozenExecutedAndExactlyRestored()
    {
        var accessor = new FakeAccessor(new RegistryDwordState(true, true, 0));
        var capability = Create(accessor);
        var before = await capability.ReadAsync(RegistryDwordCapability.CreateCurrentUserScope(), CancellationToken.None);
        var request = JsonSerializer.Serialize(RegistryDwordTargetRequest.Set(1));

        var change = await capability.PrepareAsync(before, request, CancellationToken.None);
        var result = await capability.ExecuteAsync(change, CancellationToken.None);
        var restored = await capability.RestoreAsync(Material(capability, change), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(restored.Success);
        Assert.Equal(0, accessor.State.Value);
    }

    [Fact]
    public async Task ExplicitDelete_IsDistinctFromSet_AndLegacyApplyRemainsCompatible()
    {
        var accessor = new FakeAccessor(new RegistryDwordState(true, true, 9));
        var capability = Create(accessor);
        var before = await capability.ReadAsync(RegistryDwordCapability.CreateCurrentUserScope(), CancellationToken.None);

        var set = await capability.PrepareAsync(before, JsonSerializer.Serialize(RegistryDwordTargetRequest.Set(1)), CancellationToken.None);
        var delete = await capability.PrepareAsync(before, JsonSerializer.Serialize(RegistryDwordTargetRequest.Delete()), CancellationToken.None);
        var legacy = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        Assert.NotEqual(set.TargetFingerprint, delete.TargetFingerprint);
        Assert.Equal(set.TargetFingerprint, legacy.TargetFingerprint);
        Assert.True((await capability.ExecuteAsync(delete, CancellationToken.None)).Success);
        Assert.False(accessor.State.ValueExists);
    }

    [Fact]
    public async Task InconsistentActionPayload_IsRejectedBeforeWrite()
    {
        var accessor = new FakeAccessor(new RegistryDwordState(true, true, 0));
        var capability = Create(accessor);
        var before = await capability.ReadAsync(RegistryDwordCapability.CreateCurrentUserScope(), CancellationToken.None);
        var invalid = JsonSerializer.Serialize(new RegistryDwordTargetRequest(RegistryDwordTargetAction.Delete, 1));

        await Assert.ThrowsAsync<InvalidDataException>(() => capability.PrepareAsync(before, invalid, CancellationToken.None));
        Assert.Equal(0, accessor.WriteCount);
    }

    private static RegistryDwordCapability Create(IRegistryDwordAccessor accessor) => new(
        new RegistryDwordCapabilityDefinition("test.dynamic-dword", "1.0.0", @"Software\Tests", "Value", 1, ChangeRisk.LowReversible),
        accessor);

    private static RecoveryMaterial Material(RegistryDwordCapability capability, PreparedCapabilityChange change) => new(
        Guid.NewGuid(), Guid.NewGuid(), capability.Descriptor.CapabilityId, capability.Descriptor.DefinitionVersion,
        change.Scope, change.BeforePayload, change.BeforeFingerprint, change.TargetFingerprint, change.ResourceKey,
        DateTimeOffset.UtcNow, "test");

    private sealed class FakeAccessor(RegistryDwordState initial) : IRegistryDwordAccessor
    {
        public RegistryDwordState State { get; private set; } = initial;
        public int WriteCount { get; private set; }
        public Task<RegistryDwordState> ReadCurrentUserAsync(string subKeyPath, string valueName, CancellationToken cancellationToken) => Task.FromResult(State);
        public Task WriteCurrentUserAsync(string subKeyPath, string valueName, int value, CancellationToken cancellationToken) { WriteCount++; State = new(true, true, value); return Task.CompletedTask; }
        public Task RestoreCurrentUserAsync(string subKeyPath, string valueName, RegistryDwordState original, CancellationToken cancellationToken) { State = original; return Task.CompletedTask; }
        public Task<RegistryDwordState> ReadLocalMachineAsync(string subKeyPath, string valueName, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task WriteLocalMachineAsync(string subKeyPath, string valueName, int value, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task RestoreLocalMachineAsync(string subKeyPath, string valueName, RegistryDwordState original, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
