using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class OptimizationProviderRuntimeManifestTests
{
    [Fact]
    public void RuntimeManifest_ClassifiesEveryCatalogIdOnceWithoutSourceCountInference()
    {
        var entries = OptimizationProviderRuntimeManifest.Create(new BuiltInOptimizationOperationProvider());
        var typed = entries.Count(entry => entry.Route == OptimizationOperationRoute.TypedExecutable);
        var failClosed = entries.Count(entry => entry.Route == OptimizationOperationRoute.ManagedFailClosed);

        Console.WriteLine($"OptimizationProvider runtime manifest: typed={typed}, managedFailClosed={failClosed}, total={entries.Count}.");
        foreach (var entry in entries)
        {
            Console.WriteLine($"{entry.ModuleKey}|{entry.OperationId}|{entry.Route}|{entry.RuntimeType}");
        }
        Assert.Equal(212, entries.Count);
        Assert.Equal(212, entries.Select(entry => entry.OperationId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(entries, entry => entry.Route == OptimizationOperationRoute.Unknown);
        Assert.Equal(207, typed);
        Assert.Equal(5, failClosed);
        Assert.Equal(212, typed + failClosed);

        foreach (var definition in OptimizationRegistryDwordCapabilityDefinitions.CurrentBatch)
        {
            var entry = Assert.Single(entries, candidate => candidate.OperationId == definition.CapabilityId);
            Assert.Equal(OptimizationOperationRoute.TypedExecutable, entry.Route);
            Assert.Contains(nameof(OrchestratedRegistryDwordOperation), entry.RuntimeType, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CurrentBatch_RegistersExactlyOneMachineCapabilityForEachTypedOperation()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddGameOptimizerWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();
        var capabilities = provider.GetServices<IChangeCapability>().ToArray();

        foreach (var definition in OptimizationRegistryDwordCapabilityDefinitions.CurrentBatch)
        {
            var capability = Assert.Single(capabilities, candidate => candidate.Descriptor.CapabilityId == definition.CapabilityId);
            Assert.True(capability.Descriptor.RequiresAdministrator);
            Assert.True(capability.Descriptor.RequiresExplicitConfirmation);
            Assert.Equal(definition.RequiresRestart, capability.Descriptor.RequiresRestart);
            Assert.Equal(
                $"registry:HKLM/{definition.SubKeyPath}/{definition.ValueName}",
                capability.Descriptor.ResourceKey);
        }

        Assert.Equal(
            OptimizationRegistryDwordCapabilityDefinitions.CurrentBatch.Count,
            OptimizationRegistryDwordCapabilityDefinitions.CurrentBatch
                .Select(definition => $"registry:HKLM/{definition.SubKeyPath}/{definition.ValueName}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    [Fact]
    public async Task NextBatch_TcpParametersHaveRestartGateAndExactFakeRoundTrip()
    {
        var environment = new FakeEnvironmentProvider(22631, "Windows 11 Pro", isAdministrator: true);

        Assert.Equal(
            OptimizationRegistryDwordCapabilityDefinitions.NextBatch.Count,
            OptimizationRegistryDwordCapabilityDefinitions.NextBatch
                .Select(definition => $"registry:HKLM/{definition.SubKeyPath}/{definition.ValueName}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        foreach (var definition in OptimizationRegistryDwordCapabilityDefinitions.NextBatch)
        {
            Assert.True(definition.RequiresRestart);
            var accessor = new FakeRegistryAccessor(new RegistryDwordState(false, false, null));
            var capability = CreateCapability(definition, accessor, environment);
            var scope = TargetScope.Machine(environment.MachineName);

            var before = await capability.ReadAsync(scope, CancellationToken.None);
            var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
            var applied = await capability.ExecuteAsync(change, CancellationToken.None);

            Assert.True(applied.Success);
            Assert.Equal(definition.TargetValue, accessor.State.Value);
            Assert.Equal("DWord", accessor.State.ValueKind);

            var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);

            Assert.True(restored.Success);
            Assert.Equal(new RegistryDwordState(false, false, null), accessor.State);
        }
    }

    [Theory]
    [InlineData(19041, "Windows 10 Home", CapabilityApplicability.Applicable)]
    [InlineData(22631, "Windows 11 Pro", CapabilityApplicability.Applicable)]
    [InlineData(18362, "Windows 10 Enterprise", CapabilityApplicability.Unsupported)]
    public async Task DwordCapability_UsesFakeBuildAndEditionFactsWithoutHostRegistryAccess(
        int build,
        string edition,
        CapabilityApplicability expected)
    {
        var environment = new FakeEnvironmentProvider(build, edition, isAdministrator: true);
        var capability = CreateCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableWebcam,
            new FakeRegistryAccessor(new RegistryDwordState(false, false, null)),
            environment);

        var assessment = await capability.ProbeAsync(TargetScope.Machine(environment.MachineName), CancellationToken.None);

        Assert.Equal(expected, assessment.Applicability);
    }

    [Fact]
    public async Task DwordCapability_MissingValueKindPermissionDriftAndRestoreAreFailSafeAndReversible()
    {
        var environment = new FakeEnvironmentProvider(22631, "Windows 11 Pro", isAdministrator: true);
        var accessor = new FakeRegistryAccessor(new RegistryDwordState(false, false, null));
        var capability = CreateCapability(OptimizationRegistryDwordCapabilityDefinitions.DisableWebcam, accessor, environment);
        var scope = TargetScope.Machine(environment.MachineName);

        var before = await capability.ReadAsync(scope, CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.True(accessor.State.ValueExists);
        Assert.Equal(OptimizationRegistryDwordCapabilityDefinitions.DisableWebcam.TargetValue, accessor.State.Value);

        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);
        Assert.True(restored.Success);
        Assert.Equal(new RegistryDwordState(false, false, null), accessor.State);

        var wrongKindAccessor = new FakeRegistryAccessor(new RegistryDwordState(true, true, null, "String"));
        var wrongKindCapability = CreateCapability(OptimizationRegistryDwordCapabilityDefinitions.DisableWebcam, wrongKindAccessor, environment);
        var wrongKindBefore = await wrongKindCapability.ReadAsync(scope, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => wrongKindCapability.PrepareAsync(wrongKindBefore, "apply", CancellationToken.None));
        Assert.Equal(0, wrongKindAccessor.WriteCount);

        var noPermission = CreateCapability(
            OptimizationRegistryDwordCapabilityDefinitions.DisableWebcam,
            accessor,
            new FakeEnvironmentProvider(22631, "Windows 11 Home", isAdministrator: false));
        var permission = await noPermission.ProbeAsync(scope, CancellationToken.None);
        Assert.Equal(CapabilityApplicability.Applicable, permission.Applicability);
        Assert.False(permission.HasRequiredPermission);

        var driftAccessor = new FakeRegistryAccessor(new RegistryDwordState(true, true, 1));
        var driftCapability = CreateCapability(OptimizationRegistryDwordCapabilityDefinitions.DisableWebcam, driftAccessor, environment);
        var driftBefore = await driftCapability.ReadAsync(scope, CancellationToken.None);
        var driftChange = await driftCapability.PrepareAsync(driftBefore, "apply", CancellationToken.None);
        driftAccessor.SetState(new RegistryDwordState(true, true, 2));
        var drift = await driftCapability.ExecuteAsync(driftChange, CancellationToken.None);
        Assert.False(drift.Success);
        Assert.Equal("state.drift", drift.ReasonCode);
        Assert.Equal(0, driftAccessor.WriteCount);
    }

    private static RegistryDwordCapability CreateCapability(
        RegistryDwordCapabilityDefinition definition,
        IRegistryDwordAccessor accessor,
        IRegistryDwordCapabilityEnvironmentProvider environment) =>
        new(definition, accessor, environmentProvider: environment);

    private static RecoveryMaterial CreateMaterial(RegistryDwordCapability capability, PreparedCapabilityChange change) =>
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

    private sealed class FakeEnvironmentProvider(int build, string edition, bool isAdministrator)
        : IRegistryDwordCapabilityEnvironmentProvider
    {
        public string MachineName => "TEST-MACHINE";

        public RegistryDwordCapabilityEnvironment GetCurrent() => new(
            true,
            build,
            edition,
            MachineName,
            "S-1-5-21-test",
            isAdministrator);
    }

    private sealed class FakeRegistryAccessor(RegistryDwordState initial) : IRegistryDwordAccessor
    {
        public RegistryDwordState State { get; private set; } = initial;
        public int WriteCount { get; private set; }

        public void SetState(RegistryDwordState state) => State = state;

        public Task<RegistryDwordState> ReadCurrentUserAsync(string subKeyPath, string valueName, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HKCU must not be used by this machine capability.");

        public Task WriteCurrentUserAsync(string subKeyPath, string valueName, int value, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HKCU must not be used by this machine capability.");

        public Task RestoreCurrentUserAsync(string subKeyPath, string valueName, RegistryDwordState original, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("HKCU must not be used by this machine capability.");

        public Task<RegistryDwordState> ReadLocalMachineAsync(string subKeyPath, string valueName, CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task WriteLocalMachineAsync(string subKeyPath, string valueName, int value, CancellationToken cancellationToken)
        {
            WriteCount++;
            State = new RegistryDwordState(true, true, value);
            return Task.CompletedTask;
        }

        public Task RestoreLocalMachineAsync(string subKeyPath, string valueName, RegistryDwordState original, CancellationToken cancellationToken)
        {
            State = original;
            return Task.CompletedTask;
        }
    }
}
