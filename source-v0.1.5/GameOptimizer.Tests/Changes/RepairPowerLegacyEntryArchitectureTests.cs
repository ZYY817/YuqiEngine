namespace GameOptimizer.Tests.Changes;

public sealed class RepairPowerLegacyEntryArchitectureTests
{
    [Fact]
    public void RepairPowerSources_DoNotContainLegacyMutationCommands()
    {
        var root = FindRepositoryRoot();
        var sources = new[]
        {
            @"GameOptimizer.Core\Services\Repair\RepairOperations\PowerRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\CpuRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\SystemComponentRepairs.cs",
            @"GameOptimizer.Core\Services\AutoRepairService.cs"
        };
        var forbidden = new[]
        {
            "-restoredefaultschemes",
            "/restoredefaultschemes",
            "/hibernate on",
            "-setacvalueindex scheme_current sub_sleep",
            "-setacvalueindex scheme_current sub_processor CPMINCORES",
            "-setacvalueindex scheme_current sub_processor PROCTHROTTLE",
            "config Power start= auto",
            "TryStartService(\"Power\"",
            "/batteryreport /output",
            "/id PowerDiagnostic",
            "Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power'"
        };

        foreach (var relativePath in sources)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.All(forbidden, value => Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase));
        }
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
