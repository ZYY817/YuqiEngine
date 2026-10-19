namespace GameOptimizer.Tests.Changes;

public sealed class NetworkResetLegacyEntryArchitectureTests
{
    [Fact]
    public void Authorized_legacy_entry_files_contain_no_direct_network_mutation_commands()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            @"GameOptimizer.Core\Services\AutoRepairService.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\NetworkRepairs.cs",
            @"GameOptimizer.Core\Services\WindowsFeatureHealthHelper.cs",
            @"GameOptimizer.Core\Diagnostics\SolutionProvider.cs",
            @"GameOptimizer.Core\Localization\LocalizationService.EnUS.cs",
            @"GameOptimizer.Core\Localization\LocalizationService.ZhCN.cs",
            @"GameOptimizer.Shell\Views\NetworkTestPage.xaml.cs",
            @"GameOptimizer.Shell\Views\GameModeHubPage.xaml.cs"
        };
        var forbidden = new[]
        {
            "netsh winsock reset",
            "netsh int ip reset",
            "advfirewall reset",
            "Arguments = \"/flushdns\"",
            "FileName = \"shutdown\""
        };

        foreach (var relative in files)
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            foreach (var token in forbidden)
                Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
        }

        var repairEntry = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Core\Services\Repair\RepairOperations\NetworkRepairs.cs"));
        Assert.DoesNotContain("\"netsh\"", repairEntry, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"ipconfig", repairEntry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Baseline_advanced_network_collection_contains_read_only_commands_only()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, @"GameOptimizer.Shell\Services\Baseline\BaselineDomainCollector.cs");
        var text = File.ReadAllText(path);
        var start = text.IndexOf("private async Task CollectAdvancedNetworkDomainAsync", StringComparison.Ordinal);
        var end = text.IndexOf("CollectFeaturesDomainAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = text[start..end];

        Assert.Contains("winhttp show proxy", method, StringComparison.Ordinal);
        Assert.Contains("winsock show catalog", method, StringComparison.Ordinal);
        Assert.Contains("interface ip show config", method, StringComparison.Ordinal);
        Assert.DoesNotContain("winsock reset", method, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StepType = \"RunCommand\"", method, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_network_reset_mutations_exist_only_in_the_typed_platform_adapter()
    {
        var root = FindRepositoryRoot();
        var productionRoots = new[] { "GameOptimizer.Core", "GameOptimizer.Infrastructure.Windows", "GameOptimizer.Shell" };
        var platform = Path.GetFullPath(Path.Combine(root,
            @"GameOptimizer.Infrastructure.Windows\NetworkReset\WindowsNetworkResetPlatform.cs"));
        var forbidden = new[]
        {
            "netsh winsock reset",
            "netsh int ip reset",
            "netsh advfirewall reset",
            "Arguments = \"/flushdns\"",
            "Arguments = \"/registerdns\""
        };

        foreach (var productionRoot in productionRoots)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, productionRoot), "*.cs", SearchOption.AllDirectories))
            {
                var fullPath = Path.GetFullPath(file);
                if (fullPath == platform
                    || fullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || fullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || fullPath.Contains("_decompile", StringComparison.OrdinalIgnoreCase)
                    || fullPath.Contains(".bak_", StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = File.ReadAllText(fullPath);
                foreach (var token in forbidden)
                    Assert.DoesNotContain(token, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Composition_registers_one_owner_and_explicitly_injects_all_canonical_entries()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Shell\Extensions\NetworkResetServiceCollectionExtensions.cs"));
        var app = File.ReadAllText(Path.Combine(root, @"GameOptimizer.Shell\App.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, @"GameOptimizer.Shell\MainWindow.xaml.cs"));
        var infrastructure = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Infrastructure.Windows\Extensions\ServiceCollectionExtensions.cs"));

        Assert.Equal(1, Count(composition, "AddSingleton<INetworkResetOwner>"));
        Assert.Equal(1, Count(composition, "AddSingleton<INetworkResetBaselineGate"));
        Assert.Contains("GetRequiredService<INetworkResetOwner>()", composition, StringComparison.Ordinal);
        Assert.Contains("new AutoRepairService", composition, StringComparison.Ordinal);
        Assert.Contains("new RepairItemRegistry", composition, StringComparison.Ordinal);
        Assert.Equal(1, Count(app, "AddNetworkResetComposition"));
        Assert.DoesNotContain("AddSingleton<RepairItemRegistry>()", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<NetworkTestPage>", app, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachNetworkResetOwner(networkResetOwner)", mainWindow, StringComparison.Ordinal);
        Assert.Equal(1, Count(infrastructure, "AddSingleton<INetworkResetPlatform"));
        Assert.Equal(1, Count(infrastructure, "AddSingleton<INetworkResetReceiptStore"));
        Assert.Equal(1, Count(infrastructure, "AddSingleton<FileNetworkResetPlanStore>"));
        Assert.Equal(1, Count(infrastructure, "AddSingleton<INetworkResetPlanStore>"));
        Assert.DoesNotContain("AddSingleton<IChangePlanStore, FileNetworkResetPlanStore>", infrastructure, StringComparison.Ordinal);
    }

    [Fact]
    public void Owner_contract_exposes_no_adapter_dhcp_firewall_or_automatic_recovery_writer()
    {
        var root = FindRepositoryRoot();
        var models = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Core\Services\NetworkReset\NetworkResetModels.cs"));
        var owner = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Core\Services\NetworkReset\NetworkResetOwner.cs"));
        var policy = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Core\Services\NetworkReset\NetworkResetPolicy.cs"));
        var platform = File.ReadAllText(Path.Combine(root,
            @"GameOptimizer.Infrastructure.Windows\NetworkReset\WindowsNetworkResetPlatform.cs"));

        Assert.DoesNotContain("NetworkAdapter", models, StringComparison.Ordinal);
        Assert.DoesNotContain("Dhcp", models, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Firewall", models, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RecoveryId", models, StringComparison.Ordinal);
        Assert.DoesNotContain("advfirewall", platform, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RegisterDns", models, StringComparison.Ordinal);
        Assert.DoesNotContain("/registerdns", platform, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("明确不包含 firewall reset", owner + policy, StringComparison.Ordinal);
        Assert.Contains("ManualInterventionRequired", models, StringComparison.Ordinal);
        Assert.Contains("RebootRequired", models, StringComparison.Ordinal);
    }

    [Fact]
    public void Network_page_exposes_dns_execution_cancel_and_receipt_feedback_without_success_overclaim()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, @"GameOptimizer.Shell\Views\NetworkTestPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, @"GameOptimizer.Shell\Views\NetworkTestPage.xaml.cs"));
        var owner = File.ReadAllText(Path.Combine(root, @"GameOptimizer.Core\Services\NetworkReset\NetworkResetOwner.cs"));

        Assert.Contains("DnsActionStatusPanel", xaml, StringComparison.Ordinal);
        Assert.Contains("CancelDnsBtn", xaml, StringComparison.Ordinal);
        Assert.Contains("CancelDns_Click", code, StringComparison.Ordinal);
        Assert.Contains("不会自动重试", code, StringComparison.Ordinal);
        Assert.Contains("不能据此证明缓存内容已清空", owner, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
