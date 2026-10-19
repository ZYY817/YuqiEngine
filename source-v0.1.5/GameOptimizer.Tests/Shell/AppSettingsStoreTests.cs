using GameOptimizer.Shell.Models;
using GameOptimizer.Shell.Services;

namespace GameOptimizer.Tests.Shell;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void SuccessfulUpdatePublishesOnlyPersistedCandidate()
    {
        AppSettings? persisted = null;
        var store = new AppSettingsStore(
            new AppSettings { MinimizeToTray = false },
            settings => { persisted = settings; return true; });
        AppSettingsChangedEventArgs? change = null;
        store.Changed += (_, args) => change = args;

        var success = store.TryUpdate(settings => settings.MinimizeToTray = true);

        Assert.True(success);
        Assert.True(store.Current.MinimizeToTray);
        Assert.True(persisted!.MinimizeToTray);
        Assert.False(change!.Previous.MinimizeToTray);
        Assert.True(change.Current.MinimizeToTray);
    }

    [Fact]
    public void FailedSaveKeepsCurrentValueAndDoesNotPublish()
    {
        var store = new AppSettingsStore(
            new AppSettings { EnableSoundNotification = true },
            _ => false);
        var published = false;
        store.Changed += (_, _) => published = true;

        var success = store.TryUpdate(settings => settings.EnableSoundNotification = false);

        Assert.False(success);
        Assert.True(store.Current.EnableSoundNotification);
        Assert.False(published);
    }

    [Fact]
    public void UpdateDoesNotMutatePreviouslyPublishedSnapshot()
    {
        var store = new AppSettingsStore(new AppSettings(), _ => true);
        var before = store.Current;

        Assert.True(store.TryUpdate(settings => settings.MinimizeToTray = true));

        Assert.False(before.MinimizeToTray);
        Assert.True(store.Current.MinimizeToTray);
        Assert.NotSame(before, store.Current);
    }
}
