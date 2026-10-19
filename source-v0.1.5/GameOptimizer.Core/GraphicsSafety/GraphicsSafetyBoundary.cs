namespace GameOptimizer.Core.GraphicsSafety;

public interface IGraphicsSafetyBoundary
{
    Task<GraphicsSafetyDecision> EvaluateAsync(
        GraphicsSafetyRequest request,
        GraphicsEnvironmentSnapshot environment,
        CancellationToken cancellationToken);
}

public sealed class GraphicsSafetyBoundary : IGraphicsSafetyBoundary
{
    public Task<GraphicsSafetyDecision> EvaluateAsync(
        GraphicsSafetyRequest request,
        GraphicsEnvironmentSnapshot environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(environment);

        GraphicsSafetyStatus status;
        string message;
        if (cancellationToken.IsCancellationRequested)
        {
            status = GraphicsSafetyStatus.Cancelled;
            message = "Graphics safety evaluation was cancelled; no mutation was attempted.";
        }
        else if (environment.IsPolicyManaged)
        {
            status = GraphicsSafetyStatus.PolicyManaged;
            message = "The graphics setting is managed by policy; local mutation is forbidden.";
        }
        else if (environment.IsRemoteSession)
        {
            status = GraphicsSafetyStatus.RemoteSessionDenied;
            message = "Display and driver mutations are forbidden in a remote session.";
        }
        else if (environment.IsVirtualMachine)
        {
            status = GraphicsSafetyStatus.VirtualMachineDenied;
            message = "Virtual display adapters do not satisfy the physical GPU mutation contract.";
        }
        else if (string.IsNullOrWhiteSpace(environment.DriverVersion))
        {
            status = GraphicsSafetyStatus.DriverVersionUnknown;
            message = "An authoritative driver version is required before evaluating compatibility.";
        }
        else if (environment.Vendors.Count == 0
                 || environment.Vendors.Any(vendor => vendor is GraphicsVendor.Unknown or GraphicsVendor.Virtual))
        {
            status = GraphicsSafetyStatus.UnsupportedVendor;
            message = "The detected display adapter vendor is unsupported or ambiguous.";
        }
        else if (environment.Vendors.Count > 1)
        {
            status = GraphicsSafetyStatus.MultiGpuAmbiguous;
            message = "A global graphics mutation cannot select a unique adapter on a multi-GPU system.";
        }
        else if (request.RequiresAdministrator && !environment.IsAdministrator)
        {
            status = GraphicsSafetyStatus.AdministratorRequired;
            message = "Administrator permission is required for this machine graphics resource.";
        }
        else if (!string.Equals(
                     environment.PreparedFingerprint,
                     environment.CurrentFingerprint,
                     StringComparison.Ordinal))
        {
            status = GraphicsSafetyStatus.DriftBlocked;
            message = "Graphics state changed after planning; execution is blocked.";
        }
        else if (!environment.HasRecoveryReceipt)
        {
            status = GraphicsSafetyStatus.RecoveryUnavailable;
            message = "No durable recovery receipt exists for the prepared graphics state.";
        }
        else if (request.Kind == GraphicsMutationKind.ExistingTypedRegistryCapability)
        {
            status = GraphicsSafetyStatus.ExistingOwnerRequired;
            message = "This setting must execute through its existing typed registry capability and orchestrator.";
        }
        else
        {
            status = GraphicsSafetyStatus.PolicyDenied;
            message = "Driver, display topology, DWM, MPO/TDR, routing, and vendor-private mutations are disabled in this release.";
        }

        return Task.FromResult(new GraphicsSafetyDecision(
            status,
            request.OperationId,
            request.Kind,
            false,
            message));
    }
}

public sealed class UnavailableGraphicsSafetyBoundary : IGraphicsSafetyBoundary
{
    public static UnavailableGraphicsSafetyBoundary Instance { get; } = new();

    private UnavailableGraphicsSafetyBoundary() { }

    public Task<GraphicsSafetyDecision> EvaluateAsync(
        GraphicsSafetyRequest request,
        GraphicsEnvironmentSnapshot environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = cancellationToken.IsCancellationRequested
            ? GraphicsSafetyStatus.Cancelled
            : GraphicsSafetyStatus.PolicyDenied;
        return Task.FromResult(new GraphicsSafetyDecision(
            status,
            request.OperationId,
            request.Kind,
            false,
            status == GraphicsSafetyStatus.Cancelled
                ? "Graphics safety evaluation was cancelled; no mutation was attempted."
                : "No approved graphics mutation platform is registered; the legacy entry is blocked."));
    }
}
