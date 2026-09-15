using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.DeviceRepair;

namespace GameOptimizer.Tests.Changes;

public sealed class DeviceStateChangeCapabilityTests
{
    private static readonly TargetScope MachineScope = TargetScope.Machine(Environment.MachineName);

    private static DeviceStateChangeCapability CreateCapability(FakeDeviceStateAccessor accessor) =>
        new(
            new DeviceStateChangeCapabilityDefinition(
                "device.disable_umbus",
                "1.0.0",
                "UMBus Root Bus Enumerator"),
            accessor);

    private static RecoveryMaterial CreateMaterial(DeviceStateChangeCapability capability, PreparedCapabilityChange change) =>
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
        var accessor = new FakeDeviceStateAccessor(enabled: true);
        var capability = CreateCapability(accessor);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.False(accessor.Device.IsEnabled);

        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);

        Assert.True(restored.Success);
        Assert.True(accessor.Device.IsEnabled);
    }

    [Fact]
    public async Task Probe_DeviceMissingIsNotApplicable()
    {
        var accessor = new FakeDeviceStateAccessor(enabled: true) { HasDevice = false };
        var capability = CreateCapability(accessor);

        var assessment = await capability.ProbeAsync(MachineScope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.Equal("device.not-found", assessment.ReasonCode);
    }

    [Fact]
    public async Task Probe_MissingAdministratorKeepsPermissionGate()
    {
        var accessor = new FakeDeviceStateAccessor(enabled: true) { IsAdministrator = false };
        var capability = CreateCapability(accessor);

        var assessment = await capability.ProbeAsync(MachineScope, CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Applicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
    }

    [Fact]
    public async Task ReadAndPrepare_DeviceMissingIsFailClosed()
    {
        var accessor = new FakeDeviceStateAccessor(enabled: true) { HasDevice = false };
        var capability = CreateCapability(accessor);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.ReadAsync(MachineScope, CancellationToken.None));
    }

    [Fact]
    public async Task Execute_DriftBetweenPrepareAndWriteIsRejectedWithoutMutation()
    {
        var accessor = new FakeDeviceStateAccessor(enabled: true);
        var capability = CreateCapability(accessor);

        var before = await capability.ReadAsync(MachineScope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.SetDevice(enabled: false);

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.ApplyCount);
    }

    private sealed class FakeDeviceStateAccessor(bool enabled) : IDeviceStateAccessor
    {
        public bool IsAdministrator { get; set; } = true;
        public bool HasDevice { get; set; } = true;
        public int ApplyCount { get; private set; }

        public DeviceStateSnapshot Device { get; private set; } =
            new(true, @"UMB\UMB\0000", "UMBus Root Bus Enumerator", enabled);

        public void SetDevice(bool enabled) =>
            Device = Device with { IsEnabled = enabled };

        public Task<DevicePlatformAssessment> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DevicePlatformAssessment(
                IsAdministrator,
                IsAdministrator ? "applicable" : "permission.administrator-required",
                "fake"));

        public Task<DeviceStateSnapshot> ReadAsync(string deviceNamePattern, CancellationToken cancellationToken) =>
            Task.FromResult(HasDevice
                ? Device
                : new DeviceStateSnapshot(false, string.Empty, string.Empty, true));

        public async Task<DeviceMutationResult> ApplyAsync(string deviceId, bool enabled, CancellationToken cancellationToken)
        {
            ApplyCount++;
            if (deviceId != Device.DeviceId)
                return new DeviceMutationResult(false, false, "device.id-invalid", "fake id mismatch");

            SetDevice(enabled);
            return new DeviceMutationResult(true, true, "ok", "fake");
        }

        public async Task<DeviceMutationResult> RestoreAsync(DeviceStateSnapshot original, CancellationToken cancellationToken)
        {
            if (current().Found && current().DeviceId == original.DeviceId && current().IsEnabled == original.IsEnabled)
                return new DeviceMutationResult(true, false, "no-change", "already original");
            return await ApplyAsync(original.DeviceId, original.IsEnabled, cancellationToken);

            DeviceStateSnapshot current() => HasDevice ? Device : new DeviceStateSnapshot(false, "", "", true);
        }
    }
}
