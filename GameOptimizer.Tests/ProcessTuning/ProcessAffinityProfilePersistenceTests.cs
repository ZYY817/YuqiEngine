using System.Text.Json;
using GameOptimizer.Core.Services.CpuAffinity;

namespace GameOptimizer.Tests.ProcessTuning;

public sealed class ProcessAffinityProfilePersistenceTests
{
    [Fact]
    public void ActiveReceiptAssociation_RoundTripsForRestartRecovery()
    {
        var receiptId = Guid.NewGuid();
        var source = new ProcessAffinityProfile
        {
            ProcessName = "controlled-test.exe",
            AffinityMask = 3,
            AssignedThreads = [0, 1],
            ActiveReceiptId = receiptId,
            ActiveProcessId = 4242
        };

        var json = JsonSerializer.Serialize(source);
        var restored = Assert.IsType<ProcessAffinityProfile>(JsonSerializer.Deserialize<ProcessAffinityProfile>(json));

        Assert.Equal(receiptId, restored.ActiveReceiptId);
        Assert.Equal(4242, restored.ActiveProcessId);
        Assert.Equal([0, 1], restored.AssignedThreads);
    }
}
