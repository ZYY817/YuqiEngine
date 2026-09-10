namespace GameOptimizer.Tests.Shell;

public sealed class ProcessPriorityToggleReentrancyContractTests
{
    [Fact]
    public void EnableToggleHandlerRevertsWithoutReenteringCheckedUnchecked()
    {
        var code = File.ReadAllText(RepoPath("GameOptimizer.Shell/Views/ProcessPriorityPage.xaml.cs"));

        Assert.Contains("private bool _synchronizingEnableToggle;", code, StringComparison.Ordinal);
        Assert.Contains("if (_synchronizingEnableToggle)", code, StringComparison.Ordinal);
        Assert.Contains("RevertEnableToggle(checkBox, !isEnabled);", code, StringComparison.Ordinal);
        Assert.Contains("_synchronizingEnableToggle = true;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("checkBox.IsChecked = !isEnabled;", code, StringComparison.Ordinal);
    }

    private static string RepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GameOptimizer.Main.sln")))
                return Path.Combine(directory.FullName, relativePath);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
