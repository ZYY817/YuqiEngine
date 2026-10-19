// File: GameOptimizer.Core/Services/CoreAffinityService.cs
using System.Diagnostics;
using GameOptimizer.Core.Models;

namespace GameOptimizer.Core.Services;

/// <summary>
/// Compatibility facade for callers that have not migrated to IProcessTuningOwner.
/// Runtime process mutation is intentionally unavailable here: a PID and an in-memory
/// before value cannot provide the identity, confirmation, receipt, or durable restore
/// guarantees required by the process-tuning safety chain.
/// </summary>
public sealed class CoreAffinityService
{
    private const string FailClosedMessage =
        "旧核心加速写入已阻断；请通过 ProcessTuningOwner 创建带进程 identity、显式确认和 durable receipt 的计划。";

    public CoreAffinityService(CpuTopologyService topologyService)
    {
        ArgumentNullException.ThrowIfNull(topologyService);
    }

    public AffinityResult ApplyAffinity(Process? process, GameCoreProfile profile)
    {
        _ = process;
        _ = profile;
        return AffinityResult.Failed(FailClosedMessage);
    }

    public AffinityResult ApplyAffinityByMask(
        Process? process,
        long affinityMask,
        ProcessPriorityLevel priority = ProcessPriorityLevel.High)
    {
        _ = process;
        _ = affinityMask;
        _ = priority;
        return AffinityResult.Failed(FailClosedMessage);
    }

    public bool RestoreAffinity(int processId)
    {
        _ = processId;
        return false;
    }

    public void RestoreAll()
    {
        // No state is captured by this fail-closed facade.
    }

    public bool IsOptimized(int processId)
    {
        _ = processId;
        return false;
    }

    public AffinityState? GetState(int processId)
    {
        _ = processId;
        return null;
    }

    public IReadOnlyList<AffinityState> GetAllStates() => Array.Empty<AffinityState>();
}

public sealed class AffinityState
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public long OriginalAffinity { get; set; }
    public long AppliedAffinity { get; set; }
    public ProcessPriorityClass OriginalPriority { get; set; }
    public ProcessPriorityClass AppliedPriority { get; set; }
    public DateTime AppliedTime { get; set; }
}

public sealed class AffinityResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public long AffinityMask { get; set; }
    public int CoreCount { get; set; }

    public static AffinityResult Success(string message, long mask = 0, int coreCount = 0) =>
        new() { IsSuccess = true, Message = message, AffinityMask = mask, CoreCount = coreCount };

    public static AffinityResult Failed(string message) =>
        new() { IsSuccess = false, Message = message };
}
