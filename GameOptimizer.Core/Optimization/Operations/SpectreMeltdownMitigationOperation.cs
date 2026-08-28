using GameOptimizer.Core.Security;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class SpectreMeltdownMitigationOperation : SecurityMitigationCompatibilityOperation
{
    public SpectreMeltdownMitigationOperation(string id, string title, string description, string category)
        : base(
            SecurityProtectionArea.SpectreMeltdownMitigations,
            id,
            title,
            description,
            "关闭 Spectre/Meltdown 缓解会使系统暴露于已知侧信道攻击。")
    {
    }
}
