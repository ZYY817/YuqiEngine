using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class PowerHardwareSafetyMatrixTests
{
    private static readonly Guid Scheme = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData(CpuVendor.Intel, CpuArchitecture.IntelStandard, false, false)]
    [InlineData(CpuVendor.AMD, CpuArchitecture.Standard, false, false)]
    [InlineData(CpuVendor.Intel, CpuArchitecture.IntelHybrid, false, false)]
    [InlineData(CpuVendor.Intel, CpuArchitecture.IntelStandard, true, true)]
    [InlineData(CpuVendor.AMD, CpuArchitecture.Standard, true, false)]
    public async Task ReliableSupportedMachines_AllowConservativeAcOnlySetting(
        CpuVendor vendor, CpuArchitecture architecture, bool portable, bool onAc)
    {
        var guard = CreateGuard(Facts(vendor, architecture, portable: portable, onAc: onAc));

        var result = await guard.EvaluateAsync(Scheme, PowerGuids.ProcessorSubgroup,
            PowerGuids.ProcessorPerfIncreaseThreshold, 10, null, "power.safe-threshold");

        Assert.Equal(PowerSettingGuardDecision.Allowed, result.Decision);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("unreliable")]
    [InlineData("oem")]
    [InlineData("modern-standby")]
    public async Task UnknownRestrictedOrModernStandby_FailsClosed(string scenario)
    {
        var facts = scenario switch
        {
            "unknown" => Facts(CpuVendor.Unknown, CpuArchitecture.Standard),
            "unreliable" => Facts(CpuVendor.Intel, CpuArchitecture.IntelStandard) with { IsDetectionReliable = false },
            "oem" => Facts(CpuVendor.Intel, CpuArchitecture.IntelStandard) with { IsOemOrPolicyRestricted = true },
            _ => Facts(CpuVendor.Intel, CpuArchitecture.IntelStandard) with { IsModernStandby = true }
        };
        var guard = CreateGuard(facts);

        var result = await guard.EvaluateAsync(Scheme, PowerGuids.ProcessorSubgroup,
            PowerGuids.ProcessorPerfIncreaseThreshold, 10, null, "power.safe-threshold");

        Assert.Equal(PowerSettingGuardDecision.BlockedRisk, result.Decision);
    }

    [Fact]
    public async Task MultipleProcessorGroups_BlockTopologySensitiveHybridSetting()
    {
        var facts = Facts(CpuVendor.Intel, CpuArchitecture.IntelHybrid);
        facts.Topology.ProcessorGroupCount = 2;
        facts.Topology.LogicalCores = 128;
        var guard = CreateGuard(facts);

        var result = await guard.EvaluateAsync(Scheme, PowerGuids.ProcessorSubgroup,
            PowerGuids.ProcessorClass1PerfLevel, 75, null, "power.class1_perf_level");

        Assert.Equal(PowerSettingGuardDecision.BlockedRisk, result.Decision);
        Assert.Contains("processor groups", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("power.processor_min_state_100")]
    [InlineData("cpu.disable_core_parking")]
    [InlineData("power.disable_throttle")]
    [InlineData("cpu.disable_pstates")]
    [InlineData("intel.disable_cstates")]
    [InlineData("cpu.processor_boost_mode")]
    public async Task ReversibleCpuCapabilities_AreAvailableOnOtherwiseReliableDesktop(string operationId)
    {
        var (setting, value) = operationId switch
        {
            "power.processor_min_state_100" => (PowerGuids.ProcessorMinState, 100),
            "cpu.disable_core_parking" => (PowerGuids.CoreParkingMin, 100),
            "power.disable_throttle" => (PowerGuids.ProcessorThrottlePolicy, 0),
            "cpu.disable_pstates" => (PowerGuids.ProcessorMinState, 100),
            "intel.disable_cstates" => (Guid.Parse("5D76A2CA-E8C0-402F-A133-2158492D58AD"), 1),
            _ => (PowerGuids.ProcessorBoostMode, 2)
        };
        var guard = CreateGuard(Facts(CpuVendor.Intel, CpuArchitecture.IntelStandard));

        var result = await guard.EvaluateAsync(Scheme, PowerGuids.ProcessorSubgroup,
            setting, value, null, operationId);

        Assert.Equal(PowerSettingGuardDecision.Allowed, result.Decision);
    }

    [Fact]
    public void ProductionDefinitions_DefaultToAcOnly()
    {
        foreach (var definition in GameOptimizer.Infrastructure.Windows.PowerManagement.PowerSettingCapabilityDefinitions.All)
        {
            Assert.All(definition.Targets, target => Assert.Null(target.TargetDc));
            Assert.True(definition.RequiresExplicitConfirmation);
        }
    }

    private static PowerSettingCapabilityGuard CreateGuard(PowerHardwareFacts facts) =>
        new(new FakeFactsProvider(facts), new FakeSupportProbe());

    private static PowerHardwareFacts Facts(CpuVendor vendor, CpuArchitecture architecture,
        bool portable = false, bool onAc = true) =>
        new(new CpuTopology
        {
            Vendor = vendor,
            Architecture = architecture,
            CpuName = vendor == CpuVendor.Unknown ? "Unknown CPU" : $"Test {vendor} CPU",
            LogicalCores = 16,
            ProcessorGroupCount = 1
        }, portable, portable, onAc, false, false, true);

    private sealed class FakeFactsProvider(PowerHardwareFacts facts) : IPowerHardwareFactsProvider
    {
        public PowerHardwareFacts GetCurrent() => facts;
    }

    private sealed class FakeSupportProbe : IPowerSettingSupportProbe
    {
        public Task<(int? Ac, int? Dc)> ReadAsync(Guid scheme, Guid subgroup, Guid setting,
            CancellationToken cancellationToken) => Task.FromResult<(int?, int?)>((5, 5));
    }
}
