// File: GameOptimizer.Shell/Services/AiAssistant/DashboardAiGateway.cs
using System.Text;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>
/// IAiOptimizationGateway 的生产实现：桥接到 DashboardViewModel，
/// 写操作复用与 UI 开关完全相同的 ApplyOperationsByIdAsync /
/// RestoreOperationsByIdAsync 管线（快照/确认/回读/恢复材料一致）。
/// </summary>
public sealed class DashboardAiGateway : IAiOptimizationGateway
{
    private readonly DashboardViewModel _dashboard;
    private readonly IHardwareDetectionService? _hardwareDetection;

    public DashboardAiGateway(DashboardViewModel dashboard, IHardwareDetectionService? hardwareDetection = null)
    {
        _dashboard = dashboard;
        _hardwareDetection = hardwareDetection;
    }

    public IReadOnlyList<AiModuleSummary> ListModules() =>
        _dashboard.OptimizationModules
            .Where(m => m.SupportsExecution)
            .Select(m => new AiModuleSummary(
                m.Key,
                m.Title,
                m.Toggles.Count(t => t.IsApplied),
                m.Toggles.Count))
            .ToArray();

    public IReadOnlyList<AiOperationSummary> ListOperations(string? moduleKey)
    {
        var modules = string.IsNullOrWhiteSpace(moduleKey)
            ? _dashboard.OptimizationModules.Where(m => m.SupportsExecution)
            : _dashboard.OptimizationModules.Where(m =>
                m.SupportsExecution && MatchesModuleKey(m, moduleKey));

        return modules.SelectMany(m => m.Toggles.Select(t => ToSummary(m, t)))
            .Where(s => !string.IsNullOrWhiteSpace(s.OperationId))
            .ToArray();
    }

    public AiOperationDetail? GetOperationDetail(string operationId)
    {
        foreach (var module in _dashboard.OptimizationModules.Where(m => m.SupportsExecution))
        {
            var toggle = module.Toggles.FirstOrDefault(t =>
                string.Equals(t.OperationId, operationId, StringComparison.OrdinalIgnoreCase));
            if (toggle is null)
                continue;

            return new AiOperationDetail(
                ToSummary(module, toggle),
                toggle.Description,
                toggle.ValuePreview ?? toggle.RuntimeStatusDetails,
                toggle.OptimizedValue ?? toggle.TargetValue,
                toggle.ImpactWarning ?? toggle.Drawback);
        }
        return null;
    }

    public string GetSystemOverview()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($"Machine: {Environment.MachineName}");
        sb.AppendLine($"CPU cores: {Environment.ProcessorCount}");

        var snapshot = _dashboard.Snapshot;
        if (snapshot.MemoryTotalGb > 0)
        {
            sb.AppendLine($"Memory: {snapshot.MemoryUsageGb:F1} / {snapshot.MemoryTotalGb:F1} GB in use");
        }
        sb.AppendLine($"CPU usage: {snapshot.CpuUsage:F0}%");
        if (snapshot.GpuUsage > 0)
            sb.AppendLine($"GPU usage: {snapshot.GpuUsage:F0}%");

        try
        {
            var hardware = _hardwareDetection?.GetOrDetectAsync().GetAwaiter().GetResult();
            if (hardware is not null)
            {
                if (!string.IsNullOrWhiteSpace(hardware.CpuName))
                    sb.AppendLine($"CPU: {hardware.CpuName}");
                if (!string.IsNullOrWhiteSpace(hardware.GpuName))
                    sb.AppendLine($"GPU: {hardware.GpuName}");
            }
        }
        catch { /* 硬件检测失败不阻断概要输出 */ }

        return sb.ToString();
    }

    // Why: 写管线会更新 UI 绑定的集合与状态，必须和手动点击开关一样在 UI 线程执行，
    // 否则后台线程操作 ObservableCollection 会造成界面假死/跨线程异常。
    public async Task<AiWriteResult> ApplyAsync(IReadOnlyList<string> operationIds, CancellationToken cancellationToken)
    {
        var result = await RunOnUiAsync(() => _dashboard.ApplyOperationsByIdAsync(operationIds));
        return ToWriteResult(result);
    }

    public async Task<AiWriteResult> RestoreAsync(IReadOnlyList<string> operationIds, CancellationToken cancellationToken)
    {
        var result = await RunOnUiAsync(() => _dashboard.RestoreOperationsByIdAsync(operationIds));
        return ToWriteResult(result);
    }

    private static Task<TResult> RunOnUiAsync<TResult>(Func<Task<TResult>> work)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return work();
        return dispatcher.InvokeAsync(work).Task.Unwrap();
    }

    private static AiWriteResult ToWriteResult(GameOptimizer.Core.Optimization.OptimizationBatchResult result) =>
        new(
            result.Success,
            result.Operations.Count(o => o.Success),
            result.Operations.Count(o => !o.Success),
            result.Operations.Where(o => !o.Success).Select(o => o.OperationId).ToArray(),
            string.Join("；", result.Operations.Select(o => $"{o.Title}: {(o.Success ? "成功" : "失败")} {o.Message}")));

    private static bool MatchesModuleKey(GameOptimizer.Shell.Models.OptimizationModuleDefinition module, string moduleKey) =>
        string.Equals(module.Key, moduleKey, StringComparison.OrdinalIgnoreCase);

    private static AiOperationSummary ToSummary(
        GameOptimizer.Shell.Models.OptimizationModuleDefinition module,
        GameOptimizer.Shell.Models.OptimizationToggleModel toggle) =>
        new(
            toggle.OperationId ?? string.Empty,
            toggle.Title ?? toggle.OperationId ?? string.Empty,
            module.Key,
            toggle.RiskLevel.ToString(),
            toggle.IsApplied,
            toggle.CanApply,
            toggle.CanRestore);
}
