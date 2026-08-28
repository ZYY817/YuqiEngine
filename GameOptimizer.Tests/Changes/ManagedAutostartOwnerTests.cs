using System.Collections.Concurrent;
using GameOptimizer.Core.Services.Startup;

namespace GameOptimizer.Tests.Changes;

public sealed class ManagedAutostartOwnerTests
{
    [Fact]
    public async Task ReadOnlyStartupInspection_DoesNotLoadConfirmationSecret()
    {
        var platform = new FakePlatform();
        var secretProvider = new ThrowingSecretProvider();
        var owner = new ManagedAutostartOwner(platform, new FakeGate(), new FakeStore(), secretProvider);

        var snapshot = await owner.InspectAsync(
            new(ManagedAutostartKind.RegistryRun, "current-user", "HKCU:Run:Example"), default);

        Assert.True(snapshot.Exists);
        Assert.Equal(0, secretProvider.CallCount);
        Assert.Equal(1, platform.Reads);
        Assert.Equal(0, platform.Writes);
    }

    [Fact]
    public async Task StartupInspectionReadsOwnedEntryWithoutWriting()
    {
        var fixture = new Fixture();
        var snapshot = await fixture.Owner.InspectAsync(
            new(ManagedAutostartKind.RegistryRun, "current-user", "HKCU:Run:Example"), default);

        Assert.True(snapshot.Exists);
        Assert.True(snapshot.Enabled);
        Assert.Equal(1, fixture.Platform.Reads);
        Assert.Equal(0, fixture.Platform.Writes);
    }

    [Fact]
    public async Task StartupEntry_RequiresConfirmationBaselineAndSupportsVerifiedRecovery()
    {
        var fixture = new Fixture();
        var identity = new ManagedAutostartIdentity(ManagedAutostartKind.RegistryRun, "current-user", "HKCU:Run:Example");

        var prepared = await fixture.Owner.PrepareAsync(identity, ManagedAutostartAction.Disable, default);
        Assert.Equal("ConfirmationRequired", prepared.Status);
        Assert.Equal(0, fixture.Platform.Writes);

        var confirmation = Confirm(fixture.Owner, prepared.Plan!);
        var applied = await fixture.Owner.ExecuteAsync(prepared.Plan!, confirmation, default);
        Assert.True(applied.Success);
        Assert.True(applied.Receipt!.Verified);
        Assert.Equal(1, fixture.Platform.Writes);

        var restored = await fixture.Owner.RestoreAsync(applied.Receipt.ReceiptId, default);
        Assert.True(restored.Success);
        Assert.True(restored.Receipt!.Restored);
        Assert.Equal(2, fixture.Platform.Writes);
    }

    [Fact]
    public async Task ScheduledTask_MicrosoftNamespaceIsBlockedByDefault()
    {
        var fixture = new Fixture();
        var result = await fixture.Owner.PrepareAsync(
            new(ManagedAutostartKind.ScheduledTask, "machine", @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan"),
            ManagedAutostartAction.Disable,
            default);

        Assert.Equal("Blocked", result.Status);
        Assert.Equal(0, fixture.Platform.Reads + fixture.Platform.Writes);
    }

    [Fact]
    public async Task PermissionAndTrustFailuresFailClosed()
    {
        var fixture = new Fixture();
        var identity = new ManagedAutostartIdentity(ManagedAutostartKind.StartupFolder, "machine", "common-startup:sample.lnk");
        fixture.Platform.Permission = false;
        Assert.Equal("PermissionDenied", (await fixture.Owner.PrepareAsync(identity, ManagedAutostartAction.Delete, default)).Status);

        fixture.Platform.Permission = true;
        fixture.Platform.Trusted = false;
        Assert.Equal("UntrustedTarget", (await fixture.Owner.PrepareAsync(identity, ManagedAutostartAction.Delete, default)).Status);
        Assert.Equal(0, fixture.Platform.Writes);
    }

    [Fact]
    public async Task DriftAndReplayAreBlockedBeforeSecondWrite()
    {
        var fixture = new Fixture();
        var identity = new ManagedAutostartIdentity(ManagedAutostartKind.ScheduledTask, "machine", @"\Vendor\Maintenance");
        var prepared = await fixture.Owner.PrepareAsync(identity, ManagedAutostartAction.Disable, default);
        var confirmation = Confirm(fixture.Owner, prepared.Plan!);
        fixture.Platform.State = fixture.Platform.State with { Enabled = false };
        Assert.Equal("DriftBlocked", (await fixture.Owner.ExecuteAsync(prepared.Plan!, confirmation, default)).Status);
        Assert.Equal(0, fixture.Platform.Writes);
        Assert.Equal("ReplayBlocked", (await fixture.Owner.ExecuteAsync(prepared.Plan!, confirmation, default)).Status);
    }

    [Fact]
    public async Task BaselineAndCancelledPrepareDoNotWrite()
    {
        var fixture = new Fixture { Gate = { Allowed = false } };
        var identity = new ManagedAutostartIdentity(ManagedAutostartKind.RegistryRun, "current-user", "HKCU:Run:Example");
        var prepared = await fixture.Owner.PrepareAsync(identity, ManagedAutostartAction.Disable, default);
        Assert.Equal("BaselineBlocked", (await fixture.Owner.ExecuteAsync(
            prepared.Plan!, Confirm(fixture.Owner, prepared.Plan!), default)).Status);
        Assert.Equal(0, fixture.Platform.Writes);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Owner.PrepareAsync(
            identity, ManagedAutostartAction.Disable, new CancellationToken(true)));
    }

    [Fact]
    public void ProductionLegacyWritersAreRemovedFromOwnedFiles()
    {
        foreach (var relative in new[]
        {
            Path.Combine("GameOptimizer.Core", "Services", "Startup", "StartupService.cs"),
            Path.Combine("GameOptimizer.Core", "Optimization", "Operations", "ScheduledTaskOperation.cs")
        })
        {
            var source = File.ReadAllText(Path.Combine(FindRoot(), relative));
            Assert.DoesNotContain("SetValue(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DeleteValue(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Move(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.Delete(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("schtasks", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
        }
    }

    private sealed class Fixture
    {
        public FakePlatform Platform { get; } = new();
        public FakeGate Gate { get; } = new();
        public FakeStore Store { get; } = new();
        public ManagedAutostartOwner Owner { get; }
        public Fixture() => Owner = new(Platform, Gate, Store, Store);
    }

    private static ManagedAutostartConfirmation Confirm(ManagedAutostartOwner owner, ManagedAutostartPlan plan) =>
        owner.Confirm(plan, new(plan.PlanId, plan.PlanHash, DateTimeOffset.UtcNow, true), TimeSpan.FromMinutes(1));

    private sealed class FakePlatform : IManagedAutostartPlatform
    {
        public StartupEntrySnapshot State { get; set; } = new(true, true, "C:\\sample.exe", "", "String", "1", "source");
        public bool Permission { get; set; } = true;
        public bool Trusted { get; set; } = true;
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Task<object> ReadAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads++; return Task.FromResult<object>(State);
        }
        public Task ApplyAsync(ManagedAutostartIdentity identity, ManagedAutostartAction action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Writes++; State = State with { Enabled = action == ManagedAutostartAction.Enable, Exists = action != ManagedAutostartAction.Delete }; return Task.CompletedTask;
        }
        public Task RestoreAsync(ManagedAutostartIdentity identity, object snapshot, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Writes++; State = (StartupEntrySnapshot)snapshot; return Task.CompletedTask;
        }
        public Task<bool> HasRequiredPermissionAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Permission); }
        public Task<bool> IsTrustedTargetAsync(ManagedAutostartIdentity identity, object snapshot, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Trusted); }
    }

    private sealed class FakeGate : IManagedAutostartGate
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> AuthorizeBaselineAsync(ManagedAutostartPlan plan, CancellationToken cancellationToken) => Task.FromResult(Allowed);
    }

    private sealed class FakeStore : IManagedAutostartStore, IManagedAutostartConfirmationSecretProvider
    {
        private readonly ConcurrentDictionary<Guid, (ManagedAutostartPlan, object)> _plans = new();
        private readonly ConcurrentDictionary<Guid, ManagedAutostartReceipt> _receipts = new();
        private readonly ConcurrentDictionary<string, byte> _claims = new();
        private readonly SemaphoreSlim _lease = new(1, 1);
        public Task SavePlanAsync(ManagedAutostartPlan plan, object snapshot, CancellationToken cancellationToken) { _plans[plan.PlanId] = (plan, snapshot); return Task.CompletedTask; }
        public Task<(ManagedAutostartPlan Plan, object Snapshot)?> LoadPlanAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult(_plans.TryGetValue(planId, out var value) ? ((ManagedAutostartPlan, object)?)value : null);
        public Task<bool> TryClaimPlanAsync(Guid planId, string planHash, CancellationToken cancellationToken) => Task.FromResult(_claims.TryAdd("p:" + planId, 0));
        public Task SaveReceiptAsync(ManagedAutostartReceipt receipt, CancellationToken cancellationToken) { _receipts[receipt.ReceiptId] = receipt; return Task.CompletedTask; }
        public Task<ManagedAutostartReceipt?> LoadReceiptAsync(Guid receiptId, CancellationToken cancellationToken) => Task.FromResult(_receipts.GetValueOrDefault(receiptId));
        public Task<bool> TryClaimRestoreAsync(Guid receiptId, CancellationToken cancellationToken) => Task.FromResult(_claims.TryAdd("r:" + receiptId, 0));
        public async Task<IAsyncDisposable> AcquireLeaseAsync(string resourceId, CancellationToken cancellationToken) { await _lease.WaitAsync(cancellationToken); return new Lease(_lease); }
        public byte[] GetConfirmationSecret() => Enumerable.Repeat((byte)0x5A, 32).ToArray();
        private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable { public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; } }
    }

    private sealed class ThrowingSecretProvider : IManagedAutostartConfirmationSecretProvider
    {
        public int CallCount { get; private set; }

        public byte[] GetConfirmationSecret()
        {
            CallCount++;
            throw new InvalidOperationException("Read-only navigation must not load the confirmation secret.");
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "GameOptimizer.Core"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
