using System.Reflection;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services.CpuAffinity;

namespace GameOptimizer.Tests.CpuAffinity;

public sealed class CpuAffinityServicePresetTests
{
    [Fact]
    public void GetPresets_uses_a_full_64_bit_mask_without_shift_wraparound()
    {
        var service = CreateService(new CpuInfo
        {
            LogicalProcessors = 64,
            PhysicalCores = 32
        });

        var presets = service.GetPresets();

        Assert.Equal(-1L, presets[0].AffinityMask);
        Assert.Equal((long)uint.MaxValue, presets[2].AffinityMask);
    }

    [Fact]
    public void GetPresets_never_emits_an_empty_mask_for_a_single_processor_topology()
    {
        var service = CreateService(new CpuInfo
        {
            LogicalProcessors = 1,
            PhysicalCores = 1
        });

        var presets = service.GetPresets();

        Assert.All(presets, preset => Assert.NotEqual(0L, preset.AffinityMask));
    }

    private static CpuAffinityService CreateService(CpuInfo cpuInfo)
    {
        var service = new CpuAffinityService();
        var field = typeof(CpuAffinityService).GetField("_cachedCpuInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(service, cpuInfo);
        return service;
    }
}
