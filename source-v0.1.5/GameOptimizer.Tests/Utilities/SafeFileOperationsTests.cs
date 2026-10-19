using GameOptimizer.Core.Utilities;

namespace GameOptimizer.Tests.Utilities;

public sealed class SafeFileOperationsTests
{
    [Fact]
    public async Task JsonReplacementLeavesNoTemporaryFileAndPublishesCompleteValue()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"yuqi-safe-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, "{\"value\":1}");

        try
        {
            await SafeFileOperations.WriteJsonWithRetryAsync(path, new Payload(2));

            Assert.Contains("\"Value\": 2", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TextReplacementLeavesNoTemporaryFileAndPublishesCompleteValue()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"yuqi-safe-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "state.txt");
        await File.WriteAllTextAsync(path, "old");

        try
        {
            await SafeFileOperations.WriteTextWithRetryAsync(path, "new");

            Assert.Equal("new", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record Payload(int Value);
}
