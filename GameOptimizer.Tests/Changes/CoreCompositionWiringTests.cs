using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Extensions;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

/// <summary>
/// Proves the production DI wiring gap is closed: AddGameOptimizerCore now constructs the
/// built-in provider with a live ChangeOrchestrator (and the confirmation authority), so a
/// typed operation no longer fails closed with the "must run through DI" marker.
/// </summary>
public sealed class CoreCompositionWiringTests
{
    [Fact]
    public async Task CoreComposition_TypedOperationRoutesThroughRealOrchestrator()
    {
        var services = new ServiceCollection();
        services.AddGameOptimizerCore();

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IOptimizationOperationProvider>();
        var typed = catalog
            .GetModules()
            .SelectMany(module => module.Operations)
            .OfType<OrchestratedChangeOperation>()
            .ToArray();

        Assert.NotEmpty(typed);

        // Core-only composition registers no IChangeCapability, so a real orchestrator
        // routes the typed operation to "capability.unknown" instead of the fail-closed
        // "composition.missing" marker emitted by the legacy default-ctor path.
        var status = await typed[0].GetStatusAsync(CancellationToken.None);
        Assert.DoesNotContain("该迁移能力必须通过应用依赖注入容器执行", status.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("composition.missing", status.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreComposition_RegistersMandatoryOrchestratorDependencies()
    {
        var services = new ServiceCollection();
        services.AddGameOptimizerCore();

        using var provider = services.BuildServiceProvider();

        // ChangeOrchestrator requires an IChangeExecutionGate; Core must supply a fail-closed
        // default so the orchestrator is constructible even before the Shell baseline gate.
        var orchestrator = provider.GetRequiredService<IChangeOrchestrator>();
        Assert.IsType<GameOptimizer.Core.Services.Changes.ChangeOrchestrator>(orchestrator);

        var gate = provider.GetRequiredService<IChangeExecutionGate>();
        Assert.NotNull(gate);
    }

    [Fact]
    public async Task CoreComposition_DefaultGateFailsClosedForApplyButAllowsRestore()
    {
        var services = new ServiceCollection();
        services.AddGameOptimizerCore();

        using var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<IChangeExecutionGate>();

        var apply = await gate.EnsureCanExecuteAsync("test.capability", ChangeExecutionIntent.Apply);
        Assert.Equal(ChangeExecutionGateDecisionKind.BlockedNeedBaseline, apply.Kind);

        var restore = await gate.EnsureCanExecuteAsync("test.capability", ChangeExecutionIntent.Restore);
        Assert.True(restore.IsAllowed);
    }
}
