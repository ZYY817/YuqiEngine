using GameOptimizer.Core.Services.Startup;
using GameOptimizer.Infrastructure.Windows.Extensions;
using GameOptimizer.Infrastructure.Windows.PowerManagement;
using GameOptimizer.Infrastructure.Windows.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class ManagedAutostartInfrastructureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "yuqi-autostart-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TwoStoreInstances_AllowOnlyOnePlanAndRecoveryClaim()
    {
        var a = Store();
        var b = Store();
        var id = Guid.NewGuid();
        var planClaims = await Task.WhenAll(
            a.TryClaimPlanAsync(id, new string('A', 64), default),
            b.TryClaimPlanAsync(id, new string('A', 64), default));
        var restoreClaims = await Task.WhenAll(
            a.TryClaimRestoreAsync(id, default),
            b.TryClaimRestoreAsync(id, default));

        Assert.Single(planClaims, value => value);
        Assert.Single(restoreClaims, value => value);
    }

    [Fact]
    public async Task TamperedEnvelope_IsRejected()
    {
        var store = Store();
        var plan = Plan();
        await store.SavePlanAsync(plan, Snapshot(), default);
        var path = Directory.GetFiles(_root, "plan-*.json").Single();
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^12] ^= 0x1;
        await File.WriteAllBytesAsync(path, bytes);

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => store.LoadPlanAsync(plan.PlanId, default));
    }

    [Fact]
    public async Task CrossInstanceResourceLeaseSerializesOwners()
    {
        var a = Store();
        var b = Store();
        await using var first = await a.AcquireLeaseAsync("startup:app", default);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await b.AcquireLeaseAsync("startup:app", cts.Token));
    }

    [Fact]
    public async Task ForgedConfirmationIsRejectedWithoutWrite()
    {
        var platform = new FakePlatform();
        var store = Store();
        var owner = new ManagedAutostartOwner(platform, new AllowGate(), store, store);
        var identity = new ManagedAutostartIdentity(ManagedAutostartKind.RegistryRun, "current-user", ManagedAutostartApplicationIdentity.ResourceId);
        var prepared = await owner.PrepareAsync(identity, ManagedAutostartAction.Disable, default);
        var token = owner.Confirm(prepared.Plan!, new(prepared.Plan!.PlanId, prepared.Plan.PlanHash, DateTimeOffset.UtcNow, true), TimeSpan.FromMinutes(1))
            with { Signature = new string('0', 64) };

        var result = await owner.ExecuteAsync(prepared.Plan!, token, default);

        Assert.Equal("ConfirmationRejected", result.Status);
        Assert.Equal(0, platform.Writes);
    }

    [Fact]
    public async Task ConfirmationEvidenceMustBindTheSamePlan()
    {
        var platform = new FakePlatform();
        var store = Store();
        var owner = new ManagedAutostartOwner(platform, new AllowGate(), store, store);
        var identity = new ManagedAutostartIdentity(ManagedAutostartKind.RegistryRun, "current-user", ManagedAutostartApplicationIdentity.ResourceId);
        var prepared = await owner.PrepareAsync(identity, ManagedAutostartAction.Disable, default);

        Assert.Throws<InvalidOperationException>(() => owner.Confirm(prepared.Plan!,
            new(Guid.NewGuid(), prepared.Plan!.PlanHash, DateTimeOffset.UtcNow, true), TimeSpan.FromMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => owner.Confirm(prepared.Plan!,
            new(prepared.Plan!.PlanId, prepared.Plan.PlanHash, DateTimeOffset.UtcNow, false), TimeSpan.FromMinutes(1)));
        Assert.Equal(0, platform.Writes);
    }

    [Fact]
    public async Task ExpiredSignedConfirmationAndReplayNeverWrite()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var platform = new FakePlatform();
        var store = Store();
        var owner = new ManagedAutostartOwner(platform, new AllowGate(), store, store, clock);
        var identity = new ManagedAutostartIdentity(ManagedAutostartKind.RegistryRun, "current-user", ManagedAutostartApplicationIdentity.ResourceId);
        var prepared = await owner.PrepareAsync(identity, ManagedAutostartAction.Disable, default);
        var token = owner.Confirm(prepared.Plan!, new(prepared.Plan!.PlanId, prepared.Plan.PlanHash, clock.GetUtcNow(), true), TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal("ConfirmationRejected", (await owner.ExecuteAsync(prepared.Plan!, token, default)).Status);
        Assert.Equal("ConfirmationRejected", (await owner.ExecuteAsync(prepared.Plan!, token, default)).Status);
        Assert.Equal(0, platform.Writes);
    }

    [Fact]
    public void DurableSecretSurvivesStoreRestart()
    {
        var first = Store().GetConfirmationSecret();
        var second = Store().GetConfirmationSecret();

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void DependencyInjectionMapsOneStoreAndOneOwner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPowerDataProtector>(new FakeProtector());
        services.AddManagedAutostartWindowsInfrastructure(_root);
        services.AddSingleton<IManagedAutostartGate>(new AllowGate());
        services.AddSingleton<ManagedAutostartOwner>();
        services.AddSingleton<IStartupEntryOwner>(provider => provider.GetRequiredService<ManagedAutostartOwner>());
        services.AddSingleton<IScheduledTaskOwner>(provider => provider.GetRequiredService<ManagedAutostartOwner>());

        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<FileManagedAutostartStore>(), provider.GetRequiredService<IManagedAutostartStore>());
        Assert.Same(provider.GetRequiredService<IManagedAutostartStore>(), provider.GetRequiredService<IManagedAutostartConfirmationSecretProvider>());
        Assert.Same(provider.GetRequiredService<IStartupEntryOwner>(), provider.GetRequiredService<IScheduledTaskOwner>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IManagedAutostartPlatform));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IManagedAutostartStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStartupEntryOwner));
    }

    [Fact]
    public void ProductionPagesUseOwnerAndHaveNoLegacyStartupFallback()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "StartupManagerPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "ViewModels", "StartupManagerViewModel.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "Views", "SettingsPage.xaml.cs"));

        Assert.Contains("IStartupEntryOwner startupOwner", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Application.Current as App", page, StringComparison.Ordinal);
        Assert.DoesNotContain("new StartupService", page, StringComparison.Ordinal);
        Assert.DoesNotContain("new StartupService", viewModel, StringComparison.Ordinal);
        Assert.Contains("ManagedAutostartConfirmationEvidence", viewModel, StringComparison.Ordinal);
        Assert.Contains("plan.PlanId,", viewModel, StringComparison.Ordinal);
        Assert.Contains("plan.PlanHash,", viewModel, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<IStartupEntryOwner>()", settings, StringComparison.Ordinal);
        Assert.Contains("_startupOwner.Confirm(plan, evidence", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void AutostartCommandAndWindowLifecycleKeepTrayIntentExplicit()
    {
        var root = FindRoot();
        var platform = File.ReadAllText(Path.Combine(
            root,
            "GameOptimizer.Infrastructure.Windows",
            "Startup",
            "WindowsManagedAutostartInfrastructure.cs"));
        var app = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "App.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "GameOptimizer.Shell", "MainWindow.xaml.cs"));

        Assert.Contains("public const string AutostartArgument = \"--autostart\"", platform, StringComparison.Ordinal);
        Assert.Contains("ManagedAutostartApplicationIdentity.AutostartArgument", platform, StringComparison.Ordinal);
        Assert.Contains("argument.Equals(\"--autostart\"", app, StringComparison.Ordinal);
        Assert.Contains("isAutostartInstance && settings.MinimizeToTray", app, StringComparison.Ordinal);
        Assert.Contains("trayService.MinimizeToTray();", app, StringComparison.Ordinal);
        Assert.Contains("if (_appSettings.MinimizeToTray)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_trayService.MinimizeToTray();", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsAdapterRejectsTaskAndThirdPartyIdentityBeforePlatformAccess()
    {
        var adapter = new WindowsManagedAutostartPlatform(() => @"C:\Program Files\YuqiEngine\YuqiEngine.exe");
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ReadAsync(
            new(ManagedAutostartKind.ScheduledTask, "machine", @"\Vendor\Task"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.ApplyAsync(
            new(ManagedAutostartKind.RegistryRun, "current-user", "HKCU:Run:Other"), ManagedAutostartAction.Enable, default));
    }

    [Fact]
    public void AutostartCommandQuotesExecutableAndIncludesDedicatedLaunchMode()
    {
        var command = WindowsManagedAutostartPlatform.BuildAutostartCommand(
            @"C:\Program Files\YuqiEngine\YuqiEngine.exe");

        Assert.Equal(
            "\"C:\\Program Files\\YuqiEngine\\YuqiEngine.exe\" --autostart",
            command);
        Assert.Throws<ArgumentException>(() =>
            WindowsManagedAutostartPlatform.BuildAutostartCommand("YuqiEngine.exe"));
    }

    private FileManagedAutostartStore Store() => new(_root, new FakeProtector());
    private static StartupEntrySnapshot Snapshot() => new(true, true, @"C:\YuqiEngine.exe", "", "String", "1", new string('B', 64));
    private static ManagedAutostartPlan Plan()
    {
        var id = Guid.NewGuid();
        return new(id, new(ManagedAutostartKind.RegistryRun, "current-user", ManagedAutostartApplicationIdentity.ResourceId),
            ManagedAutostartAction.Disable, new string('B', 64), new string('A', 64), DateTimeOffset.UtcNow.AddMinutes(5), "Medium", true, false);
    }

    private sealed class FakeProtector : IPowerDataProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Select(value => (byte)(value ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] protectedData) => Protect(protectedData);
    }
    private sealed class AllowGate : IManagedAutostartGate { public Task<bool> AuthorizeBaselineAsync(ManagedAutostartPlan plan, CancellationToken cancellationToken) => Task.FromResult(true); }
    private sealed class FakePlatform : IManagedAutostartPlatform
    {
        private StartupEntrySnapshot _state = Snapshot();
        public int Writes { get; private set; }
        public Task<object> ReadAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken) => Task.FromResult<object>(_state);
        public Task ApplyAsync(ManagedAutostartIdentity identity, ManagedAutostartAction action, CancellationToken cancellationToken) { Writes++; _state = _state with { Exists = false, Enabled = false }; return Task.CompletedTask; }
        public Task RestoreAsync(ManagedAutostartIdentity identity, object snapshot, CancellationToken cancellationToken) { Writes++; _state = (StartupEntrySnapshot)snapshot; return Task.CompletedTask; }
        public Task<bool> HasRequiredPermissionAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> IsTrustedTargetAsync(ManagedAutostartIdentity identity, object snapshot, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;
        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan amount) => _value = _value.Add(amount);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "GameOptimizer.Core"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
