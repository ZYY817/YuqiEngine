namespace GameOptimizer.Core.GraphicsSafety;

public static class GraphicsSafetyIdentity
{
    public const string BoundaryId = "gameoptimizer.graphics-safety";
    public const string ResourceKey = "graphics:machine:driver-display-state";
}

public enum GraphicsVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Virtual
}

public enum GraphicsMutationKind
{
    ExistingTypedRegistryCapability,
    VendorPrivateSetting,
    DriverLifecycle,
    DisplayTopology,
    DwmLifecycle,
    MpoOrTdrOverride,
    GlobalGpuRouting
}

public enum GraphicsSafetyStatus
{
    ExistingOwnerRequired,
    PolicyDenied,
    PolicyManaged,
    RemoteSessionDenied,
    VirtualMachineDenied,
    UnsupportedVendor,
    MultiGpuAmbiguous,
    DriverVersionUnknown,
    AdministratorRequired,
    DriftBlocked,
    RecoveryUnavailable,
    Cancelled
}

public sealed record GraphicsSafetyRequest(
    string OperationId,
    GraphicsMutationKind Kind,
    bool RequiresAdministrator = true);

public sealed record GraphicsEnvironmentSnapshot(
    IReadOnlyList<GraphicsVendor> Vendors,
    bool IsRemoteSession,
    bool IsVirtualMachine,
    bool IsPolicyManaged,
    bool IsAdministrator,
    string? DriverVersion,
    string PreparedFingerprint,
    string CurrentFingerprint,
    bool HasRecoveryReceipt);

public sealed record GraphicsSafetyDecision(
    GraphicsSafetyStatus Status,
    string OperationId,
    GraphicsMutationKind Kind,
    bool Allowed,
    string Message,
    string BoundaryId = GraphicsSafetyIdentity.BoundaryId,
    string ResourceKey = GraphicsSafetyIdentity.ResourceKey);
