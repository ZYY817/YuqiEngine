namespace GameOptimizer.Core.Services;

public record OperationDependency(string OperationId, string DependsOnId, string Reason);

public record OperationConflict(string OperationId1, string OperationId2, string Reason);

public record DependencyCheckResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> MissingDependencies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OperationConflict> Conflicts { get; init; } = Array.Empty<OperationConflict>();
    public IReadOnlyList<string> ExecutionOrder { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public interface IOperationDependencyService
{
    void RegisterDependency(string operationId, string dependsOnId, string reason);
    void RegisterConflict(string operationId1, string operationId2, string reason);
    DependencyCheckResult ValidateOperations(IEnumerable<string> operationIds);
    IReadOnlyList<string> GetDependencies(string operationId);
    IReadOnlyList<string> GetConflicts(string operationId);
    IReadOnlyList<string> GetOptimalExecutionOrder(IEnumerable<string> operationIds);
}
