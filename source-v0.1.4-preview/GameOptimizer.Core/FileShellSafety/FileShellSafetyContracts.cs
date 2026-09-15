namespace GameOptimizer.Core.FileShellSafety;

public interface IFileShellSafetyInspector
{
    Task<FileShellSafetyInspection> InspectAsync(
        FileShellSafetyRequest request,
        CancellationToken cancellationToken);
}

public interface IFileShellSafetyOwner
{
    Task<FileShellSafetyDecision> EvaluateAsync(
        FileShellSafetyRequest request,
        CancellationToken cancellationToken);
}
