using Microsoft.Extensions.Logging;

namespace GameOptimizer.Core.Services;

public sealed class OperationDependencyService : IOperationDependencyService
{
    private readonly ILogger<OperationDependencyService> _logger;
    private readonly Dictionary<string, List<OperationDependency>> _dependencies = new();
    private readonly Dictionary<string, List<OperationConflict>> _conflicts = new();
    private readonly object _lock = new();

    public OperationDependencyService(ILogger<OperationDependencyService> logger)
    {
        _logger = logger;
        RegisterBuiltInRules();
    }

    public void RegisterDependency(string operationId, string dependsOnId, string reason)
    {
        Guard.NotNullOrEmpty(operationId, nameof(operationId));
        Guard.NotNullOrEmpty(dependsOnId, nameof(dependsOnId));
        
        lock (_lock)
        {
            if (!_dependencies.ContainsKey(operationId))
                _dependencies[operationId] = new List<OperationDependency>();
            
            _dependencies[operationId].Add(new OperationDependency(operationId, dependsOnId, reason ?? string.Empty));
        }
    }

    public void RegisterConflict(string operationId1, string operationId2, string reason)
    {
        Guard.NotNullOrEmpty(operationId1, nameof(operationId1));
        Guard.NotNullOrEmpty(operationId2, nameof(operationId2));
        
        lock (_lock)
        {
            var conflict = new OperationConflict(operationId1, operationId2, reason ?? string.Empty);
            
            if (!_conflicts.ContainsKey(operationId1))
                _conflicts[operationId1] = new List<OperationConflict>();
            _conflicts[operationId1].Add(conflict);
            
            if (!_conflicts.ContainsKey(operationId2))
                _conflicts[operationId2] = new List<OperationConflict>();
            _conflicts[operationId2].Add(new OperationConflict(operationId2, operationId1, reason ?? string.Empty));
        }
    }

    public DependencyCheckResult ValidateOperations(IEnumerable<string> operationIds)
    {
        var opSet = new HashSet<string>(operationIds);
        var missing = new List<string>();
        var conflicts = new List<OperationConflict>();
        var warnings = new List<string>();

        lock (_lock)
        {
            foreach (var opId in opSet)
            {
                if (_dependencies.TryGetValue(opId, out var deps))
                {
                    foreach (var dep in deps)
                    {
                        if (!opSet.Contains(dep.DependsOnId))
                        {
                            missing.Add($"{opId} requires {dep.DependsOnId}: {dep.Reason}");
                        }
                    }
                }

                if (_conflicts.TryGetValue(opId, out var opConflicts))
                {
                    foreach (var conflict in opConflicts)
                    {
                        if (opSet.Contains(conflict.OperationId2) && 
                            !conflicts.Any(c => c.OperationId1 == conflict.OperationId2 && c.OperationId2 == conflict.OperationId1))
                        {
                            conflicts.Add(conflict);
                            warnings.Add($"Conflict: {conflict.OperationId1} and {conflict.OperationId2} - {conflict.Reason}");
                        }
                    }
                }
            }
        }

        var executionOrder = GetOptimalExecutionOrder(opSet);
        var isValid = missing.Count == 0 && conflicts.Count == 0;

        return new DependencyCheckResult
        {
            IsValid = isValid,
            MissingDependencies = missing,
            Conflicts = conflicts,
            ExecutionOrder = executionOrder,
            Warnings = warnings
        };
    }

    public IReadOnlyList<string> GetDependencies(string operationId)
    {
        if (string.IsNullOrEmpty(operationId)) return Array.Empty<string>();
        
        lock (_lock)
        {
            if (_dependencies.TryGetValue(operationId, out var deps))
                return deps.Select(d => d.DependsOnId).ToList();
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> GetConflicts(string operationId)
    {
        if (string.IsNullOrEmpty(operationId)) return Array.Empty<string>();
        
        lock (_lock)
        {
            if (_conflicts.TryGetValue(operationId, out var conflicts))
                return conflicts.Select(c => c.OperationId2).ToList();
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> GetOptimalExecutionOrder(IEnumerable<string> operationIds)
    {
        var opList = operationIds.ToList();
        var result = new List<string>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();

        foreach (var op in opList)
        {
            if (!visited.Contains(op))
                TopologicalSort(op, opList, visited, visiting, result);
        }

        // 不需要 Reverse - 依赖项已经在递归中先被添加
        return result;
    }

    private void TopologicalSort(string op, List<string> allOps, HashSet<string> visited, HashSet<string> visiting, List<string> result)
    {
        if (visiting.Contains(op))
        {
            _logger.LogWarning("[DependencyService] Circular dependency detected for {Op}", op);
            return;
        }

        if (visited.Contains(op)) return;

        visiting.Add(op);

        var deps = GetDependencies(op);
        foreach (var dep in deps.Where(d => allOps.Contains(d)))
        {
            TopologicalSort(dep, allOps, visited, visiting, result);
        }

        visiting.Remove(op);
        visited.Add(op);
        result.Add(op);
    }

    private void RegisterBuiltInRules()
    {
        // Power plan conflicts
        RegisterConflict("power.high_performance", "power.power_saver", "Cannot enable both high performance and power saver");
        
        // Service dependencies
        RegisterDependency("basic.disable_game_dvr", "basic.disable_game_bar", "Game DVR requires Game Bar to be disabled first");
        
        // Network conflicts
        RegisterConflict("network.disable_qos", "network.enable_qos_gaming", "QoS settings conflict");
    }
}
