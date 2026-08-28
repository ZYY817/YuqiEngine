using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.SystemServices;

namespace GameOptimizer.Tests.Changes;

public sealed class ServiceChangeCapabilityTests
{
    [Theory]
    [InlineData(WindowsServiceRunState.Stopped, WindowsServiceStartType.Manual)]
    [InlineData(WindowsServiceRunState.Stopped, WindowsServiceStartType.Automatic)]
    [InlineData(WindowsServiceRunState.Running, WindowsServiceStartType.AutomaticDelayed)]
    public async Task EnsureRunning_FreezesStartTypeAndVerifiesTarget(
        WindowsServiceRunState runState,
        WindowsServiceStartType startType)
    {
        var accessor = new FakeServiceAccessor(State("Appinfo", runState, startType));
        var capability = Create("cap.appinfo", "Appinfo", accessor);
        var scope = TargetScope.Machine(Environment.MachineName);
        var current = await capability.ReadAsync(scope, CancellationToken.None);
        var prepared = await capability.PrepareAsync(current, "apply", CancellationToken.None);

        var target = JsonSerializer.Deserialize<ServiceStateSnapshot>(prepared.TargetPayload)!;
        Assert.Equal(WindowsServiceRunState.Running, target.RunState);
        Assert.Equal(startType, target.StartType);

        var result = await capability.ExecuteAsync(prepared, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(runState != WindowsServiceRunState.Running, result.MutationOccurred);
        Assert.Equal(target, accessor.Current);
    }

    [Fact]
    public async Task Execute_WhenStateDrifts_IsZeroWrite()
    {
        var accessor = new FakeServiceAccessor(State("Themes", WindowsServiceRunState.Stopped, WindowsServiceStartType.Manual));
        var capability = Create("cap.themes", "Themes", accessor);
        var scope = TargetScope.Machine(Environment.MachineName);
        var current = await capability.ReadAsync(scope, CancellationToken.None);
        var prepared = await capability.PrepareAsync(current, "apply", CancellationToken.None);
        accessor.Current = State("Themes", WindowsServiceRunState.Running, WindowsServiceStartType.Manual);

        var result = await capability.ExecuteAsync(prepared, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MutationOccurred);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.ApplyCount);
    }

    [Fact]
    public async Task Restore_ReinstatesExactRunAndStartType()
    {
        var original = State("Appinfo", WindowsServiceRunState.Stopped, WindowsServiceStartType.Manual);
        var accessor = new FakeServiceAccessor(original);
        var capability = Create("cap.appinfo", "Appinfo", accessor);
        var scope = TargetScope.Machine(Environment.MachineName);
        var current = await capability.ReadAsync(scope, CancellationToken.None);
        var prepared = await capability.PrepareAsync(current, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(prepared, CancellationToken.None);
        Assert.True(applied.Success);
        var material = new RecoveryMaterial(
            Guid.NewGuid(), Guid.NewGuid(), capability.Descriptor.CapabilityId,
            capability.Descriptor.DefinitionVersion, scope, current.Payload, current.Fingerprint,
            prepared.TargetFingerprint, prepared.ResourceKey, DateTimeOffset.UtcNow, "test-integrity");

        var restored = await capability.RestoreAsync(material, CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal(original, accessor.Current);
        Assert.Equal(1, accessor.RestoreCount);
    }

    [Fact]
    public async Task Probe_MapsPolicyAndPermissionWithoutWriting()
    {
        var accessor = new FakeServiceAccessor(State("Themes", WindowsServiceRunState.Running, WindowsServiceStartType.Automatic))
        {
            Assessment = new ServicePlatformAssessment(true, false, true, false, "service.policy-managed", "policy")
        };
        var capability = Create("cap.themes", "Themes", accessor);

        var result = await capability.ProbeAsync(TargetScope.Machine(Environment.MachineName), CancellationToken.None);

        Assert.Equal(CapabilityApplicability.ManagedByPolicy, result.Applicability);
        Assert.False(result.HasRequiredPermission);
        Assert.Equal(0, accessor.ApplyCount);
    }

    [Fact]
    public async Task Probe_CancellationStopsBeforeAccessor()
    {
        var accessor = new FakeServiceAccessor(State("Themes", WindowsServiceRunState.Running, WindowsServiceStartType.Automatic));
        var capability = Create("cap.themes", "Themes", accessor);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            capability.ProbeAsync(TargetScope.Machine(Environment.MachineName), cancellation.Token));

        Assert.Equal(0, accessor.ProbeCount);
    }

    [Fact]
    public async Task Prepare_RejectsCallerSelectedOppositeAction()
    {
        var accessor = new FakeServiceAccessor(State("Appinfo", WindowsServiceRunState.Stopped, WindowsServiceStartType.Manual));
        var capability = Create("cap.appinfo", "Appinfo", accessor);
        var current = await capability.ReadAsync(TargetScope.Machine(Environment.MachineName), CancellationToken.None);
        var spoofed = JsonSerializer.Serialize(new ServiceChangeTarget(
            ServiceChangeAction.EnsureStopped,
            WindowsServiceStartType.Manual));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            capability.PrepareAsync(current, spoofed, CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_RejectsCallerSelectedStartType()
    {
        var accessor = new FakeServiceAccessor(State(
            "Themes", WindowsServiceRunState.Stopped, WindowsServiceStartType.Automatic));
        var capability = new ServiceChangeCapability(
            new ServiceChangeCapabilityDefinition(
                "cap.themes", "1.0.0", "Themes", ServiceChangeAction.EnsureRunning,
                WindowsServiceStartType.Automatic),
            accessor);
        var current = await capability.ReadAsync(
            TargetScope.Machine(Environment.MachineName), CancellationToken.None);
        var spoofed = JsonSerializer.Serialize(new ServiceChangeTarget(
            ServiceChangeAction.EnsureRunning,
            WindowsServiceStartType.Disabled));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            capability.PrepareAsync(current, spoofed, CancellationToken.None));

        Assert.Equal(0, accessor.ApplyCount);
    }

    [Fact]
    public async Task Restore_RejectsRecoveryMaterialForDifferentServiceOwner()
    {
        var accessor = new FakeServiceAccessor(State(
            "Themes", WindowsServiceRunState.Stopped, WindowsServiceStartType.Automatic));
        var capability = Create("cap.themes", "Themes", accessor);
        var scope = TargetScope.Machine(Environment.MachineName);
        var current = await capability.ReadAsync(scope, CancellationToken.None);
        var material = new RecoveryMaterial(
            Guid.NewGuid(), Guid.NewGuid(), "cap.other", "1.0.0", scope,
            current.Payload, current.Fingerprint, current.Fingerprint,
            capability.Descriptor.ResourceKey, DateTimeOffset.UtcNow, "integrity");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            capability.RestoreAsync(material, CancellationToken.None));

        Assert.Equal(0, accessor.RestoreCount);
    }

    [Fact]
    public async Task Execute_PropagatesPartialMutationForDurableRecovery()
    {
        var accessor = new FakeServiceAccessor(State("Appinfo", WindowsServiceRunState.Stopped, WindowsServiceStartType.Manual))
        {
            ApplyResult = new ServicePlatformMutationResult(false, true, "service.partial", "partial")
        };
        var capability = Create("cap.appinfo", "Appinfo", accessor);
        var scope = TargetScope.Machine(Environment.MachineName);
        var current = await capability.ReadAsync(scope, CancellationToken.None);
        var prepared = await capability.PrepareAsync(current, "apply", CancellationToken.None);

        var result = await capability.ExecuteAsync(prepared, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.MutationOccurred);
        Assert.Equal("service.partial", result.ReasonCode);
    }

    [Fact]
    public async Task Read_PausedServiceFailsClosedBecauseExactRestoreIsUnsupported()
    {
        var accessor = new FakeServiceAccessor(
            State("Themes", WindowsServiceRunState.Paused, WindowsServiceStartType.Automatic));
        var capability = Create("cap.themes", "Themes", accessor);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            capability.ReadAsync(TargetScope.Machine(Environment.MachineName), CancellationToken.None));

        Assert.Equal(0, accessor.ApplyCount);
        Assert.Equal(0, accessor.RestoreCount);
    }

    [Fact]
    public void Constructor_RejectsRunningDisabledFixedTarget()
    {
        var accessor = new FakeServiceAccessor(
            State("Themes", WindowsServiceRunState.Stopped, WindowsServiceStartType.Manual));

        Assert.Throws<ArgumentException>(() => new ServiceChangeCapability(
            new ServiceChangeCapabilityDefinition(
                "cap.themes", "1.0.0", "Themes", ServiceChangeAction.EnsureRunning,
                WindowsServiceStartType.Disabled),
            accessor));
    }

    private static ServiceChangeCapability Create(
        string capabilityId,
        string serviceName,
        IWindowsServiceAccessor accessor) =>
        new(new ServiceChangeCapabilityDefinition(
            capabilityId, "1.0.0", serviceName, ServiceChangeAction.EnsureRunning), accessor);

    private static ServiceStateSnapshot State(
        string serviceName,
        WindowsServiceRunState runState,
        WindowsServiceStartType startType) =>
        new(serviceName, true, runState, startType);

    private sealed class FakeServiceAccessor(ServiceStateSnapshot initial) : IWindowsServiceAccessor
    {
        public ServiceStateSnapshot Current { get; set; } = initial;
        public ServicePlatformAssessment Assessment { get; set; } =
            new(true, true, false, false, "applicable", "test");
        public ServicePlatformMutationResult? ApplyResult { get; set; }
        public int ProbeCount { get; private set; }
        public int ApplyCount { get; private set; }
        public int RestoreCount { get; private set; }

        public Task<ServicePlatformAssessment> ProbeAsync(string serviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCount++;
            return Task.FromResult(Assessment);
        }

        public Task<ServiceStateSnapshot> ReadAsync(string serviceName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Current);
        }

        public Task<ServicePlatformMutationResult> ApplyAsync(
            string serviceName,
            ServiceChangeTarget target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCount++;
            if (ApplyResult is not null)
                return Task.FromResult(ApplyResult);
            var desired = target.Action == ServiceChangeAction.EnsureRunning
                ? WindowsServiceRunState.Running
                : WindowsServiceRunState.Stopped;
            var changed = Current.RunState != desired || Current.StartType != target.StartType;
            Current = Current with { RunState = desired, StartType = target.StartType };
            return Task.FromResult(new ServicePlatformMutationResult(true, changed, "ok", "ok"));
        }

        public Task<ServicePlatformMutationResult> RestoreAsync(
            ServiceStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCount++;
            var changed = Current != snapshot;
            Current = snapshot;
            return Task.FromResult(new ServicePlatformMutationResult(true, changed, "ok", "ok"));
        }
    }
}
