using GameOptimizer.Core.Services.Repair;

namespace GameOptimizer.Tests.Changes;

public sealed class RepairLegacyClosureTests
{
    [Fact]
    public void All_static_items_have_a_managed_or_specialized_owner_route()
    {
        var registry = new RepairItemRegistry();
        var legacyItems = GetLegacyItems(registry);

        Assert.Empty(legacyItems);
    }

    [Fact]
    public async Task Four_residual_delegates_remain_fixed_fail_closed_when_called_directly()
    {
        var registry = new RepairItemRegistry();
        registry.Initialize();

        var ids = new[]
        {
            "repair.activity_history", "repair.iso_mount", "repair.standby_list", "repair.working_set"
        };
        foreach (var id in ids)
        {
            var item = Assert.Single(registry.GetAll(), candidate => candidate.Id == id);
            var result = await item.RepairAsync(null);

            Assert.False(result.Success);
        }
    }

    [Fact]
    public void Four_removed_direct_writers_cannot_reappear_in_repair_sources()
    {
        var root = FindRepositoryRoot();
        AssertSourceExcludes(
            root,
            @"GameOptimizer.Core\Services\Repair\RepairOperations\PrivacyRepairs.cs",
            "ClearActivityHistoryAsync",
            "PublishUserActivities",
            "UploadUserActivities");
        AssertSourceExcludes(
            root,
            @"GameOptimizer.Core\Services\Repair\RepairOperations\StorageRepairs.cs",
            "RepairIsoMountAsync",
            "assoc .iso",
            "ftype Windows.IsoFile");
        AssertSourceExcludes(
            root,
            @"GameOptimizer.Core\Services\Repair\RepairOperations\MemoryRepairs.cs",
            "ClearStandbyListAsync",
            "ClearWorkingSetAsync",
            "Clear-RecycleBin",
            ".MinWorkingSet");
    }

    [Fact]
    public void Static_delegate_execution_is_guarded_by_specialized_owner_catalog()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            @"GameOptimizer.Core\Services\Repair\RepairService.cs"));

        Assert.Equal(1, CountOccurrences(source, "item.RepairAsync(progress)"));
        var ownerGuard = source.IndexOf("RepairSpecializedOwnerCatalog.Contains(item.Id)", StringComparison.Ordinal);
        var delegateCall = source.IndexOf("item.RepairAsync(progress)", StringComparison.Ordinal);
        Assert.True(ownerGuard >= 0);
        Assert.True(delegateCall > ownerGuard);
        Assert.Contains("静态 legacy 执行入口已关闭", source, StringComparison.Ordinal);
    }

    private static RepairItem[] GetLegacyItems(RepairItemRegistry registry)
    {
        registry.Initialize();
        return registry.GetAll()
            .Where(item => !UnavailableRepairChangeExecutor.Instance.IsManaged(item.Id))
            .Where(item => !RepairSpecializedOwnerCatalog.Contains(item.Id))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AssertSourceExcludes(
        string root,
        string relativePath,
        params string[] forbidden)
    {
        var source = File.ReadAllText(Path.Combine(root, relativePath));
        Assert.All(forbidden, value =>
            Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class AllowGate : IRepairExecutionGate
    {
        public Task<RepairExecutionGateDecision> EnsureCanExecuteAsync(
            string repairItemId,
            RepairExecutionIntent intent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(RepairExecutionGateDecision.Allowed());
        }
    }
}
