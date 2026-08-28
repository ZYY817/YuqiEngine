using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.Registry;

namespace GameOptimizer.Tests.Changes;

public sealed class HardwareAccelerationRepairCompatibilityRegressionTests
{
    [Theory]
    [InlineData("Windows 10", 19041, false)]
    [InlineData("Windows 10", 19041, true)]
    [InlineData("Windows 11", 22631, false)]
    [InlineData("Windows 11", 22631, true)]
    public async Task HardwareAcceleration_RealRepairEntry_AppliesAndRestores(
        string edition,
        int build,
        bool isAdministrator)
    {
        var harness = FeedbackRepairCompatibilityRegressionTests.Harness.Create(
            build,
            edition,
            isWindows: true,
            isAdministrator,
            definition: RepairRegistryCapabilityDefinitions.HardwareAccelerationReset,
            repairItemId: "repair.hardware_acceleration");

        Assert.False(harness.Capability.Descriptor.RequiresAdministrator);
        var applied = await harness.Service.RepairAsync(harness.Item);

        Assert.True(applied.Success, applied.Message);
        Assert.False(applied.RequiresReboot);
        Assert.Equal(1, harness.Registry.CurrentUserWriteCount);
        Assert.Equal(0, harness.Registry.State.Value);
        var receipt = Assert.IsType<RepairChangeReceipt>(applied.ChangeReceipt);

        var restored = await harness.Service.RestoreAsync(
            new RepairRecoveryRequest(harness.Item.Id, receipt));

        Assert.True(restored.Success, restored.Message);
        Assert.False(restored.RequiresReboot);
        Assert.Equal(new RegistryDwordState(true, true, 7), harness.Registry.State);
        Assert.Equal(1, harness.Registry.CurrentUserRestoreCount);
    }

    [Fact]
    public async Task HardwareAcceleration_UnsupportedWindowsBuild_BlocksBeforeRegistryProbe()
    {
        var harness = FeedbackRepairCompatibilityRegressionTests.Harness.Create(
            18363,
            "Windows 10 1909",
            isWindows: true,
            isAdministrator: false,
            definition: RepairRegistryCapabilityDefinitions.HardwareAccelerationReset,
            repairItemId: "repair.hardware_acceleration");

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Contains("Windows 10 2004", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Registry.CurrentUserReadCount);
        Assert.Equal(0, harness.Registry.CurrentUserWriteCount);
    }

    [Fact]
    public async Task HardwareAcceleration_PolicyGate_BlocksBeforeRegistryProbe()
    {
        var harness = FeedbackRepairCompatibilityRegressionTests.Harness.Create(
            22631,
            "Windows 11 Pro",
            isWindows: true,
            isAdministrator: false,
            repairGate: new FeedbackRepairCompatibilityRegressionTests.FixedRepairGate(
                new RepairExecutionGateDecision(
                    RepairExecutionGateDecisionKind.BlockedNeedBaseline,
                    "policy-managed")),
            definition: RepairRegistryCapabilityDefinitions.HardwareAccelerationReset,
            repairItemId: "repair.hardware_acceleration");

        var result = await harness.Service.RepairAsync(harness.Item);

        Assert.False(result.Success);
        Assert.Equal("policy-managed", result.Message);
        Assert.Equal(0, harness.Registry.CurrentUserReadCount);
        Assert.Equal(0, harness.Registry.CurrentUserWriteCount);
    }

    [Fact]
    public async Task HardwareAcceleration_CancellationBeforeEntry_IsZeroRegistryWrite()
    {
        var harness = FeedbackRepairCompatibilityRegressionTests.Harness.Create(
            22631,
            "Windows 11",
            isWindows: true,
            isAdministrator: false,
            definition: RepairRegistryCapabilityDefinitions.HardwareAccelerationReset,
            repairItemId: "repair.hardware_acceleration");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await harness.Service.RepairAsync(
            harness.Item,
            cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(0, harness.Registry.CurrentUserReadCount);
        Assert.Equal(0, harness.Registry.CurrentUserWriteCount);
        Assert.Equal(0, harness.Registry.CurrentUserRestoreCount);
    }
}
