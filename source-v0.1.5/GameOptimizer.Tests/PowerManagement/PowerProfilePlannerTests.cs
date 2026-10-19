using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Models;
using GameOptimizer.Infrastructure.Windows.PowerManagement;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class PowerProfilePlannerTests
{
    [Fact]
    public void ReliableDesktopOnAc_OffersThreeReadOnlyCandidates()
    {
        var preview = new PowerProfilePlanner().Plan(Context());

        Assert.True(preview.DoesNotWriteOrSwitchSchemes);
        Assert.Equal(3, preview.Candidates.Count);
        Assert.All(preview.Candidates, candidate => Assert.True(candidate.DoesNotWriteOrSwitchSchemes));
        Assert.Equal(PowerProfileAvailability.Available,
            preview.Candidates.Single(candidate => candidate.Id == PowerProfileId.GamingConservative).Availability);
    }

    [Fact]
    public void ConservativeGaming_UsesExistingTypedDefinitions_AndExcludesHighRiskOverrides()
    {
        var candidate = new PowerProfilePlanner().Plan(Context()).Candidates
            .Single(value => value.Id == PowerProfileId.GamingConservative);
        var ids = candidate.PreviewItems.Select(item => item.CapabilityId).ToArray();

        Assert.Contains(PowerSettingCapabilityDefinitions.PerfIncreaseThreshold.CapabilityId, ids);
        Assert.Contains(PowerSettingCapabilityDefinitions.DisableUsbSuspend.CapabilityId, ids);
        Assert.DoesNotContain("cpu.disable_core_parking", ids);
        Assert.DoesNotContain("power.disable_throttle", ids);
        Assert.DoesNotContain("cpu.disable_pstates", ids);
        Assert.DoesNotContain("intel.disable_cstates", ids);
        Assert.DoesNotContain("power.hetero_thread_scheduling", ids);
        Assert.All(candidate.PreviewItems, item => Assert.Contains(item.Risk,
            new[] { ChangeRisk.LowReversible, ChangeRisk.MediumRebootOrExperience }));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void RestrictedInputs_MakeGamingPreviewUnavailable(
        bool onBattery, bool modernStandby, bool policyRestricted, bool unreadableScheme)
    {
        var context = Context() with
        {
            Platform = Platform() with
            {
                HasBattery = onBattery,
                IsOnAcPower = !onBattery,
                IsModernStandby = modernStandby,
                IsPolicyRestricted = policyRestricted
            },
            ActiveSchemeIsReadable = !unreadableScheme
        };

        var candidate = new PowerProfilePlanner().Plan(context).Candidates
            .Single(value => value.Id == PowerProfileId.GamingConservative);

        Assert.Equal(PowerProfileAvailability.Unavailable, candidate.Availability);
        Assert.NotEmpty(candidate.Reasons);
    }

    [Fact]
    public void IntelHybrid_IsDescribedButNeverAddsHybridSchedulingOverride()
    {
        var topology = Topology();
        topology.Architecture = CpuArchitecture.IntelHybrid;
        topology.HasHybridCores = true;
        var context = Context() with { Topology = topology };

        var candidate = new PowerProfilePlanner().Plan(context).Candidates
            .Single(value => value.Id == PowerProfileId.CurrentPlanAdvancedSettings);

        Assert.Contains(candidate.Preconditions, item => item.Contains("混合架构", StringComparison.Ordinal));
        Assert.DoesNotContain(candidate.PreviewItems, item =>
            item.CapabilityId.Contains("hetero", StringComparison.OrdinalIgnoreCase));
    }

    private static PowerProfilePlanningContext Context() => new(Platform(), Topology(), ActiveSchemeIsReadable: true);

    private static PowerPlatformFacts Platform() => new(
        IsWindows: true,
        WindowsBuild: 22631,
        MachineName: "test-machine",
        IsAdministrator: true,
        HasBattery: false,
        IsOnAcPower: true,
        IsModernStandby: false,
        IsPolicyRestricted: false,
        IsOemRestricted: false,
        ProcessorGroupCount: 1,
        IsDetectionReliable: true);

    private static CpuTopology Topology() => new()
    {
        CpuName = "Test CPU",
        Vendor = CpuVendor.Intel,
        Architecture = CpuArchitecture.IntelStandard,
        LogicalCores = 16,
        ProcessorGroupCount = 1
    };
}
