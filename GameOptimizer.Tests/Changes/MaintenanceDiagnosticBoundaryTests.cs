using GameOptimizer.Core.Services.MaintenanceDiagnostics;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.MaintenanceDiagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class MaintenanceDiagnosticBoundaryTests
{
    private static readonly string[] RepairIds =
    [
        "repair.chkdsk",
        "repair.location_service",
        "repair.memory_service",
        "repair.search_index",
        "repair.storage_service",
        "repair.superfetch"
    ];

    [Fact]
    public void CatalogIsFixedAndUnique()
    {
        Assert.Equal(6, MaintenanceDiagnosticCatalog.All.Count);
        Assert.Equal(6, MaintenanceDiagnosticCatalog.All.Select(value => value.RepairItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(6, MaintenanceDiagnosticCatalog.All.Select(value => value.CapabilityId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(RepairIds.Order(), MaintenanceDiagnosticCatalog.All.Select(value => value.RepairItemId).Order());
    }

    [Fact]
    public async Task RegistryRoutesEveryCandidateToOneReadOnlyOwnerAndNeverWrites()
    {
        var platform = new FakePlatform();
        var owner = new MaintenanceDiagnosticOwner(platform);
        var registry = new RepairItemRegistry(maintenanceDiagnosticOwner: owner);
        registry.Initialize();

        foreach (var repairId in RepairIds)
        {
            var item = registry.GetById(repairId);
            Assert.NotNull(item);
            var result = await item.RepairAsync(null);
            Assert.False(result.Success);
            Assert.Contains("不会修改", result.Message, StringComparison.Ordinal);
        }

        Assert.Equal(6, platform.Reads);
        Assert.Equal(0, platform.Writes);
    }

    [Theory]
    [InlineData(MaintenanceDiagnosticOutcome.NotApplicable)]
    [InlineData(MaintenanceDiagnosticOutcome.PermissionDenied)]
    [InlineData(MaintenanceDiagnosticOutcome.Unknown)]
    public async Task DiagnosticFailureCategoriesRemainStructuredAndFailClosed(
        MaintenanceDiagnosticOutcome outcome)
    {
        var owner = new MaintenanceDiagnosticOwner(new FixedPlatform(
            MaintenanceDiagnosticSnapshot.Failed("repair.storage_service", outcome, outcome.ToString())));

        var snapshot = await owner.DiagnoseAsync("repair.storage_service", default);
        var result = await owner.RepairAsync("repair.storage_service", null, default);

        Assert.Equal(outcome, snapshot.Outcome);
        Assert.Null(snapshot.IsHealthy);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ServiceObservationDoesNotPretendThatExistenceMeansHealthy()
    {
        var snapshot = new MaintenanceDiagnosticSnapshot(
            "repair.superfetch",
            MaintenanceDiagnosticOutcome.Observed,
            null,
            "service:SysMain",
            new Dictionary<string, string> { ["State"] = "Running", ["StartMode"] = "Auto" },
            "observed only");
        var owner = new MaintenanceDiagnosticOwner(new FixedPlatform(snapshot));

        var actual = await owner.DiagnoseAsync("repair.superfetch", default);

        Assert.Equal(MaintenanceDiagnosticOutcome.Observed, actual.Outcome);
        Assert.Null(actual.IsHealthy);
        Assert.Equal("Running", actual.Evidence["State"]);
    }

    [Fact]
    public async Task PermissionExceptionAndCancellationHaveDistinctSemantics()
    {
        var denied = new MaintenanceDiagnosticOwner(new ThrowingPlatform(new UnauthorizedAccessException("denied")));
        var deniedSnapshot = await denied.DiagnoseAsync("repair.chkdsk", default);
        Assert.Equal(MaintenanceDiagnosticOutcome.PermissionDenied, deniedSnapshot.Outcome);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new MaintenanceDiagnosticOwner(new ThrowingPlatform(new OperationCanceledException(cts.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelled.DiagnoseAsync("repair.chkdsk", cts.Token));
    }

    [Fact]
    public void CompositionRegistersExactlyOnePlatformAndOwner()
    {
        var services = new ServiceCollection();
        services.AddMaintenanceDiagnosticWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, value => value.ServiceType == typeof(IMaintenanceDiagnosticPlatform));
        Assert.Single(services, value => value.ServiceType == typeof(IMaintenanceDiagnosticOwner));
        Assert.IsType<WindowsMaintenanceDiagnosticPlatform>(provider.GetRequiredService<IMaintenanceDiagnosticPlatform>());
        Assert.Same(provider.GetRequiredService<IMaintenanceDiagnosticOwner>(), provider.GetRequiredService<IMaintenanceDiagnosticOwner>());
    }

    [Fact]
    public void ProductionEntriesContainNoLegacyWritersForTheSixCandidates()
    {
        var root = FindRoot();
        var assertions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [@"GameOptimizer.Core\Services\Repair\RepairOperations\StorageRepairs.cs"] =
            [
                "RunCommandAsync(\"chkdsk\"", "RunCommandAsync(\"sc\", \"query WSearch\"",
                "RunCommandAsync(\"sc\", \"query storsvc\"", "RunCommandAsync(\"net\", \"stop storsvc\""
            ],
            [@"GameOptimizer.Core\Services\Repair\RepairOperations\MemoryRepairs.cs"] =
            [
                "RunCommandAsync(\"sc\", \"query SysMain\"", "RunCommandAsync(\"sc\", \"config SysMain",
                "RunCommandAsync(\"net\", \"stop SysMain\""
            ],
            [@"GameOptimizer.Core\Services\Repair\RepairOperations\PrivacyRepairs.cs"] =
            [
                "RunCommandAsync(\"sc\", \"query lfsvc\"", "RunCommandAsync(\"net\", \"stop lfsvc\"",
                "ConsentStore\\location' -Name 'Value' -Value 'Allow'"
            ]
        };

        foreach (var (relativePath, forbiddenTokens) in assertions)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            foreach (var token in forbiddenTokens)
            {
                Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private sealed class FakePlatform : IMaintenanceDiagnosticPlatform
    {
        public int Reads { get; private set; }
        public int Writes => 0;

        public Task<MaintenanceDiagnosticSnapshot> InspectAsync(
            MaintenanceDiagnosticDefinition definition,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult(new MaintenanceDiagnosticSnapshot(
                definition.RepairItemId,
                MaintenanceDiagnosticOutcome.Observed,
                null,
                $"fake:{definition.CapabilityId}",
                new Dictionary<string, string> { ["Source"] = "fake" },
                "read-only fake"));
        }
    }

    private sealed class FixedPlatform(MaintenanceDiagnosticSnapshot snapshot) : IMaintenanceDiagnosticPlatform
    {
        public Task<MaintenanceDiagnosticSnapshot> InspectAsync(
            MaintenanceDiagnosticDefinition definition,
            CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class ThrowingPlatform(Exception exception) : IMaintenanceDiagnosticPlatform
    {
        public Task<MaintenanceDiagnosticSnapshot> InspectAsync(
            MaintenanceDiagnosticDefinition definition,
            CancellationToken cancellationToken) => Task.FromException<MaintenanceDiagnosticSnapshot>(exception);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "GameOptimizer.Core")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
