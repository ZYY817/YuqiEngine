namespace GameOptimizer.Core.Optimization;

public enum OperationSupportState
{
    Supported,
    Unsupported,
    AlternativeAvailable,
    PermissionRequired,
    NotApplicable,
    ManagedByPolicy,
    Conflict,
    Unknown
}

public sealed record OperationStatus(
    bool IsCompliant,
    string Details,
    bool RequiresReboot = false,
    bool IsSystemDefault = false,
    bool IsPending = false,
    bool RequiresRecovery = false,
    bool IsKnown = true,
    OperationSupportState SupportState = OperationSupportState.Supported,
    string? SupportReasonCode = null,
    string? Alternative = null,
    string? CompatibilityVariantId = null,
    string? CompatibilityVariantLabel = null,
    string? CompatibilityMethod = null,
    string? CompatibilityInformation = null)
{
    public static OperationStatus Aggregate(
        IReadOnlyList<OperationStatus> statuses,
        bool isCompliant,
        string compliantDetails,
        string nonCompliantDetails)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        var blocked = statuses.FirstOrDefault(status =>
            !status.IsKnown || status.SupportState != OperationSupportState.Supported);
        if (blocked is not null)
        {
            return blocked with { IsCompliant = false };
        }

        var compatibility = statuses.FirstOrDefault(status =>
            !string.IsNullOrWhiteSpace(status.CompatibilityVariantId));
        return new OperationStatus(
            isCompliant,
            isCompliant ? compliantDetails : nonCompliantDetails,
            RequiresReboot: statuses.Any(status => status.RequiresReboot),
            IsSystemDefault: statuses.All(status => status.IsSystemDefault),
            IsPending: statuses.Any(status => status.IsPending),
            RequiresRecovery: statuses.Any(status => status.RequiresRecovery),
            IsKnown: true,
            CompatibilityVariantId: compatibility?.CompatibilityVariantId,
            CompatibilityVariantLabel: compatibility?.CompatibilityVariantLabel,
            CompatibilityMethod: compatibility?.CompatibilityMethod,
            CompatibilityInformation: compatibility?.CompatibilityInformation);
    }
}
