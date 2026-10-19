using System;
using System.Collections.Generic;
using System.Linq;

namespace GameOptimizer.Core.Services.Snapshot;

/// <summary>
/// Explicit coverage boundary for an ordinary settings snapshot.
/// </summary>
public sealed record SnapshotCoverageReport(
    int CatalogOperationCount,
    int TypedExecutableOperationCount,
    int CapturedOperationCount,
    int CapturedScalarValueCount,
    int ManagedFailClosedOperationCount,
    IReadOnlyList<string> UncapturedOperationIds)
{
    public bool IsComplete => CatalogOperationCount > 0
        && CapturedOperationCount == CatalogOperationCount
        && UncapturedOperationIds.Count == 0;

    public string Summary => IsComplete
        ? $"已覆盖 {CapturedOperationCount}/{CatalogOperationCount} 项"
        : $"已覆盖 {CapturedOperationCount}/{CatalogOperationCount} 项；未覆盖 {UncapturedOperationIds.Count} 项";
}

internal static class SnapshotCoverageAnalyzer
{
    internal static SnapshotCoverageReport Analyze(
        IReadOnlyList<string> operationIds,
        IReadOnlySet<string> typedSnapshotIds,
        IReadOnlySet<string> typedExecutableIds,
        IReadOnlySet<string> managedFailClosedIds,
        int scalarValueCount)
    {
        var distinctOperationIds = operationIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var captured = distinctOperationIds
            .Where(typedSnapshotIds.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uncaptured = distinctOperationIds
            .Where(id => !captured.Contains(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SnapshotCoverageReport(
            distinctOperationIds.Length,
            distinctOperationIds.Count(typedExecutableIds.Contains),
            captured.Count,
            scalarValueCount,
            distinctOperationIds.Count(managedFailClosedIds.Contains),
            uncaptured);
    }
}
