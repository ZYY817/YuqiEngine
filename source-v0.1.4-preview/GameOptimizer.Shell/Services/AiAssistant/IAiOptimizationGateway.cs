// File: GameOptimizer.Shell/Services/AiAssistant/IAiOptimizationGateway.cs
namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>
/// AI 助手与优化引擎之间的只读/写操作网关。
/// 与 DashboardViewModel 解耦，测试可注入假实现；
/// 写操作必须走与 UI 开关完全相同的确认/快照/回读管线。
/// </summary>
public interface IAiOptimizationGateway
{
    /// <summary>模块概览：键、标题、已应用/总数。</summary>
    IReadOnlyList<AiModuleSummary> ListModules();

    /// <summary>指定模块（null=全部）的操作清单：id、标题、风险、状态。</summary>
    IReadOnlyList<AiOperationSummary> ListOperations(string? moduleKey);

    /// <summary>单项操作的详细状态（不存在返回 null）。</summary>
    AiOperationDetail? GetOperationDetail(string operationId);

    /// <summary>系统概要（OS、CPU、内存、GPU），语言无关。</summary>
    string GetSystemOverview();

    /// <summary>
    /// 应用指定操作（走完整确认/快照/回读管线）。
    /// 调用方必须已获得用户确认——本方法不内嵌 UI 确认。
    /// </summary>
    Task<AiWriteResult> ApplyAsync(IReadOnlyList<string> operationIds, CancellationToken cancellationToken);

    /// <summary>恢复指定操作，同样走完整回读管线。</summary>
    Task<AiWriteResult> RestoreAsync(IReadOnlyList<string> operationIds, CancellationToken cancellationToken);
}

public sealed record AiModuleSummary(string Key, string Title, int AppliedCount, int TotalCount);

public sealed record AiOperationSummary(
    string OperationId,
    string Title,
    string ModuleKey,
    string RiskLevel,
    bool IsApplied,
    bool CanApply,
    bool CanRestore);

public sealed record AiOperationDetail(
    AiOperationSummary Summary,
    string? Description,
    string? CurrentValue,
    string? SuggestedValue,
    string? ImpactWarning);

/// <summary>写操作结果摘要，回填给 AI 作为工具结果。</summary>
public sealed record AiWriteResult(
    bool Succeeded,
    int SucceededCount,
    int FailedCount,
    IReadOnlyList<string> FailedOperationIds,
    string Message);
