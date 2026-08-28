using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class ShowFileExtensionsCapabilityTests
{
    [Fact]
    public async Task ApplyAndRestore_UsesInjectedAccessorAndRestoresMissingValue()
    {
        var accessor = new FakeRegistryAccessor(new RegistryDwordState(true, false, null));
        var capability = new ShowFileExtensionsCapability(accessor);
        var scope = ShowFileExtensionsCapability.CreateCurrentUserScope();
        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        var execution = await capability.ExecuteAsync(change, CancellationToken.None);
        var material = new RecoveryMaterial(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShowFileExtensionsCapability.CapabilityId,
            capability.Descriptor.DefinitionVersion,
            scope,
            before.Payload,
            before.Fingerprint,
            change.TargetFingerprint,
            change.ResourceKey,
            DateTimeOffset.UtcNow,
            "test-only");
        var restore = await capability.RestoreAsync(material, CancellationToken.None);

        Assert.True(execution.Success);
        Assert.True(restore.Success);
        Assert.Equal(new RegistryDwordState(true, false, null), accessor.State);
        Assert.Equal(1, accessor.WriteCount);
        Assert.Equal(1, accessor.RestoreCount);
    }

    [Fact]
    public async Task ProbeAsync_WhenScopeIdentityDoesNotMatchCurrentUser_Blocks()
    {
        var capability = new ShowFileExtensionsCapability(
            new FakeRegistryAccessor(new RegistryDwordState(true, true, 1)));

        var assessment = await capability.ProbeAsync(
            TargetScope.CurrentUser("not-the-current-user"),
            CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
    }

    [Fact]
    public async Task RestoreAsync_WhenValueIsDeclaredButMissing_BlocksCorruptMaterial()
    {
        var accessor = new FakeRegistryAccessor(new RegistryDwordState(true, true, 0));
        var capability = new ShowFileExtensionsCapability(accessor);
        var scope = ShowFileExtensionsCapability.CreateCurrentUserScope();
        var invalidState = JsonSerializer.Serialize(new RegistryDwordState(true, true, null));
        var material = new RecoveryMaterial(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShowFileExtensionsCapability.CapabilityId,
            capability.Descriptor.DefinitionVersion,
            scope,
            invalidState,
            "before",
            "target",
            capability.Descriptor.ResourceKey,
            DateTimeOffset.UtcNow,
            "test-only");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            capability.RestoreAsync(material, CancellationToken.None));
        Assert.Equal(0, accessor.RestoreCount);
    }

    private sealed class FakeRegistryAccessor : IRegistryDwordAccessor
    {
        public FakeRegistryAccessor(RegistryDwordState initialState)
        {
            State = initialState;
        }

        public RegistryDwordState State { get; private set; }
        public int WriteCount { get; private set; }
        public int RestoreCount { get; private set; }

        public Task<RegistryDwordState> ReadCurrentUserAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken) => Task.FromResult(State);

        public Task WriteCurrentUserAsync(
            string subKeyPath,
            string valueName,
            int value,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            State = new RegistryDwordState(true, true, value);
            return Task.CompletedTask;
        }

        public Task RestoreCurrentUserAsync(
            string subKeyPath,
            string valueName,
            RegistryDwordState original,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            State = original;
            return Task.CompletedTask;
        }
    }
}
