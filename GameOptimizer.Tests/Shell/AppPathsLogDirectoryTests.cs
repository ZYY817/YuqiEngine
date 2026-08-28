using GameOptimizer.Core.Constants;

namespace GameOptimizer.Tests.Shell;

[CollectionDefinition("AppPathsLogDirectory", DisableParallelization = true)]
public sealed class AppPathsLogDirectoryCollection;

[Collection("AppPathsLogDirectory")]
public sealed class AppPathsLogDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "yuqi-log-root-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ConfigureLocalDirectory_ChangesGlobalLogRootForThisProcess()
    {
        var configured = Path.Combine(_root, "custom-logs");

        var success = AppPaths.TryConfigureLogsDirectory(configured, out var error);

        Assert.True(success, error);
        Assert.Equal(Path.GetFullPath(configured), AppPaths.LogsDir);
        Assert.True(Directory.Exists(configured));
    }

    [Fact]
    public void ConfigureDriveRoot_IsRejectedAndFallsBackToDefault()
    {
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(_root))!;

        var success = AppPaths.TryConfigureLogsDirectory(driveRoot, out var error);

        Assert.False(success);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(AppPaths.DefaultLogsDir, AppPaths.LogsDir);
    }

    public void Dispose()
    {
        AppPaths.TryConfigureLogsDirectory(null, out _);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
