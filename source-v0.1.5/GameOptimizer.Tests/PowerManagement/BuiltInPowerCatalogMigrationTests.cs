// File: GameOptimizer.Tests/PowerManagement/BuiltInPowerCatalogMigrationTests.cs
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.PowerManagement;

public sealed class BuiltInPowerCatalogMigrationTests
{
    private static readonly string[] ExpectedTypedIds =
    [
        "cpu.disable_boost_mode",
        "cpu.disable_core_parking",
        "cpu.disable_pstates",
        "cpu.processor_boost_mode",
        "intel.disable_cstates",
        "peripheral.usb.suspend",
        "power.class1_efficiency_threshold",
        "power.class1_perf_level",
        "power.disable_aspm",
        "power.disable_throttle",
        "power.disable_usb_suspend",
        "power.hetero_thread_scheduling",
        "power.idle_demote_threshold",
        "power.perf_increase_threshold",
        "power.perf_time_check",
        "power.processor_min_state_100",
        "services.enable_high_performance_mode"
    ];

    [Fact]
    public void ProviderContainsExactlyOneTypedOrchestratedEntryForEveryLegacyPowerId()
    {
        var powerOperations = GetPowerOperations();

        Assert.Equal(ExpectedTypedIds, powerOperations.Select(operation => operation.Id).Order().ToArray());
        Assert.Equal(powerOperations.Length, powerOperations.Select(operation => operation.Id).Distinct().Count());
        Assert.All(powerOperations, operation =>
        {
            Assert.IsAssignableFrom<OrchestratedChangeOperation>(operation);
            Assert.True(operation.RequiresExplicitConfirmation);
            Assert.True(operation.RequiresSnapshotForRestore);
            Assert.True(operation.RequiresAdmin);
        });

        // Every migrated id maps to either the typed power-setting owner or the scheme-switch owner.
        var schemeSwitch = Assert.Single(powerOperations, operation => operation.Id == "services.enable_high_performance_mode");
        Assert.IsType<OrchestratedPowerSchemeOperation>(schemeSwitch);

        Assert.Equal(16, powerOperations.Count(operation => operation is OrchestratedPowerSettingOperation));
    }

    [Fact]
    public void ProviderDoesNotExposeAnyLegacyPowerWriterType()
    {
        var operations = GetPowerOperations();
        var forbiddenTypes = new[]
        {
            typeof(PowerSettingIndexOperation),
            typeof(PowerPlanOperation),
            typeof(CoreParkingDisableOperation),
            typeof(BasePowerPlanOperation),
            typeof(DisablePStatesOperation),
            typeof(DisableIntelCStatesOperation)
        };

        Assert.All(operations, operation =>
            Assert.DoesNotContain(forbiddenTypes, type => type.IsAssignableFrom(operation.GetType())));
    }

    [Fact]
    public async Task TypedEntriesAreCompositionOwnedAndNeverReportFailClosed()
    {
        // The default constructor wires every migrated operation to UnavailableChangeOrchestrator,
        // which fails closed at composition level — never through the old FailClosedPower owner.
        foreach (var operation in GetPowerOperations())
        {
            var status = await operation.GetStatusAsync(default);
            Assert.False(status.IsCompliant);
            Assert.DoesNotContain("typed Power Owner", status.Details, StringComparison.Ordinal);
        }
    }

    private static IOptimizationOperation[] GetPowerOperations() =>
        new BuiltInOptimizationOperationProvider()
            .GetModules()
            .SelectMany(module => module.Operations)
            .Where(operation => operation.OperationType == OperationType.PowerPlan)
            .OrderBy(operation => operation.Id, StringComparer.Ordinal)
            .ToArray();
}
