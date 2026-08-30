using System.Text;
using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Tests.Baseline;

public sealed class FirstProtectionPreferenceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "YuqiEngineTests", Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public FirstProtectionPreferenceStoreTests()
    {
        _path = Path.Combine(_root, "Config", "first_protection_preference.json");
    }

    [Fact]
    public async Task FirstLaunchStartsPendingAndOffersTheIntroduction()
    {
        var state = await CreateStore().LoadAsync();

        Assert.Equal(FirstProtectionPreferenceStatus.Pending, state.Status);
        Assert.True(state.ShouldOfferOnStartup);
    }

    [Fact]
    public async Task SkipPersistsWithoutChangingTheSafetyGateState()
    {
        var store = CreateStore();
        await store.DeferAsync("User chose to continue without a backup.");

        var state = await store.LoadAsync();
        Assert.Equal(FirstProtectionPreferenceStatus.UserDeferred, state.Status);
        Assert.False(state.ShouldOfferOnStartup);
        Assert.Equal("User chose to continue without a backup.", state.LastFailureReason);
    }

    [Fact]
    public async Task PreviousRevisionSkipIsShownOnceForTheNewSafetyOnboarding()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, "{\"schemaVersion\":1,\"status\":1}", Encoding.UTF8);

        var state = await CreateStore().LoadAsync();

        Assert.Equal(FirstProtectionPreferenceStatus.UserDeferred, state.Status);
        Assert.Equal(0, state.OnboardingRevision);
        Assert.True(state.ShouldOfferOnStartup);
    }

    [Fact]
    public async Task FailedBackupIsOfferedAgainOnTheNextStartup()
    {
        var store = CreateStore();
        await store.RecordFailureAsync("The system protection prerequisite is unavailable.");

        var state = await store.LoadAsync();
        Assert.Equal(FirstProtectionPreferenceStatus.BackupFailed, state.Status);
        Assert.True(state.ShouldOfferOnStartup);
    }

    [Fact]
    public async Task SuccessfulBackupSuppressesTheRepeatedIntroduction()
    {
        var store = CreateStore();
        await store.RecordCompletedAsync();

        var state = await store.LoadAsync();
        Assert.Equal(FirstProtectionPreferenceStatus.BackupCompleted, state.Status);
        Assert.NotNull(state.BackupCompletedUtc);
        Assert.False(state.ShouldOfferOnStartup);
    }

    [Fact]
    public async Task CorruptedPreferenceIsReofferedWithoutClaimingTheBaselineIsSafe()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(_path, "{not-json", Encoding.UTF8);

        var state = await CreateStore().LoadAsync();
        Assert.Equal(FirstProtectionPreferenceStatus.Corrupted, state.Status);
        Assert.True(state.ShouldOfferOnStartup);
        Assert.Contains("未影响现有安全基线", state.LastFailureReason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FirstProtectionPreferenceStore CreateStore() => new(_path);
}
