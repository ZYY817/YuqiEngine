using GameOptimizer.Core.Diagnostics;
using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Tests.Shell;

public sealed class LogSettingsBehaviorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "yuqi-log-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DiagnosticLogServiceUsesConfiguredWritableDirectory()
    {
        var configured = Path.Combine(_root, "custom-logs");

        using var service = new DiagnosticLogService(configured);

        Assert.Equal(Path.GetFullPath(configured), service.LogDirectory);
        Assert.Single(Directory.GetFiles(configured, "optimizer-*.log"));
    }

    [Fact]
    public void PermanentRetentionDoesNotDeleteOldLogs()
    {
        var directory = CreateLogDirectory();
        var oldLog = CreateLog(directory, "old.log", new DateTime(2024, 1, 1));
        var store = new AppSettingsStore(new AppSettings { LogRetentionDays = 0 }, _ => true);
        var service = new LogRetentionService(
            new StubDiagnosticLogService(directory),
            store,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero)));

        service.ApplyRetentionPolicy();

        Assert.True(File.Exists(oldLog));
    }

    [Fact]
    public void CustomRetentionDeletesOnlyExpiredLogs()
    {
        var directory = CreateLogDirectory();
        var expired = CreateLog(directory, "expired.log", new DateTime(2026, 8, 10));
        var expiredRotation = CreateLog(directory, "expired.log.old", new DateTime(2026, 8, 9));
        var retained = CreateLog(directory, "retained.log", new DateTime(2026, 8, 24));
        var unrelated = Path.Combine(directory, "notes.txt");
        File.WriteAllText(unrelated, "keep");
        var store = new AppSettingsStore(new AppSettings { LogRetentionDays = 10 }, _ => true);
        var service = new LogRetentionService(
            new StubDiagnosticLogService(directory),
            store,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero)));

        service.ApplyRetentionPolicy();

        Assert.False(File.Exists(expired));
        Assert.False(File.Exists(expiredRotation));
        Assert.True(File.Exists(retained));
        Assert.True(File.Exists(unrelated));
    }

    private string CreateLogDirectory()
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateLog(string directory, string name, DateTime lastWriteTime)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTime(path, lastWriteTime);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubDiagnosticLogService(string logDirectory) : IDiagnosticLogService
    {
        public string LogDirectory { get; } = logDirectory;
        public event EventHandler<DiagnosticLogEntry>? EntryPublished
        {
            add { }
            remove { }
        }
        public void Append(LogLevel level, string source, string message, string? detail = null) { }
        public IReadOnlyList<DiagnosticLogEntry> GetRecentEntries(int count = 32) => [];
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
