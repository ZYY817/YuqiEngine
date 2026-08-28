// File: GameOptimizer.Tests/Changes/PowerSettingCapabilityTests.cs
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.Changes;

public sealed class PowerSettingCapabilityTests
{
    private static readonly Guid Scheme = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Subgroup = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid Setting = new("893dee8e-2bef-41e0-89c6-b55d0929964c");

    [Fact]
    public async Task RoundTrip_WritesTargetsThenRestoresExactOriginals()
    {
        var accessor = new FakePowerSettingAccessor(Scheme, (Subgroup, Setting, 5, 8));
        var capability = CreateCapability(accessor);

        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.True(applied.MutationOccurred);
        Assert.Equal((100, 100), accessor.Get(Subgroup, Setting));

        var material = CreateMaterial(capability, change);
        var restored = await capability.RestoreAsync(material, CancellationToken.None);

        Assert.True(restored.Success);
        Assert.Equal((5, 8), accessor.Get(Subgroup, Setting));
    }

    [Fact]
    public async Task AcOnlyTarget_PreservesDcValue()
    {
        var accessor = new FakePowerSettingAccessor(Scheme, (Subgroup, Setting, 5, 37));
        var capability = CreateCapability(accessor, targetDc: null);

        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.Equal((100, 37), accessor.Get(Subgroup, Setting));
    }

    [Fact]
    public async Task PartialWriteFailure_CompensatesAndVerifiesOriginalState()
    {
        var second = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");
        var definition = new PowerSettingChangeCapabilityDefinition(
            "test.safe-pair", "1.0.0", "power:setting:test-safe-pair",
            ChangeRisk.LowReversible,
            [
                new PowerSettingTarget("first", Subgroup, Setting, 20, null),
                new PowerSettingTarget("second", Subgroup, second, 30, null)
            ]);
        var accessor = new FakePowerSettingAccessor(
            Scheme, (Subgroup, Setting, 5, 7), (Subgroup, second, 9, 11))
        {
            ThrowOnWriteNumber = 2
        };
        var capability = new PowerSettingChangeCapability(definition, accessor,
            new FakeGuard(PowerSettingGuardDecision.Allowed, ""), TimeProvider.System,
            new FakePowerSettingEnvironmentProvider("TEST-MACHINE", true));

        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("execution.compensated", result.ReasonCode);
        Assert.Equal((5, 7), accessor.Get(Subgroup, Setting));
        Assert.Equal((9, 11), accessor.Get(Subgroup, second));
    }

    [Fact]
    public async Task Execute_BlocksWhenStateDriftedAfterFreeze()
    {
        var accessor = new FakePowerSettingAccessor(Scheme, (Subgroup, Setting, 5, 8));
        var capability = CreateCapability(accessor);

        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        accessor.Set(Subgroup, Setting, 99, 99);
        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("state.drift", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task Probe_SkipsUnsupportedWhenGuardReportsUnsupported()
    {
        var accessor = new FakePowerSettingAccessor(Scheme, (Subgroup, Setting, 5, 8));
        var guard = new FakeGuard(PowerSettingGuardDecision.SkippedUnsupported, "当前方案未公开该设置");
        var capability = CreateCapability(accessor, guard);

        var assessment = await capability.ProbeAsync(Machine(), CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Unsupported, assessment.Applicability);
        Assert.Equal("guard.unsupported", assessment.ReasonCode);
    }

    [Fact]
    public async Task Probe_BlocksRiskWhenGuardReportsBlocked()
    {
        var accessor = new FakePowerSettingAccessor(Scheme, (Subgroup, Setting, 5, 8));
        var guard = new FakeGuard(PowerSettingGuardDecision.BlockedRisk, "便携设备高危写入已拦截");
        var capability = CreateCapability(accessor, guard);

        var assessment = await capability.ProbeAsync(Machine(), CancellationToken.None);

        Assert.Equal(CapabilityApplicability.Conflict, assessment.Applicability);
        Assert.Equal("guard.blocked-risk", assessment.ReasonCode);
    }

    [Fact]
    public async Task Execute_RechecksGuardAndCannotBypassProbe()
    {
        var accessor = new FakePowerSettingAccessor(Scheme, (Subgroup, Setting, 5, 8));
        var guard = new SequencedGuard(
            new PowerSettingGuardResult { Decision = PowerSettingGuardDecision.Allowed },
            new PowerSettingGuardResult { Decision = PowerSettingGuardDecision.BlockedRisk, Reason = "hardware facts changed" });
        var capability = CreateCapability(accessor, guard);
        _ = await capability.ProbeAsync(Machine(), CancellationToken.None);
        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        var result = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("guard.recheck-blocked", result.ReasonCode);
        Assert.Equal(0, accessor.WriteCount);
    }

    [Fact]
    public async Task Restore_RejectsMaterialForADifferentCapability()
    {
        var accessor = new FakePowerSettingAccessor(Scheme, (Subgroup, Setting, 5, 8));
        var capability = CreateCapability(accessor);
        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var material = CreateMaterial(capability, change) with { CapabilityId = "other.capability" };

        await Assert.ThrowsAsync<InvalidDataException>(() => capability.RestoreAsync(material, CancellationToken.None));
    }

    [Fact]
    public async Task Prepare_RejectsFingerprintMismatchViaFrozenValidation()
    {
        var accessor = new FakePowerSettingAccessor(Scheme, (Subgroup, Setting, 5, 8));
        var capability = CreateCapability(accessor);
        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);

        var tampered = change with { TargetFingerprint = "DEADBEEF" };

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => capability.ExecuteAsync(tampered, CancellationToken.None));
        Assert.Contains("指纹", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiTargetCapability_WritesAndRestoresEveryTarget()
    {
        var coreParkingDef = new PowerSettingChangeCapabilityDefinition(
            "cpu.disable_core_parking",
            "1.0.0",
            "power:setting:core-parking",
            ChangeRisk.MediumRebootOrExperience,
            new[]
            {
                new PowerSettingTarget("min", Subgroup, Setting, 100, 100),
                new PowerSettingTarget("max", Subgroup, new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec"), 100, 100)
            },
            RequiresExplicitConfirmation: true);
        var maxSetting = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");
        var accessor = new FakePowerSettingAccessor(
            Scheme,
            (Subgroup, Setting, 50, 60),
            (Subgroup, maxSetting, 90, 95));
        var capability = new PowerSettingChangeCapability(
            coreParkingDef,
            accessor,
            new FakeGuard(PowerSettingGuardDecision.Allowed, ""),
            TimeProvider.System,
            new FakePowerSettingEnvironmentProvider("TEST-MACHINE", isAdministrator: true));

        var before = await capability.ReadAsync(Machine(), CancellationToken.None);
        var change = await capability.PrepareAsync(before, "apply", CancellationToken.None);
        var applied = await capability.ExecuteAsync(change, CancellationToken.None);

        Assert.True(applied.Success);
        Assert.Equal((100, 100), accessor.Get(Subgroup, Setting));
        Assert.Equal((100, 100), accessor.Get(Subgroup, maxSetting));

        var restored = await capability.RestoreAsync(CreateMaterial(capability, change), CancellationToken.None);
        Assert.True(restored.Success);
        Assert.Equal((50, 60), accessor.Get(Subgroup, Setting));
        Assert.Equal((90, 95), accessor.Get(Subgroup, maxSetting));
    }

    private static PowerSettingChangeCapability CreateCapability(
        FakePowerSettingAccessor accessor,
        IPowerSettingCapabilityGuard? guard = null,
        int? targetDc = 100) =>
        new(
            new PowerSettingChangeCapabilityDefinition(
                "power.processor_min_state_100",
                "1.0.0",
                "power:setting:processor-min-state",
                ChangeRisk.MediumRebootOrExperience,
                [new PowerSettingTarget("min-state", Subgroup, Setting, 100, targetDc)],
                RequiresExplicitConfirmation: true),
            accessor,
            guard ?? new FakeGuard(PowerSettingGuardDecision.Allowed, ""),
            TimeProvider.System,
            new FakePowerSettingEnvironmentProvider("TEST-MACHINE", isAdministrator: true));

    private static TargetScope Machine() => TargetScope.Machine("TEST-MACHINE");

    private static RecoveryMaterial CreateMaterial(PowerSettingChangeCapability capability, PreparedCapabilityChange change) =>
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

    private sealed class FakePowerSettingEnvironmentProvider(string machineName, bool isAdministrator)
        : IPowerSettingEnvironmentProvider
    {
        public PowerSettingEnvironment GetCurrent() => new(true, 22631, machineName, isAdministrator);
    }

    private sealed class FakeGuard(PowerSettingGuardDecision decision, string reason) : IPowerSettingCapabilityGuard
    {
        public Task<PowerSettingGuardResult> EvaluateAsync(
            Guid scheme,
            Guid subgroup,
            Guid setting,
            int? targetAc,
            int? targetDc,
            string? operationId = null,
            string? label = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PowerSettingGuardResult { Decision = decision, Reason = reason });
    }

    private sealed class SequencedGuard(params PowerSettingGuardResult[] results) : IPowerSettingCapabilityGuard
    {
        private readonly Queue<PowerSettingGuardResult> _results = new(results);
        public Task<PowerSettingGuardResult> EvaluateAsync(Guid scheme, Guid subgroup, Guid setting, int? targetAc,
            int? targetDc, string? operationId = null, string? label = null,
            CancellationToken cancellationToken = default) => Task.FromResult(_results.Dequeue());
    }

    private sealed class FakePowerSettingAccessor : IPowerSettingAccessor
    {
        private readonly Guid _scheme;
        private readonly Dictionary<(Guid, Guid), (int? Ac, int? Dc)> _values = new();

        public int WriteCount { get; private set; }
        public int? ThrowOnWriteNumber { get; init; }

        public FakePowerSettingAccessor(Guid scheme, params (Guid Subgroup, Guid Setting, int Ac, int Dc)[] initial)
        {
            _scheme = scheme;
            foreach (var entry in initial)
            {
                _values[(entry.Subgroup, entry.Setting)] = (entry.Ac, entry.Dc);
            }
        }

        public void Set(Guid subgroup, Guid setting, int ac, int dc) => _values[(subgroup, setting)] = (ac, dc);

        public (int Ac, int Dc) Get(Guid subgroup, Guid setting)
        {
            var (ac, dc) = _values[(subgroup, setting)];
            return (ac ?? 0, dc ?? 0);
        }

        public Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_scheme);

        public Task<PowerSettingValueState> ReadValueAsync(
            Guid scheme,
            Guid subgroup,
            Guid setting,
            CancellationToken cancellationToken)
        {
            var (ac, dc) = _values.TryGetValue((subgroup, setting), out var value) ? value : ((int?)null, (int?)null);
            return Task.FromResult(new PowerSettingValueState(subgroup, setting, ac, dc));
        }

        public Task WriteValueAsync(
            Guid scheme,
            Guid subgroup,
            Guid setting,
            int? acValue,
            int? dcValue,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            if (ThrowOnWriteNumber == WriteCount)
                throw new InvalidOperationException("fake partial write failure");
            _values[(subgroup, setting)] = (acValue, dcValue);
            return Task.CompletedTask;
        }
    }
}
