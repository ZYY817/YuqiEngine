using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Services;

/// <summary>
/// Keeps internal capability identifiers out of user-facing copy. Identifiers remain
/// available to diagnostics and receipts, but UI always gets a localized display name.
/// </summary>
public static class CapabilityDisplayFormatter
{
    public static string GetDisplayName(string? capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
            return GetFallbackName();

        if (string.Equals(
                capabilityId,
                GameOptimizer.Core.Services.Memory.MemoryCleaningCapabilityIds.ProcessTermination,
                StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Instance.CurrentLanguage != "zh-CN"
                ? "End selected processes"
                : "结束所选进程";
        }

        var isEnglish = LocalizationService.Instance.CurrentLanguage != "zh-CN";
        var title = OptimizationTranslations.Instance.GetTitle(capabilityId, capabilityId, isEnglish);
        return string.Equals(title, capabilityId, StringComparison.Ordinal)
            ? GetFallbackName()
            : title;
    }

    private static string GetFallbackName() =>
        LocalizationService.Instance.CurrentLanguage != "zh-CN"
            ? "System setting"
            : "系统设置项";
}
