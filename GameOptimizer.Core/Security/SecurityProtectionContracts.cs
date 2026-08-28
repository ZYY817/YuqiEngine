namespace GameOptimizer.Core.Security;

public interface ISecurityProtectionInspector
{
    Task<SecurityProtectionEnvironment> InspectAsync(
        SecurityProtectionArea area,
        CancellationToken cancellationToken);
}

public interface ISecurityProtectionOwner
{
    Task<SecurityProtectionDecision> EvaluateAsync(
        SecurityProtectionRequest request,
        CancellationToken cancellationToken);
}
