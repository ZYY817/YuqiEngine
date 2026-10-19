using GameOptimizer.Core.Diagnostics;
using System.IO.Compression;

namespace GameOptimizer.Tests.Diagnostics;

public sealed class DiagnosticPackageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "yuqi-diagnostic-package-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateDiagnosticPackageAsync_RejectsUncAndUsesLocalFallback()
    {
        var fallback = Path.Combine(_root, "fallback");
        var service = CreateService([@"\\server\share", fallback]);

        var result = await service.CreateDiagnosticPackageAsync();

        Assert.True(result.Success, result.Error);
        Assert.Equal(fallback, Path.GetDirectoryName(result.PackagePath));
        Assert.True(File.Exists(result.PackagePath));
    }

    [Fact]
    public async Task CreateDiagnosticPackageAsync_WhenPreferredZipWriteFails_RetriesNextDirectory()
    {
        var preferred = Path.Combine(_root, "preferred");
        var fallback = Path.Combine(_root, "fallback");
        var attempts = new List<string>();
        var service = CreateService(
            [preferred, fallback],
            (_, destination) =>
            {
                attempts.Add(destination);
                if (Path.GetDirectoryName(destination) == preferred)
                {
                    throw new IOException("simulated preferred destination failure");
                }

                File.WriteAllText(destination, "isolated test archive");
            });

        var result = await service.CreateDiagnosticPackageAsync();

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(fallback, Path.GetDirectoryName(result.PackagePath));
        Assert.True(File.Exists(result.PackagePath));
    }

    [Fact]
    public async Task CreateDiagnosticPackageAsync_WhenEveryDestinationFails_ReturnsExplicitError()
    {
        var first = Path.Combine(_root, "first");
        var second = Path.Combine(_root, "second");
        var service = CreateService(
            [first, second],
            (_, _) => throw new IOException("simulated output failure"));

        var result = await service.CreateDiagnosticPackageAsync();

        Assert.False(result.Success);
        Assert.Empty(result.PackagePath);
        Assert.Contains("任何安全的本地目录", result.Error, StringComparison.Ordinal);
        Assert.Contains("simulated output failure", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPreview_OnlyIncludesWhitelistedApplicationLogs()
    {
        var logs = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "optimizer-session.log"), "runtime");
        File.WriteAllText(Path.Combine(logs, "diagnostic.json"), "diagnostic");
        File.WriteAllText(Path.Combine(logs, "settings.json"), "must not be collected");
        File.WriteAllText(Path.Combine(logs, "personal-notes.txt"), "must not be collected");

        var service = CreateService([Path.Combine(_root, "output")], logDirectories: [logs]);

        var preview = service.GetPreview();

        Assert.Equal(2, preview.LogFileCount);
        Assert.Contains("optimizer-session.log", preview.LogFileNames);
        Assert.Contains("diagnostic.json", preview.LogFileNames);
        Assert.DoesNotContain("settings.json", preview.LogFileNames);
        Assert.DoesNotContain("personal-notes.txt", preview.LogFileNames);
        Assert.Contains("不包含配置", preview.PrivacyNotice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateDiagnosticPackageAsync_FlushesSanitizesAndExcludesNonLogFiles()
    {
        var logs = Path.Combine(_root, "logs");
        var output = Path.Combine(_root, "output");
        Directory.CreateDirectory(logs);
        var userPath = $@"C:\Users\{Environment.UserName}\Documents\game.json";
        File.WriteAllText(
            Path.Combine(logs, "optimizer-session.log"),
            $"path={userPath} contact=test@example.com password=SuperSecret\n{{\"api_key\":\"JsonSecret\"}}\nAuthorization: Bearer HeaderSecret");
        File.WriteAllText(Path.Combine(logs, "settings.json"), "api_key=must-not-leak");
        var flushed = false;
        var service = CreateService(
            [output],
            logDirectories: [logs],
            flushLogs: _ =>
            {
                flushed = true;
                return Task.CompletedTask;
            });

        var result = await service.CreateDiagnosticPackageAsync();

        Assert.True(result.Success, result.Error);
        Assert.True(flushed);
        using var archive = ZipFile.OpenRead(result.PackagePath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "logs/optimizer-session.log");
        Assert.Contains(archive.Entries, entry => entry.FullName == "system_info.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "README.txt");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("settings.json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("configs/", StringComparison.OrdinalIgnoreCase));

        var logEntry = Assert.Single(archive.Entries, entry => entry.FullName == "logs/optimizer-session.log");
        using var reader = new StreamReader(logEntry.Open());
        var content = await reader.ReadToEndAsync();
        Assert.Contains(@"C:\Users\[USER]\Documents", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("password=[REDACTED]", content, StringComparison.Ordinal);
        Assert.Contains("[EMAIL_REDACTED]", content, StringComparison.Ordinal);
        Assert.DoesNotContain("SuperSecret", content, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSecret", content, StringComparison.Ordinal);
        Assert.DoesNotContain("HeaderSecret", content, StringComparison.Ordinal);
        Assert.DoesNotContain("test@example.com", content, StringComparison.Ordinal);
    }

    private DiagnosticPackageService CreateService(
        IReadOnlyList<string> candidates,
        Action<string, string>? archiveWriter = null,
        IReadOnlyList<string>? logDirectories = null,
        Func<CancellationToken, Task>? flushLogs = null)
    {
        var collectionDirectory = Path.Combine(_root, "collection", Guid.NewGuid().ToString("N"));
        return new DiagnosticPackageService(
            () => candidates,
            archiveWriter,
            () => collectionDirectory,
            logDirectories is null ? null : () => logDirectories,
            flushLogs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
