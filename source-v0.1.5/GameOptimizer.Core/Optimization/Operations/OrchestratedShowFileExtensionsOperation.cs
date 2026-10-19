using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class OrchestratedShowFileExtensionsOperation : OrchestratedRegistryDwordOperation
{
    public const string OperationId = "registry.show_file_extensions";

    public OrchestratedShowFileExtensionsOperation(
        IChangeOrchestrator orchestrator,
        IChangeConfirmationAuthority? confirmationAuthority = null)
        : base(
            orchestrator,
            OperationId,
            "显示文件扩展名",
            "HideFileExt=0，使所有文件始终显示其扩展名。",
            "资源管理器界面",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "HideFileExt",
            0,
            OperationRiskLevel.Safe,
            "识别文件真实类型，防止伪装成文档的恶意程序",
            "无明显副作用",
            confirmationAuthority,
            requiresExplicitConfirmation: true)
    {
    }
}
