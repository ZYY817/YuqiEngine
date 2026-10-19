// File: GameOptimizer.Shell/Services/GameProcessPriorityService.cs
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services;

public interface IGameProcessPriorityService
{
    void StartMonitoring();
    void StopMonitoring();
    bool SetProcessPriority(int processId, ProcessPriorityClass priority);
    bool RestoreProcessPriority(int processId);
}

/// <summary>
/// Legacy synchronous priority API retained only as a fail-closed compatibility facade.
/// Automatic process events cannot obtain per-plan confirmation or return a durable
/// ProcessTuningOwner receipt, so this type never subscribes to mutation callbacks.
/// </summary>
public sealed class GameProcessPriorityService : IGameProcessPriorityService, IDisposable
{
    private readonly ILogger<GameProcessPriorityService> _logger;
    private bool _isMonitoring;
    private bool _disposed;

    public GameProcessPriorityService(
        IGameProcessMonitorService monitorService,
        ILogger<GameProcessPriorityService> logger)
    {
        ArgumentNullException.ThrowIfNull(monitorService);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void StartMonitoring()
    {
        if (_isMonitoring)
        {
            return;
        }

        _isMonitoring = true;
        _logger.LogWarning(
            "[PriorityService] Automatic runtime priority is fail-closed; use ProcessTuningOwner with explicit confirmation.");
    }

    public void StopMonitoring()
    {
        if (!_isMonitoring)
        {
            return;
        }

        _isMonitoring = false;
        _logger.LogInformation("[PriorityService] Fail-closed priority monitor stopped");
    }

    public bool SetProcessPriority(int processId, ProcessPriorityClass priority)
    {
        _ = processId;
        _ = priority;
        _logger.LogWarning(
            "[PriorityService] Direct runtime priority write is blocked; use ProcessTuningOwner.");
        return false;
    }

    public bool RestoreProcessPriority(int processId)
    {
        _ = processId;
        _logger.LogWarning(
            "[PriorityService] PID-only restore is blocked; restore through a durable ProcessTuningOwner receipt.");
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopMonitoring();
    }
}
