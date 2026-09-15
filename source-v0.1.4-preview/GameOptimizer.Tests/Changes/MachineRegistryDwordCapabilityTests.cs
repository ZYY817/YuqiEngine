using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class MachineRegistryDwordCapabilityTests
{
    [Fact]
    public async Task MachineCapability_UsesMachineScopeAndFakeHklmApplyRestore()
    {
        var accessor = new FakeRegistryAccessor(new RegistryDwordState(true, true, 1));
        var capability = new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "test.machine",
                "1.0.0",
                @"SOFTWARE\Test",
                "Enabled",
                0,
                ChangeRisk.MediumRebootOrExperience,
                RequiresExplicitConfirmation: true,
                Hive: RegistryDwordHive.LocalMachine),
            accessor);
        var scope = TargetScope.Machine(Environment.MachineName);

        Assert.True(capability.Descriptor.RequiresAdministrator);
        Assert.Contains(TargetScopeKind.Machine, capability.Descriptor.SupportedScopes);
        Assert.StartsWith("registry:HKLM/", capability.Descriptor.ResourceKey);

        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.Equal(0, accessor.LocalMachineState.Value);
        Assert.Equal(1, accessor.LocalMachineWriteCount);

        var restored = await capability.RestoreAsync(
            new RecoveryMaterial(
                Guid.NewGuid(),
                Guid.NewGuid(),
                capability.Descriptor.CapabilityId,
                capability.Descriptor.DefinitionVersion,
                scope,
                change.BeforePayload,
                change.BeforeFingerprint,
                change.TargetFingerprint,
                change.ResourceKey,
                DateTimeOffset.UtcNow,
                "not-used-by-direct-capability-test"),
            CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal(1, accessor.LocalMachineState.Value);
        Assert.Equal(1, accessor.LocalMachineRestoreCount);
    }

    [Fact]
    public async Task MachineCapability_RejectsCurrentUserScope()
    {
        var capability = new RegistryDwordCapability(
            new RegistryDwordCapabilityDefinition(
                "test.machine",
                "1.0.0",
                @"SOFTWARE\Test",
                "Enabled",
                0,
                ChangeRisk.MediumRebootOrExperience,
                Hive: RegistryDwordHive.LocalMachine),
            new FakeRegistryAccessor(new RegistryDwordState(true, true, 1)));

        var assessment = await capability.ProbeAsync(
            TargetScope.CurrentUser("wrong-scope"),
            CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
    }

    [Fact]
    public async Task SixthBatchDefinitions_FreezeTypedTargetsAndRestoreMissingOriginalExactly()
    {
        var definitions = new[]
        {
            RepairRegistryCapabilityDefinitions.HagsEnable,
            RepairRegistryCapabilityDefinitions.DiagnosticDataReset,
            RepairRegistryCapabilityDefinitions.FastStartupEnable
        };

        foreach (var definition in definitions)
        {
            var accessor = new FakeRegistryAccessor(new RegistryDwordState(true, false, null));
            var capability = new RegistryDwordCapability(definition, accessor);
            var scope = TargetScope.Machine(Environment.MachineName);
            var before = await capability.ReadAsync(scope, CancellationToken.None);
            var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

            var applied = await capability.ExecuteAsync(change, CancellationToken.None);

            Assert.True(applied.Success);
            Assert.Equal(definition.TargetValue, accessor.LocalMachineState.Value);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);

            var restored = await capability.RestoreAsync(
                new RecoveryMaterial(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    definition.CapabilityId,
                    definition.DefinitionVersion,
                    scope,
                    change.BeforePayload,
                    change.BeforeFingerprint,
                    change.TargetFingerprint,
                    change.ResourceKey,
                    DateTimeOffset.UtcNow,
                    "test"),
                CancellationToken.None);

            Assert.True(restored.Success);
            Assert.False(accessor.LocalMachineState.ValueExists);
        }
    }

    private sealed class FakeRegistryAccessor(RegistryDwordState initial) : IRegistryDwordAccessor
    {
        public RegistryDwordState LocalMachineState { get; private set; } = initial;
        public int LocalMachineWriteCount { get; private set; }
        public int LocalMachineRestoreCount { get; private set; }

        public Task<RegistryDwordState> ReadCurrentUserAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HKCU must not be used by a machine capability.");

        public Task WriteCurrentUserAsync(
            string subKeyPath,
            string valueName,
            int value,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HKCU must not be used by a machine capability.");

        public Task RestoreCurrentUserAsync(
            string subKeyPath,
            string valueName,
            RegistryDwordState original,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HKCU must not be used by a machine capability.");

        public Task<RegistryDwordState> ReadLocalMachineAsync(
            string subKeyPath,
            string valueName,
            CancellationToken cancellationToken) =>
            Task.FromResult(LocalMachineState);

        public Task WriteLocalMachineAsync(
            string subKeyPath,
            string valueName,
            int value,
            CancellationToken cancellationToken)
        {
            LocalMachineWriteCount++;
            LocalMachineState = new RegistryDwordState(true, true, value);
            return Task.CompletedTask;
        }

        public Task RestoreLocalMachineAsync(
            string subKeyPath,
            string valueName,
            RegistryDwordState original,
            CancellationToken cancellationToken)
        {
            LocalMachineRestoreCount++;
            LocalMachineState = original;
            return Task.CompletedTask;
        }
    }
}
