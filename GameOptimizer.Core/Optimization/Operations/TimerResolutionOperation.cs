using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Requests a higher system timer resolution for the current process using NtSetTimerResolution.
/// </summary>
public sealed class TimerResolutionOperation : IOptimizationOperation
{
    private readonly uint _desiredResolution;

    private sealed record TimerSnapshot(uint RequestedResolution, uint PreviousCurrentResolution, DateTime RequestedAtUtc);

    public TimerResolutionOperation(string id, string title, string description, string category, TimeSpan resolution)
    {
        if (resolution <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        Id = id;
        Title = title;
        Description = description;
        Category = category;
        _desiredResolution = (uint)(resolution.TotalSeconds * 10_000_000);
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Extreme;
    public string? Benefit { get; init; }
    public string? Drawback { get; init; } = "提高计时器分辨率会增加功耗并可能影响电池续航";
    public bool RequiresAdmin => false;
    public bool RequiresSnapshotForRestore => true;
    public bool RestoreMayLeaveStatusCompliant => true;
    public OperationType OperationType => OperationType.SystemApi;
    public string? ImpactWarning => "计时器分辨率设置仅对当前进程有效，进程退出后自动失效";

    public Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var status = QueryResolution();
        if (!status.Success)
        {
            return Task.FromResult(new OperationStatus(false, status.ErrorMessage ?? "无法查询计时器分辨率"));
        }

        var compliant = status.Current <= _desiredResolution;
        var details = $"当前={Format(status.Current)} (最小={Format(status.Minimum)}, 最大={Format(status.Maximum)})";
        return Task.FromResult(new OperationStatus(compliant, details));
    }

    public async Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        var requestIssued = false;

        try
        {
            var before = QueryResolution();
            if (!before.Success)
            {
                return new OperationInvokeResult(false, false, before.ErrorMessage ?? "无法查询计时器分辨率");
            }

            if (before.Current <= _desiredResolution)
            {
                return new OperationInvokeResult(true, false, "计时器分辨率已满足目标");
            }

            var nativeResult = NtSetTimerResolution(_desiredResolution, true, out _);
            if (nativeResult != 0)
            {
                return new OperationInvokeResult(false, false, $"NtSetTimerResolution 失败，错误码 {nativeResult}");
            }

            requestIssued = true;

            var after = QueryResolution();
            if (!after.Success)
            {
                ReleaseRequestBestEffort();
                return new OperationInvokeResult(false, false, after.ErrorMessage ?? "无法验证计时器分辨率");
            }

            if (after.Current > _desiredResolution)
            {
                ReleaseRequestBestEffort();
                return new OperationInvokeResult(false, false, "设置后分辨率仍高于目标值");
            }

            try
            {
                var snapshot = new TimerSnapshot(_desiredResolution, before.Current, DateTime.UtcNow);
                await context.SaveSnapshotAsync(Id, snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReleaseRequestBestEffort();
                return new OperationInvokeResult(false, false, $"保存计时器快照失败：{ex.Message}");
            }

            return new OperationInvokeResult(
                true,
                true,
                $"已请求 {Format(_desiredResolution)} 计时器分辨率（进程退出后设置会失效）");
        }
        catch (Exception ex)
        {
            if (requestIssued)
            {
                ReleaseRequestBestEffort();
            }

            return Utilities.OperationErrorAnalyzer.HandleOperationException(ex, "CPU优化");
        }
    }

    public async Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await context.LoadSnapshotAsync<TimerSnapshot>(Id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return new OperationInvokeResult(false, false, "没有可恢复的计时器快照");
            }

            var nativeResult = NtSetTimerResolution(snapshot.RequestedResolution, false, out _);
            if (nativeResult != 0)
            {
                return new OperationInvokeResult(false, false, $"NtSetTimerResolution 恢复失败，错误码 {nativeResult}");
            }

            var after = QueryResolution();
            if (!after.Success)
            {
                return new OperationInvokeResult(false, false, after.ErrorMessage ?? "无法验证计时器分辨率");
            }

            if (after.Current <= snapshot.RequestedResolution)
            {
                return new OperationInvokeResult(
                    true,
                    true,
                    "已释放计时器分辨率请求");
            }

            return new OperationInvokeResult(true, true, "已释放计时器请求");
        }
        catch (Exception ex)
        {
            return Utilities.OperationErrorAnalyzer.HandleOperationException(ex, "CPU优化");
        }
    }

    private static (bool Success, uint Minimum, uint Maximum, uint Current, string? ErrorMessage) QueryResolution()
    {
        var status = NtQueryTimerResolution(out var minimum, out var maximum, out var current);
        return status == 0
            ? (true, minimum, maximum, current, null)
            : (false, 0, 0, 0, $"NtQueryTimerResolution 失败，错误码 {status}");
    }

    private void ReleaseRequestBestEffort()
    {
        try
        {
            NtSetTimerResolution(_desiredResolution, false, out _);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string Format(uint hundredNs) => $"{hundredNs / 10_000.0:F2} µs";

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desiredResolution, [MarshalAs(UnmanagedType.I1)] bool setResolution, out uint currentResolution);
}
