using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public enum EsportsPresetKind
{
    Conservative,
    Performance
}

public sealed record EsportsPresetRecommendation(
    IReadOnlyList<string> SelectedOperationIds,
    int PolicyExcludedCount,
    int IncompatibleCount,
    int UnavailableCount)
{
    public int BlockedCount => IncompatibleCount + UnavailableCount;
}

/// <summary>
/// Builds fail-closed esports recommendations. A preset may select only operations in an
/// explicit allowlist and only after the page has confirmed hardware compatibility,
/// operation registration and current-user permission.
/// </summary>
public sealed class EsportsPresetPlanner
{
    private static readonly IReadOnlySet<string> ConservativeAllowlist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gaming.mode.enable"
        };

    private static readonly IReadOnlySet<string> PerformanceAllowlist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gaming.mode.enable",
            "gaming.dvr.disable"
        };

    public IReadOnlyList<string> GetPreflightOperationIds(
        EsportsPresetKind kind,
        IEnumerable<GameModeOptimizationItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var allowlist = GetAllowlist(kind);

        return items
            .Where(item => allowlist.Contains(item.OperationId) && item.IsCompatible)
            .Select(static item => item.OperationId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public EsportsPresetRecommendation CreateRecommendation(
        EsportsPresetKind kind,
        IEnumerable<GameModeOptimizationItem> items,
        IEnumerable<string> executableOperationIds)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(executableOperationIds);

        var allItems = items
            .GroupBy(static item => item.OperationId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        var allowlist = GetAllowlist(kind);
        var executableIds = executableOperationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var presetItems = allItems.Where(item => allowlist.Contains(item.OperationId)).ToArray();

        var selectedIds = presetItems
            .Where(item => item.IsCompatible && executableIds.Contains(item.OperationId))
            .Select(static item => item.OperationId)
            .ToArray();

        return new EsportsPresetRecommendation(
            selectedIds,
            PolicyExcludedCount: allItems.Length - presetItems.Length,
            IncompatibleCount: presetItems.Count(static item => !item.IsCompatible),
            UnavailableCount: presetItems.Count(item => item.IsCompatible && !executableIds.Contains(item.OperationId)));
    }

    private static IReadOnlySet<string> GetAllowlist(EsportsPresetKind kind)
    {
        return kind switch
        {
            EsportsPresetKind.Conservative => ConservativeAllowlist,
            EsportsPresetKind.Performance => PerformanceAllowlist,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown esports preset kind.")
        };
    }
}
