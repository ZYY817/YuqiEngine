// File: GameOptimizer.Shell/Services/AiAssistant/AiSystemPromptComposer.cs
namespace GameOptimizer.Shell.Services.AiAssistant;

/// <summary>AI 助手的交互节奏。</summary>
public enum AiInteractionMode
{
    /// <summary>问诊分轮：先诊断、问清症状、分级清单、逐步确认执行（默认）。</summary>
    Guided,
    /// <summary>高效全量：一次诊断并输出完整方案清单，用户挑编号执行。</summary>
    FullBatch
}

/// <summary>
/// 系统提示词组装：角色文件（用户可编辑）+ 交互模式规则 + 工具协议约束。
/// </summary>
public static class AiSystemPromptComposer
{
    public const string ModeSettingGuided = "guided";
    public const string ModeSettingFullBatch = "full";

    public static string Compose(string skillContext, AiInteractionMode mode)
    {
        var modeRule = mode == AiInteractionMode.FullBatch
            ? "## 当前节奏：高效全量\n用户选择了一次给全方案：先只读诊断，随后直接输出完整分级方案清单（编号+风险+代价+验证方法），等用户挑编号后再逐个执行，不在中途反复问诊。"
            : "## 当前节奏：问诊分轮\n用户选择了逐步来：每轮只做一两件事——诊断→问 2~3 个关键问题→出分级清单→等用户选→执行→复测。不要在一轮里塞太多操作。";

        return skillContext.Trim()
            + "\n\n" + modeRule
            + "\n\n## 工具协议（硬性）\n"
            + "- 写操作（apply_operations/restore_operations）只在你已经征得用户同意后调用；工具会再弹一次用户确认卡。\n"
            + "- 不在工具结果里的操作项不要编造；找不到的 operation_id 就如实告诉用户。\n"
            + "- 诊断数据优先用 collect_diagnostics / get_system_overview 工具获取真实值，不要猜测系统状态。";
    }

    public static AiInteractionMode ParseMode(string? setting) =>
        string.Equals(setting, ModeSettingFullBatch, StringComparison.OrdinalIgnoreCase)
            ? AiInteractionMode.FullBatch
            : AiInteractionMode.Guided;

    public static string ToSetting(AiInteractionMode mode) =>
        mode == AiInteractionMode.FullBatch ? ModeSettingFullBatch : ModeSettingGuided;
}
