using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class PerInterfaceRegistryCapabilityTests
{
    [Fact]
    public async Task Execute_AppliesTargetsToEveryEnumeratedSubKeyAndVerifies()
    {
        var accessor = new FakePerInterfaceAccessor(
            ("{iface-a}", 0, null),
            ("{iface-b}", 0, null));
        var capability = CreateCapability(PerInterfaceRegistryCapabilityDefinitions.DisableNetBios, accessor);
        var scope = PerInterfaceRegistryCapability.CreateMachineScope();

        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal(2, accessor.Get("{iface-a}", "NetbiosOptions"));
        Assert.Equal(2, accessor.Get("{iface-b}", "NetbiosOptions"));
        Assert.Equal(2, accessor.WriteCount);
    }

    [Fact]
    public async Task Prepare_WhenAllAlreadyMatch_ProducesNoChangeFingerprint()
    {
        var accessor = new FakePerInterfaceAccessor(
            ("{iface-a}", 2, null),
            ("{iface-b}", 2, null));
        var capability = CreateCapability(PerInterfaceRegistryCapabilityDefinitions.DisableNetBios, accessor);
        var before = await capability.ReadAsync(PerInterfaceRegistryCapability.CreateMachineScope(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        Assert.Equal(change.BeforeFingerprint, change.TargetFingerprint);
        var result = await capability.ExecuteAsync(change, CancellationToken.None);
        Assert.True(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task Restore_RevertsToOriginalValuesAndVerifies()
    {
        var accessor = new FakePerInterfaceAccessor(
            ("{iface-a}", 0, null),
            ("{iface-b}", 1, null));
        var capability = CreateCapability(PerInterfaceRegistryCapabilityDefinitions.DisableNetBios, accessor);
        var scope = PerInterfaceRegistryCapability.CreateMachineScope();

        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var execute = await capability.ExecuteAsync(change, CancellationToken.None);
        Assert.True(execute.Success);

        var material = new RecoveryMaterial(
            Guid.NewGuid(),
            Guid.NewGuid(),
            capability.Descriptor.CapabilityId,
            capability.Descriptor.DefinitionVersion,
            scope,
            before.Payload,
            before.Fingerprint,
            change.TargetFingerprint,
            capability.Descriptor.ResourceKey,
            DateTimeOffset.UtcNow,
            "hash");

        var restore = await capability.RestoreAsync(material, CancellationToken.None);
        Assert.True(restore.Success);
        Assert.True(restore.MutationOccurred);
        Assert.Equal(0, accessor.Get("{iface-a}", "NetbiosOptions"));
        Assert.Equal(1, accessor.Get("{iface-b}", "NetbiosOptions"));
    }

    [Fact]
    public async Task Execute_WhenSubKeySetDrifts_RefusesOldPlan()
    {
        var accessor = new FakePerInterfaceAccessor(
            ("{iface-a}", 0, null));
        var capability = CreateCapability(PerInterfaceRegistryCapabilityDefinitions.DisableNetBios, accessor);
        var scope = PerInterfaceRegistryCapability.CreateMachineScope();

        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        accessor.AddSubKey("{iface-b}", 0);

        var result = await capability.ExecuteAsync(change, CancellationToken.None);
        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("state.drift", result.ReasonCode);
    }

    [Fact]
    public async Task Prepare_RejectsUnsupportedObservedKind()
    {
        var accessor = new FakePerInterfaceAccessor(
            ("{iface-a}", 0, "QWord"));
        var capability = CreateCapability(PerInterfaceRegistryCapabilityDefinitions.DisableNetBios, accessor);
        var before = await capability.ReadAsync(PerInterfaceRegistryCapability.CreateMachineScope(), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.PrepareAsync(before, "apply", CancellationToken.None));
    }

    private static PerInterfaceRegistryCapability CreateCapability(
        PerInterfaceRegistryCapabilityDefinition definition,
        IPerInterfaceRegistryAccessor accessor) =>
        new(definition, accessor, TimeProvider.System);

    private sealed class FakePerInterfaceAccessor : IPerInterfaceRegistryAccessor
    {
        private readonly Dictionary<(string SubKey, string Value), PerInterfaceValueState> _state = new();
        private readonly List<string> _subKeys = [];

        public int WriteCount { get; private set; }

        public FakePerInterfaceAccessor(params (string SubKey, int Dword, string? Kind)[] entries)
        {
            foreach (var (subKey, dword, kind) in entries)
            {
                AddSubKey(subKey, dword, kind);
            }
        }
        public void AddSubKey(string subKey, int dword, string? kind = null)
        {
            if (!_subKeys.Contains(subKey, StringComparer.OrdinalIgnoreCase))
            {
                _subKeys.Add(subKey);
            }

            var resolvedKind = kind ?? "DWord";
            var isDword = string.Equals(resolvedKind, "DWord", StringComparison.Ordinal);
            _state[(subKey, "NetbiosOptions")] = new PerInterfaceValueState(
                subKey, "NetbiosOptions", true, resolvedKind, null, isDword ? dword : null);
        }

        public int? Get(string subKey, string value) => _state[(subKey, value)].DwordValue;

        public Task<IReadOnlyList<string>> EnumerateSubKeysAsync(string rootPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<string>>(
                _subKeys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        public Task<PerInterfaceValueState> ReadValueAsync(
            string rootPath,
            string subKeyName,
            string valueName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_state.TryGetValue((subKeyName, valueName), out var state)
                ? state
                : new PerInterfaceValueState(subKeyName, valueName, false, null, null, null));
        }

        public Task WriteValueAsync(
            string rootPath,
            string subKeyName,
            string valueName,
            PerInterfaceScalarValueKind kind,
            string? stringValue,
            int? dwordValue,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            _state[(subKeyName, valueName)] = new PerInterfaceValueState(
                subKeyName,
                valueName,
                true,
                kind == PerInterfaceScalarValueKind.String ? "String" : "DWord",
                stringValue,
                dwordValue);
            return Task.CompletedTask;
        }

        public Task RestoreValueAsync(
            string rootPath,
            string subKeyName,
            PerInterfaceValueState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state[(subKeyName, original.ValueName)] = original;
            return Task.CompletedTask;
        }
    }
}
