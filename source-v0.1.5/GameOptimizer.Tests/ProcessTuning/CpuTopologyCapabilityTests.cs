using GameOptimizer.Core.Services;

namespace GameOptimizer.Tests.ProcessTuning;

public sealed class CpuTopologyCapabilityTests
{
    [Theory]
    [InlineData(1, 8, false, true, "topology.manual-supported")]
    [InlineData(1, 32, true, true, "topology.virtualized-manual-only")]
    [InlineData(2, 32, false, false, "topology.processor-group-unsupported")]
    [InlineData(1, 64, false, false, "topology.mask-width-unsupported")]
    [InlineData(0, 0, false, false, "topology.unknown")]
    public void CapabilityMatrix_IsFailClosed(
        int groups, int logical, bool virtualized, bool expectedSupported, string expectedReason)
    {
        var result = CpuTopologyService.EvaluateAffinityCapability(groups, logical, virtualized);
        Assert.Equal(expectedSupported, result.IsSupported);
        Assert.Equal(expectedReason, result.ReasonCode);
    }
}
