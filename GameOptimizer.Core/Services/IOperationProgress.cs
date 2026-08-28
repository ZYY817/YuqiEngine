namespace GameOptimizer.Core.Services;

public interface IOperationProgress
{
    void Report(ProgressInfo progress);
}

public record ProgressInfo
{
    public string OperationId { get; init; } = string.Empty;
    public string OperationName { get; init; } = string.Empty;
    public ProgressState State { get; init; }
    public int CurrentStep { get; init; }
    public int TotalSteps { get; init; }
    public double Percentage => TotalSteps > 0 ? (double)CurrentStep / TotalSteps * 100 : 0;
    public string? StatusMessage { get; init; }
    public TimeSpan? EstimatedRemaining { get; init; }

    public static ProgressInfo Starting(string operationId, string name, int totalSteps) =>
        new() { OperationId = operationId, OperationName = name, State = ProgressState.Starting, TotalSteps = totalSteps };

    public static ProgressInfo Running(string operationId, string name, int current, int total, string? status = null) =>
        new() { OperationId = operationId, OperationName = name, State = ProgressState.Running, CurrentStep = current, TotalSteps = total, StatusMessage = status };

    public static ProgressInfo Completed(string operationId, string name) =>
        new() { OperationId = operationId, OperationName = name, State = ProgressState.Completed, CurrentStep = 1, TotalSteps = 1 };

    public static ProgressInfo Failed(string operationId, string name, string error) =>
        new() { OperationId = operationId, OperationName = name, State = ProgressState.Failed, StatusMessage = error };

    public static ProgressInfo Cancelled(string operationId, string name, string message) =>
        new() { OperationId = operationId, OperationName = name, State = ProgressState.Cancelled, StatusMessage = message };
}

public enum ProgressState
{
    Pending,
    Starting,
    Running,
    Completed,
    Failed,
    Cancelled
}

public class AggregateProgress : IOperationProgress
{
    private readonly Action<ProgressInfo> _handler;
    private readonly object _lock = new();
    private readonly Dictionary<string, ProgressInfo> _operations = new();

    public AggregateProgress(Action<ProgressInfo> handler)
    {
        _handler = handler;
    }

    public void Report(ProgressInfo progress)
    {
        lock (_lock)
        {
            _operations[progress.OperationId] = progress;
        }
        _handler(progress);
    }

    public (int Completed, int Total, int Failed) GetSummary()
    {
        lock (_lock)
        {
            var completed = _operations.Values.Count(p => p.State == ProgressState.Completed);
            var failed = _operations.Values.Count(p => p.State == ProgressState.Failed);
            return (completed, _operations.Count, failed);
        }
    }
}
