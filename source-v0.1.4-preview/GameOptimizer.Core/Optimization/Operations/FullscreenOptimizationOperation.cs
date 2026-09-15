using GameOptimizer.Core.GraphicsSafety;

namespace GameOptimizer.Core.Optimization.Operations;

public sealed class FullscreenOptimizationOperation : FailClosedGraphicsOptimizationOperation
{
    public FullscreenOptimizationOperation(string id, string title, string description, string category)
        : base(
            id,
            $"{title}（安全阻断）",
            $"{description}；旧兼容类不再直接写 GameConfigStore，请使用现有 fps.disable_fullscreen_optimizations typed capability。",
            GraphicsMutationKind.ExistingTypedRegistryCapability,
            "HDR、覆盖层、无边框窗口与新游戏兼容性可能下降。")
    {
        _ = category;
    }
}
