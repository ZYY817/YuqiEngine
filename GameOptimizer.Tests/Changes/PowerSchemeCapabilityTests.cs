// File: GameOptimizer.Tests/Changes/PowerSchemeCapabilityTests.cs
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.Changes;

public sealed class PowerSchemeCapabilityTests
{
    private static readonly Guid Original = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid Target = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    [Fact]
    public async Task RoundTrip_SwitchesToTargetThenRestoresOriginal()
    {
        var accessor = new FakePowerSchemeAccessor(Original);
        var capability = new PowerSchemeChangeCapability(
            new PowerSchemeChangeCapabilityDefinition(
                "services.enable_high_performance_mode",
                "1.0.0",
                PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource,
                Target),
            accessor,
            TimeProvider.System,
            new FakePowerSchemeEnvironmentProvider("TEST-MACHINE", isAdministrator: true),
            catalog: new FakePowerSchemeCatalog(Target));

        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.Equal(Target, accessor.ActiveScheme);

        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);
        Assert.True(restored.Success);
        Assert.Equal(Original, accessor.ActiveScheme);
    }

    [Fact]
    public async Task Execute_BlocksWhenActiveSchemeDriftedAfterFreeze()
    {
        var accessor = new FakePowerSchemeAccessor(Original);
        var capability = new PowerSchemeChangeCapability(
            new PowerSchemeChangeCapabilityDefinition(
                "services.enable_high_performance_mode",
                "1.0.0",
                PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource,
                Target),
            accessor,
            TimeProvider.System,
            new FakePowerSchemeEnvironmentProvider("TEST-MACHINE", isAdministrator: true),
            catalog: new FakePowerSchemeCatalog(Target));

        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.ActiveScheme = new Guid("33333333-4444-5555-6666-777777777777");
        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task Probe_RequiresAdministratorAndMachineScope()
    {
        var accessor = new FakePowerSchemeAccessor(Original);
        var capability = new PowerSchemeChangeCapability(
            new PowerSchemeChangeCapabilityDefinition(
                "services.enable_high_performance_mode",
                "1.0.0",
                PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource,
                Target),
            accessor,
            TimeProvider.System,
            new FakePowerSchemeEnvironmentProvider("TEST-MACHINE", isAdministrator: false),
            catalog: new FakePowerSchemeCatalog(Target));

        var assessment = await capability.ProbeAsync(Machine(), CancellationToken.None);
        Assert.Equal(CapabilityApplicability.Applicable, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);

        var wrongScope = await capability.ProbeAsync(TargetScope.Machine("OTHER-MACHINE"), CancellationToken.None);
        Assert.Equal(CapabilityApplicability.NotApplicable, wrongScope.Applicability);
    }

    [Theory]
    [InlineData("modern", CapabilityApplicability.NotApplicable, "power.guidance.best-performance")]
    [InlineData("battery", CapabilityApplicability.NotApplicable, "power.guidance.ac-required")]
    [InlineData("dc", CapabilityApplicability.NotApplicable, "power.guidance.ac-required")]
    [InlineData("policy", CapabilityApplicability.Unsupported, "power.unavailable.managed")]
    [InlineData("oem", CapabilityApplicability.Unsupported, "power.unavailable.managed")]
    [InlineData("unknown", CapabilityApplicability.Unsupported, "power.unavailable.detection")]
    [InlineData("groups-unknown", CapabilityApplicability.Unsupported, "power.unavailable.detection")]
    public async Task UnsafeOrUnsupportedPlatform_ReturnsGuidanceOrUnavailableAndNeverWrites(
        string scenario, CapabilityApplicability expected, string reason)
    {
        var accessor = new FakePowerSchemeAccessor(Original);
        var facts = DesktopFacts() with
        {
            IsModernStandby = scenario == "modern",
            HasBattery = scenario == "battery",
            IsOnAcPower = scenario != "dc",
            IsPolicyRestricted = scenario == "policy",
            IsOemRestricted = scenario == "oem",
            IsDetectionReliable = scenario is not ("unknown" or "groups-unknown"),
            ProcessorGroupCount = scenario == "groups-unknown" ? 0 : 1
        };
        var capability = Create(accessor, facts, Target);

        var assessment = await capability.ProbeAsync(Machine(), CancellationToken.None);

        Assert.Equal(expected, assessment.Applicability);
        Assert.False(assessment.HasRequiredPermission);
        Assert.Equal(reason, assessment.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task MissingBuiltInPlan_ReturnsGuidanceOnlyAndNeverWrites()
    {
        var accessor = new FakePowerSchemeAccessor(Original);
        var capability = Create(accessor, DesktopFacts(), Original);

        var assessment = await capability.ProbeAsync(Machine(), CancellationToken.None);

        Assert.Equal(CapabilityApplicability.NotApplicable, assessment.Applicability);
        Assert.Equal("power.guidance.plan-missing", assessment.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task PowerSourceDriftAfterPrepare_BlocksBeforeWrite()
    {
        var accessor = new FakePowerSchemeAccessor(Original);
        var facts = new MutableFactsProvider(DesktopFacts());
        var capability = new PowerSchemeChangeCapability(
            new("services.enable_high_performance_mode", "1.0.0", "power:machine:display-timeout-and-active-scheme", Target),
            accessor, TimeProvider.System, factsProvider: facts, catalog: new FakePowerSchemeCatalog(Target));
        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        facts.Value = facts.Value with { IsOnAcPower = false };

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("power.guidance.ac-required", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    private static PowerSchemeChangeCapability Create(FakePowerSchemeAccessor accessor, PowerPlatformFacts facts, params Guid[] catalog) =>
        new(new("services.enable_high_performance_mode", "1.0.0", "power:machine:display-timeout-and-active-scheme", Target),
            accessor, TimeProvider.System, factsProvider: new MutableFactsProvider(facts), catalog: new FakePowerSchemeCatalog(catalog));

    private static PowerPlatformFacts DesktopFacts() =>
        new(true, 22631, "TEST-MACHINE", true, false, true, false, false, false, 1, true);

    private static TargetScope Machine() => TargetScope.Machine("TEST-MACHINE");

    private static RecoveryMaterial CreateMaterial(PowerSchemeChangeCapability capability, PreparedCapabilityChange change) =>
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

    private sealed class FakePowerSchemeEnvironmentProvider(string machineName, bool isAdministrator)
        : IPowerSchemeEnvironmentProvider
    {
        public PowerSchemeEnvironment GetCurrent() => new(true, 22631, machineName, isAdministrator);
    }

    private sealed class FakePowerSchemeAccessor(Guid activeScheme) : IPowerSchemeAccessor
    {
        public Guid ActiveScheme { get; set; } = activeScheme;
        public int WriteCount { get; private set; }

        public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ActiveScheme);

        public Task SetActiveSchemeAsync(Guid scheme, CancellationToken cancellationToken)
        {
            WriteCount++;
            ActiveScheme = scheme;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePowerSchemeCatalog(params Guid[] schemes) : IPowerSchemeCatalog
    {
        public Task<IReadOnlyList<PowerSchemeCatalogEntry>> EnumerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PowerSchemeCatalogEntry>>(schemes.Select(id => new PowerSchemeCatalogEntry(id, id.ToString("D"))).ToArray());
    }

    private sealed class MutableFactsProvider(PowerPlatformFacts value) : IPowerPlatformFactsProvider, IPowerSchemeEnvironmentProvider
    {
        public PowerPlatformFacts Value { get; set; } = value;
        public PowerPlatformFacts GetCurrent() => Value;
        PowerSchemeEnvironment IPowerSchemeEnvironmentProvider.GetCurrent() =>
            new(Value.IsWindows, Value.WindowsBuild, Value.MachineName, Value.IsAdministrator);
    }
}
