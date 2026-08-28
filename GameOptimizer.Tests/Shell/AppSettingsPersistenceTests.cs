using GameOptimizer.Shell.Models;

namespace GameOptimizer.Tests.Shell;

public sealed class AppSettingsPersistenceTests
{
    [Fact]
    public void LegacyVisualModeIsDroppedDuringConfigurationMigration()
    {
        var root = Path.Combine(Path.GetTempPath(), "yuqi-settings-visual-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, "{\"ConfigVersion\":2,\"Language\":\"en-US\",\"VisualMode\":\"LowUsage\"}");

            var migrated = AppSettings.LoadFromPath(path);

            Assert.Equal(3, migrated.ConfigVersion);
            Assert.Equal("en-US", migrated.Language);
            Assert.DoesNotContain("VisualMode", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorruptPrimaryRecoversFromDurableBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "yuqi-settings-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            Assert.True(new AppSettings { Language = "zh-CN", MinimizeToTray = false }.SaveToPath(path));
            Assert.True(new AppSettings { Language = "en-US", MinimizeToTray = true }.SaveToPath(path));
            Assert.True(File.Exists(path + ".recovery.bak"));

            File.WriteAllText(path, "{corrupt-json");
            var recovered = AppSettings.LoadFromPath(path);

            Assert.True(AppSettings.LastLoadWasRecovered);
            Assert.Equal("zh-CN", recovered.Language);
            Assert.False(recovered.MinimizeToTray);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyThemeSettingIsDroppedDuringConfigurationMigration()
    {
        var root = Path.Combine(Path.GetTempPath(), "yuqi-settings-theme-migration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        try
        {
            File.WriteAllText(path, "{\"ConfigVersion\":1,\"ThemeMode\":\"light\",\"VisualMode\":\"LowUsage\"}");

            var migrated = AppSettings.LoadFromPath(path);

            Assert.Equal(3, migrated.ConfigVersion);
            Assert.DoesNotContain("ThemeMode", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.DoesNotContain("VisualMode", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CustomLogDirectoryAndRetentionPersistTogether()
    {
        var root = Path.Combine(Path.GetTempPath(), "yuqi-settings-log-path", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        var customLogDirectory = Path.Combine(root, "logs-on-selected-drive");
        try
        {
            var settings = new AppSettings
            {
                LogRetentionDays = 45,
                LogBackupDirectory = customLogDirectory
            };

            Assert.True(settings.SaveToPath(path));
            var loaded = AppSettings.LoadFromPath(path);

            Assert.Equal(45, loaded.LogRetentionDays);
            Assert.Equal(customLogDirectory, loaded.LogBackupDirectory);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
