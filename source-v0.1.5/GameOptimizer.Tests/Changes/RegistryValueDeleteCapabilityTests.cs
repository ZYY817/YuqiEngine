using System.Security.Principal;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class RegistryValueDeleteCapabilityTests
{
    [Fact]
    public async Task DeleteAndRestore_RoundTripsTypedValues()
    {
        var accessor = new FakeAccessor(new Dictionary<string, RegistryValueSetEntryState>
        {
            ["LogPixels"] = Dword("LogPixels", 144),
            ["Win8DpiScaling"] = Dword("Win8DpiScaling", 1)
        });
        var capability = new RegistryValueDeleteCapability(
            RepairRegistryCapabilityDefinitions.DpiReset,
            accessor,
            TimeProvider.System);
        var scope = CurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        var execute = await capability.ExecuteAsync(change, CancellationToken.None);
        var deleted = await capability.ReadAsync(scope, CancellationToken.None);
        var restore = await capability.RestoreAsync(
            Material(change),
            CancellationToken.None);
        var afterRestore = await capability.ReadAsync(scope, CancellationToken.None);

        Assert.True(execute.Success);
        Assert.Equal(change.TargetFingerprint, deleted.Fingerprint);
        Assert.True(restore.Success);
        Assert.Equal(before.Fingerprint, afterRestore.Fingerprint);
    }

    [Fact]
    public async Task DriftBeforeDelete_IsZeroWrite()
    {
        var accessor = new FakeAccessor(new Dictionary<string, RegistryValueSetEntryState>
        {
            ["ForceVSync"] = Dword("ForceVSync", 1)
        });
        var capability = new RegistryValueDeleteCapability(
            RepairRegistryCapabilityDefinitions.VsyncReset,
            accessor,
            TimeProvider.System);
        var before = await capability.ReadAsync(CurrentUserScope(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.Set(Dword("ForceVSync", 0));

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task CancelDuringSecondDelete_CompensatesFirstValue()
    {
        var initial = new Dictionary<string, RegistryValueSetEntryState>
        {
            ["LogPixels"] = Dword("LogPixels", 144),
            ["Win8DpiScaling"] = Dword("Win8DpiScaling", 1)
        };
        var accessor = new FakeAccessor(initial) { ThrowOnWriteNumber = 2 };
        var capability = new RegistryValueDeleteCapability(
            RepairRegistryCapabilityDefinitions.DpiReset,
            accessor,
            TimeProvider.System);
        var before = await capability.ReadAsync(CurrentUserScope(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            capability.ExecuteAsync(change, CancellationToken.None));
        var after = await capability.ReadAsync(CurrentUserScope(), CancellationToken.None);

        Assert.Equal(before.Fingerprint, after.Fingerprint);
    }

    private static TargetScope CurrentUserScope()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return TargetScope.CurrentUser(identity.User!.Value);
    }

    private static RegistryValueSetEntryState Dword(string name, int value) =>
        new(name, true, true, "DWord", null, value);

    private static RecoveryMaterial Material(PreparedCapabilityChange change) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        change.CapabilityId,
        change.DefinitionVersion,
        change.Scope,
        change.BeforePayload,
        change.BeforeFingerprint,
        change.TargetFingerprint,
        change.ResourceKey,
        DateTimeOffset.UtcNow,
        "test-integrity");

    private sealed class FakeAccessor(Dictionary<string, RegistryValueSetEntryState> initial) : IRegistryValueSetAccessor
    {
        private readonly Dictionary<string, RegistryValueSetEntryState> _values =
            new(initial, StringComparer.OrdinalIgnoreCase);

        public int WriteCount { get; private set; }
        public int? ThrowOnWriteNumber { get; init; }

        public void Set(RegistryValueSetEntryState value) => _values[value.ValueName] = value;

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadCurrentUserAsync(
            string subKeyPath,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<RegistryValueSetEntryState>>(valueNames.Select(name =>
                _values.TryGetValue(name, out var state)
                    ? state
                    : new RegistryValueSetEntryState(name, true, false, null, null, null)).ToArray());
        }

        public Task WriteCurrentUserValueAsync(
            string subKeyPath,
            RegistryTypedTargetValue target,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreCurrentUserValueAsync(
            string subKeyPath,
            RegistryValueSetEntryState original,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteCount++;
            if (ThrowOnWriteNumber == WriteCount)
            {
                throw new OperationCanceledException("Injected cancellation.");
            }

            _values[original.ValueName] = original;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RegistryValueSetEntryState>> ReadLocalMachineAsync(
            string subKeyPath,
            IReadOnlyList<string> valueNames,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task WriteLocalMachineValueAsync(
            string subKeyPath,
            RegistryTypedTargetValue target,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RestoreLocalMachineValueAsync(
            string subKeyPath,
            RegistryValueSetEntryState original,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
