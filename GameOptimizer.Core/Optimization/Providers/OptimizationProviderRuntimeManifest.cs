using GameOptimizer.Core.FileShellSafety;
using GameOptimizer.Core.GraphicsSafety;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.PowerManagement;
using GameOptimizer.Core.Security;

namespace GameOptimizer.Core.Optimization.Providers;

/// <summary>
/// Per-operation routing evidence derived from the catalog object actually returned by a provider.
/// It intentionally classifies runtime types instead of counting source construction sites.
/// </summary>
public enum OptimizationOperationRoute
{
    TypedExecutable,
    ManagedFailClosed,
    Unknown
}

public sealed record OptimizationOperationManifestEntry(
    string ModuleKey,
    string OperationId,
    OptimizationOperationRoute Route,
    string RuntimeType);

public static class OptimizationProviderRuntimeManifest
{
    public static IReadOnlyList<OptimizationOperationManifestEntry> Create(
        IOptimizationOperationProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var entries = provider.GetModules()
            .SelectMany(module => module.Operations.Select(operation => new OptimizationOperationManifestEntry(
                module.Key,
                operation.Id,
                Classify(operation),
                operation.GetType().FullName ?? operation.GetType().Name)))
            .OrderBy(entry => entry.OperationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (entries.Select(entry => entry.OperationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
        {
            throw new InvalidOperationException("Optimization runtime manifest contains duplicate operation IDs.");
        }

        return entries;
    }

    private static OptimizationOperationRoute Classify(IOptimizationOperation operation) =>
        operation switch
        {
            OrchestratedChangeOperation => OptimizationOperationRoute.TypedExecutable,
            PolicyDeniedOptimizationOperation => OptimizationOperationRoute.ManagedFailClosed,
            FailClosedServiceOptimizationOperation => OptimizationOperationRoute.ManagedFailClosed,
            FailClosedSecurityOptimizationOperation => OptimizationOperationRoute.ManagedFailClosed,
            FailClosedGraphicsOptimizationOperation => OptimizationOperationRoute.ManagedFailClosed,
            FailClosedPowerOptimizationOperation => OptimizationOperationRoute.ManagedFailClosed,
            FailClosedFileShellOptimizationOperation => OptimizationOperationRoute.ManagedFailClosed,
            _ => OptimizationOperationRoute.Unknown
        };
}
