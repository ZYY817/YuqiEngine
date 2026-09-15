using System.Text.RegularExpressions;

namespace GameOptimizer.Tests.Architecture;

public sealed class EvidenceIsolationArchitectureTests
{
    private static readonly string[] ProjectFiles =
    {
        "GameOptimizer.Core/GameOptimizer.Core.csproj",
        "GameOptimizer.Infrastructure.Windows/GameOptimizer.Infrastructure.Windows.csproj",
        "GameOptimizer.ModuleContracts/GameOptimizer.ModuleContracts.csproj",
        "GameOptimizer.Modules.PerformanceHealth/GameOptimizer.Modules.PerformanceHealth.csproj",
        "GameOptimizer.Shell/GameOptimizer.Shell.csproj",
        "GameOptimizer.Tests/GameOptimizer.Tests.csproj"
    };

    [Fact]
    public void Evidence_files_are_globally_removed_from_build_and_publish_item_types()
    {
        var root = FindRepositoryRoot();
        var targetsPath = Path.Combine(root, "Directory.Build.targets");
        var targets = File.ReadAllText(targetsPath);

        foreach (var itemType in new[] { "Compile", "Content", "None", "Page", "ApplicationDefinition", "EmbeddedResource", "Resource" })
        {
            Assert.Contains($"<{itemType} Remove=\"$(EvidenceIsolationGlobs)\"", targets, StringComparison.Ordinal);
        }

        foreach (var project in ProjectFiles)
        {
            var projectText = File.ReadAllText(Path.Combine(root, project));
            var includes = Regex.Matches(projectText, "<(?:Compile|Content|Page|ApplicationDefinition|EmbeddedResource|Resource)\\s+Include=\\\"([^\\\"]+)\\\"", RegexOptions.CultureInvariant)
                .Select(match => match.Groups[1].Value);

            Assert.DoesNotContain(includes, IsEvidencePath);
        }
    }

    [Fact]
    public void Both_solutions_reference_only_the_six_formal_projects()
    {
        var root = FindRepositoryRoot();

        foreach (var solutionName in new[] { "GameOptimizer.sln", "GameOptimizer.Main.sln" })
        {
            var projectPaths = File.ReadLines(Path.Combine(root, solutionName))
                .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
                .Select(line => Regex.Match(line, "= \\\"[^\\\"]+\\\", \\\"([^\\\"]+)\\\""))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value.Replace('\\', '/'))
                .ToArray();

            Assert.Equal(ProjectFiles.Length, projectPaths.Length);
            Assert.All(projectPaths, path => Assert.Contains(path, ProjectFiles, StringComparer.OrdinalIgnoreCase));
            Assert.DoesNotContain(projectPaths, IsEvidencePath);
        }
    }

    [Fact]
    public void Read_only_evidence_verifier_is_present()
    {
        var root = FindRepositoryRoot();
        var verifierPath = Path.Combine(root, "eng", "Verify-EvidenceIsolation.ps1");
        var verifier = File.ReadAllText(verifierPath);

        Assert.Contains("Get-FileHash", verifier, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", verifier, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", verifier, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Set-Content", verifier, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shell_sources_do_not_reintroduce_decompiler_residue_or_retired_batch_path()
    {
        var root = FindRepositoryRoot();
        var shellRoot = Path.Combine(root, "GameOptimizer.Shell");
        var sourceFiles = Directory.EnumerateFiles(shellRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        Assert.All(sourceFiles, path =>
            Assert.DoesNotContain("if (1 == 0)", File.ReadAllText(path), StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(shellRoot, "Services", "BatchOperationService.cs")));
        Assert.False(File.Exists(Path.Combine(shellRoot, "Interfaces", "IOptimizationExecutor.cs")));
    }

    [Fact]
    public void Cpu_profile_editor_reuses_the_loaded_topology_instead_of_starting_a_second_probe()
    {
        var root = FindRepositoryRoot();
        var profileSource = File.ReadAllText(Path.Combine(
            root,
            "GameOptimizer.Shell",
            "ViewModels",
            "CpuCoreManagementViewModel.Profiles.cs"));

        Assert.Contains("CreateEditorCpuInfo", profileSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new CpuAffinityService()", profileSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_view_model_requires_its_safety_dependencies_from_di()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "GameOptimizer.Shell",
            "ViewModels",
            "SystemCleanupViewModel.cs"));

        Assert.Contains("public SystemCleanupViewModel(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public SystemCleanupViewModel()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Power_plan_editor_requires_its_typed_owner_chain_from_di()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "GameOptimizer.Shell",
            "Views",
            "PowerPlanEditorPage.xaml.cs"));

        Assert.Contains("PowerPlanEditorPage(", source, StringComparison.Ordinal);
        Assert.Contains("IPowerManagementOwner powerOwner", source, StringComparison.Ordinal);
        Assert.Contains("IManagedPowerPlanService managedPower", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFailClosedCompatibility", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_page_uses_an_explicit_lazy_startup_owner_factory()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "GameOptimizer.Shell",
            "Views",
            "SettingsPage.xaml.cs"));

        Assert.Contains("Func<IStartupEntryOwner> startupOwnerFactory", source, StringComparison.Ordinal);
        Assert.Contains("IConfigurationExportService configurationExportService", source, StringComparison.Ordinal);
        Assert.Contains("_startupOwner ??= _startupOwnerFactory()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App)?.Services", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider _services", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Memory_pages_receive_the_single_composed_service_chain()
    {
        var root = FindRepositoryRoot();
        var extension = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Extensions", "MemoryModuleExtension.cs"));
        var panel = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MemoryControlPanel.xaml.cs"));
        var dialog = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "MemoryCleanDialog.xaml.cs"));

        Assert.Contains("public MemoryModuleExtension(", extension, StringComparison.Ordinal);
        Assert.Contains("new(\n        _memoryCleanerService,", extension, StringComparison.Ordinal);
        Assert.Contains("public MemoryControlPanel(", panel, StringComparison.Ordinal);
        Assert.Contains("new MemoryCleanDialog(", panel, StringComparison.Ordinal);
        Assert.Contains("CleanMode.WorkingSet", panel, StringComparison.Ordinal);
        Assert.Contains("public MemoryCleanDialog(", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("new MemoryCleanerService()", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProcessCleanerService()", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Remaining_legacy_pages_do_not_resolve_services_from_the_application_singleton()
    {
        var root = FindRepositoryRoot();
        var pageSources = new[]
        {
            "GameOptimizer.Shell/Views/GameModeHubPage.xaml.cs",
            "GameOptimizer.Shell/Views/HardwareOptimizationPage.xaml.cs",
            "GameOptimizer.Shell/Views/StartupManagerPage.xaml.cs"
        };

        Assert.All(pageSources, relativePath =>
            Assert.DoesNotContain(
                "Application.Current as App",
                File.ReadAllText(Path.Combine(root, relativePath)),
                StringComparison.Ordinal));
    }

    [Fact]
    public void Dashboard_uses_typed_dependencies_and_the_composed_widget_factory()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "GameOptimizer.Shell",
            "ViewModels",
            "DashboardViewModel.cs"));

        Assert.Contains("Func<IModuleWidget, object> widgetContentFactory", source, StringComparison.Ordinal);
        Assert.Contains("_widgetContentFactory(widget)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IServiceProvider services", source, StringComparison.Ordinal);
        Assert.DoesNotContain("services.GetService", source, StringComparison.Ordinal);
    }

    private static bool IsEvidencePath(string path)
        => Regex.IsMatch(
            path,
            "(?:_decompile|\\.bak$|_Backup\\.|_New\\.|\\.verify_build|docs[\\\\/]RECOVERY|_evidence|_staging|_recovery-history|_temporary-validation)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到包含 GameOptimizer.sln 的工作区根目录。");
    }
}
