using Xunit;

namespace GameOptimizer.Tests.Shell;

public sealed class ShellRegistryWriteBoundaryTests
{
    [Fact]
    public void System_restore_compatibility_facade_is_reachable_through_the_owner_and_never_writes_frequency()
    {
        var root = FindRepositoryRoot();
        var facade = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Services", "SystemRestoreService.cs"));
        var app = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "App.xaml.cs"));

        Assert.Contains("ISystemRestorePointOwner _owner", facade, StringComparison.Ordinal);
        Assert.Contains("_owner.PrepareCreateAsync", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("SystemRestorePointCreationFrequency", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("Registry.SetValue", facade, StringComparison.Ordinal);
        Assert.DoesNotContain("Registry.LocalMachine", facade, StringComparison.Ordinal);
        Assert.Contains("AddSystemRestorePointWindowsInfrastructure", app, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<ISystemRestoreService, SystemRestoreService>", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_info_dialog_does_not_write_regedit_navigation_state()
    {
        var root = FindRepositoryRoot();
        var dialog = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "RegistryInfoDialog.xaml.cs"));
        var reader = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Services", "RegistryInfoReader.cs"));

        Assert.Contains("IRegistryInfoReader", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Win32", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("RegistryKey", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Registry.CurrentUser.CreateSubKey", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValue(\"LastKey\"", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Applets\\Regedit", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateSubKey", reader, StringComparison.Ordinal);
        Assert.DoesNotContain("SetValue", reader, StringComparison.Ordinal);
        Assert.Contains("Process.Start", dialog, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GameOptimizer.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate GameOptimizer.sln.");
    }
}
