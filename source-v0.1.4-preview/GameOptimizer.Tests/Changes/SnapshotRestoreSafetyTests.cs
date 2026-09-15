using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Tests.Changes;

public sealed class SnapshotRestoreSafetyTests
{
    private const string IdentityA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string IdentityB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public async Task Authorized_plan_is_single_use()
    {
        var claims = new FakeClaimStore();
        var coordinator = Coordinator(new AllowGate(), claims, () => IdentityA);
        var plan = await coordinator.PrepareAsync("snapshot_1", SnapshotRestoreKind.Normal, default);
        var writes = 0;

        var first = await coordinator.ExecuteAsync(
            plan, SnapshotRestoreKind.Normal,
            _ => Task.FromResult(++writes),
            _ => -1,
            default);
        var replay = await coordinator.ExecuteAsync(
            plan, SnapshotRestoreKind.Normal,
            _ => Task.FromResult(++writes),
            _ => -1,
            default);

        Assert.Equal(1, first);
        Assert.Equal(-1, replay);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task Baseline_denial_is_fail_closed_before_claim_and_write()
    {
        var claims = new FakeClaimStore();
        var coordinator = Coordinator(new DenySnapshotRestoreAuthorizationGate(), claims, () => IdentityA);
        var plan = await coordinator.PrepareAsync("snapshot_1", SnapshotRestoreKind.Normal, default);
        var writes = 0;

        var result = await coordinator.ExecuteAsync(
            plan, SnapshotRestoreKind.Normal,
            _ => { writes++; return Task.FromResult("written"); },
            message => message,
            default);

        Assert.Contains("阻断", result);
        Assert.Equal(0, writes);
        Assert.Equal(0, claims.ClaimCount);
    }

    [Fact]
    public async Task Identity_drift_is_blocked_before_claim_and_write()
    {
        var current = IdentityA;
        var claims = new FakeClaimStore();
        var coordinator = Coordinator(new AllowGate(), claims, () => current);
        var plan = await coordinator.PrepareAsync("snapshot_1", SnapshotRestoreKind.DeepRegistry, default);
        current = IdentityB;
        var writes = 0;

        var result = await coordinator.ExecuteAsync(
            plan, SnapshotRestoreKind.DeepRegistry,
            _ => { writes++; return Task.FromResult("written"); },
            message => message,
            default);

        Assert.Contains("发生变化", result);
        Assert.Equal(0, writes);
        Assert.Equal(0, claims.ClaimCount);
    }

    [Fact]
    public async Task Wrong_restore_kind_is_blocked()
    {
        var claims = new FakeClaimStore();
        var coordinator = Coordinator(new AllowGate(), claims, () => IdentityA);
        var plan = await coordinator.PrepareAsync("snapshot_1", SnapshotRestoreKind.Normal, default);
        var writes = 0;

        var result = await coordinator.ExecuteAsync(
            plan, SnapshotRestoreKind.DeepRegistry,
            _ => { writes++; return Task.FromResult("written"); },
            message => message,
            default);

        Assert.Contains("无效", result);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("snapshot/path")]
    public async Task Unsafe_snapshot_identity_is_rejected(string snapshotId)
    {
        var coordinator = Coordinator(new AllowGate(), new FakeClaimStore(), () => IdentityA);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            coordinator.PrepareAsync(snapshotId, SnapshotRestoreKind.Normal, default));
    }

    private static SnapshotRestoreCoordinator Coordinator(
        ISnapshotRestoreAuthorizationGate gate,
        IRecoveryClaimStore claims,
        Func<string> identity) =>
        new(gate, (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(identity());
        }, claims);

    private sealed class AllowGate : ISnapshotRestoreAuthorizationGate
    {
        public Task<SnapshotRestoreAuthorizationDecision> AuthorizeAsync(SnapshotRestorePlan plan, CancellationToken cancellationToken) =>
            Task.FromResult(new SnapshotRestoreAuthorizationDecision(true, "allowed", "allowed"));
    }

    private sealed class FakeClaimStore : IRecoveryClaimStore
    {
        private readonly Dictionary<Guid, (string Hash, State State, Guid? Claim)> _entries = [];
        public int ClaimCount { get; private set; }

        public Task RegisterAvailableAsync(Guid recoveryId, string integrityHash, CancellationToken cancellationToken)
        {
            _entries.Add(recoveryId, (integrityHash, State.Available, null));
            return Task.CompletedTask;
        }

        public Task<RecoveryClaimResult> TryBeginRestoreAsync(Guid recoveryId, string integrityHash, CancellationToken cancellationToken)
        {
            ClaimCount++;
            if (!_entries.TryGetValue(recoveryId, out var entry)) return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Missing, null));
            if (entry.Hash != integrityHash) return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.IntegrityMismatch, null));
            if (entry.State == State.Restored) return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestored, null));
            if (entry.State == State.Restoring) return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestoring, null));
            var claim = Guid.NewGuid();
            _entries[recoveryId] = (integrityHash, State.Restoring, claim);
            return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Claimed, claim));
        }

        public Task MarkRestoredAsync(Guid recoveryId, string integrityHash, Guid claimId, CancellationToken cancellationToken)
        {
            var entry = _entries[recoveryId];
            Assert.Equal((integrityHash, State.Restoring, (Guid?)claimId), entry);
            _entries[recoveryId] = (integrityHash, State.Restored, null);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid recoveryId, string integrityHash, Guid claimId, CancellationToken cancellationToken)
        {
            _entries[recoveryId] = (integrityHash, State.Available, null);
            return Task.CompletedTask;
        }

        private enum State { Available, Restoring, Restored }
    }
}
