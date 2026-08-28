using GameOptimizer.Core.GraphicsSafety;

namespace GameOptimizer.Tests.GraphicsSafety;

public sealed class GraphicsSafetyBoundaryTests
{
    private readonly GraphicsSafetyBoundary _boundary = new();

    [Fact]
    public async Task ExistingTypedCapabilityRequiresExistingOwnerAfterAllChecksPass()
    {
        var result = await EvaluateAsync(GraphicsMutationKind.ExistingTypedRegistryCapability, Valid());

        Assert.Equal(GraphicsSafetyStatus.ExistingOwnerRequired, result.Status);
        Assert.False(result.Allowed);
        Assert.Equal(GraphicsSafetyIdentity.ResourceKey, result.ResourceKey);
    }

    [Fact]
    public async Task VendorPrivateMutationRemainsPolicyDeniedAfterAllChecksPass()
    {
        var result = await EvaluateAsync(GraphicsMutationKind.VendorPrivateSetting, Valid());

        Assert.Equal(GraphicsSafetyStatus.PolicyDenied, result.Status);
        Assert.False(result.Allowed);
    }

    [Theory]
    [InlineData(true, false, false, true, "1.2.3", "same", "same", true, GraphicsSafetyStatus.PolicyManaged)]
    [InlineData(false, true, false, true, "1.2.3", "same", "same", true, GraphicsSafetyStatus.RemoteSessionDenied)]
    [InlineData(false, false, true, true, "1.2.3", "same", "same", true, GraphicsSafetyStatus.VirtualMachineDenied)]
    [InlineData(false, false, false, false, "1.2.3", "same", "same", true, GraphicsSafetyStatus.AdministratorRequired)]
    [InlineData(false, false, false, true, "1.2.3", "before", "after", true, GraphicsSafetyStatus.DriftBlocked)]
    [InlineData(false, false, false, true, "1.2.3", "same", "same", false, GraphicsSafetyStatus.RecoveryUnavailable)]
    public async Task EnvironmentGatesFailClosed(
        bool policy,
        bool remote,
        bool vm,
        bool admin,
        string driver,
        string prepared,
        string current,
        bool receipt,
        GraphicsSafetyStatus expected)
    {
        var environment = Valid() with
        {
            IsPolicyManaged = policy,
            IsRemoteSession = remote,
            IsVirtualMachine = vm,
            IsAdministrator = admin,
            DriverVersion = driver,
            PreparedFingerprint = prepared,
            CurrentFingerprint = current,
            HasRecoveryReceipt = receipt
        };

        Assert.Equal(expected, (await EvaluateAsync(GraphicsMutationKind.DriverLifecycle, environment)).Status);
    }

    [Fact]
    public async Task UnknownDriverAndVendorFailClosed()
    {
        Assert.Equal(GraphicsSafetyStatus.DriverVersionUnknown,
            (await EvaluateAsync(GraphicsMutationKind.DriverLifecycle, Valid() with { DriverVersion = null })).Status);
        Assert.Equal(GraphicsSafetyStatus.UnsupportedVendor,
            (await EvaluateAsync(GraphicsMutationKind.DriverLifecycle,
                Valid() with { Vendors = [GraphicsVendor.Unknown] })).Status);
    }

    [Fact]
    public async Task MultiGpuAndVirtualAdapterFailClosed()
    {
        Assert.Equal(GraphicsSafetyStatus.MultiGpuAmbiguous,
            (await EvaluateAsync(GraphicsMutationKind.GlobalGpuRouting,
                Valid() with { Vendors = [GraphicsVendor.Intel, GraphicsVendor.Nvidia] })).Status);
        Assert.Equal(GraphicsSafetyStatus.UnsupportedVendor,
            (await EvaluateAsync(GraphicsMutationKind.DriverLifecycle,
                Valid() with { Vendors = [GraphicsVendor.Virtual] })).Status);
    }

    [Fact]
    public async Task CancellationNeverAllowsMutation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await _boundary.EvaluateAsync(
            new GraphicsSafetyRequest("gpu.test", GraphicsMutationKind.DriverLifecycle),
            Valid(),
            cancellation.Token);

        Assert.Equal(GraphicsSafetyStatus.Cancelled, result.Status);
        Assert.False(result.Allowed);
    }

    private Task<GraphicsSafetyDecision> EvaluateAsync(
        GraphicsMutationKind kind,
        GraphicsEnvironmentSnapshot environment) =>
        _boundary.EvaluateAsync(new GraphicsSafetyRequest("gpu.test", kind), environment, default);

    private static GraphicsEnvironmentSnapshot Valid() => new(
        [GraphicsVendor.Nvidia], false, false, false, true, "32.0.15.1234", "same", "same", true);
}
