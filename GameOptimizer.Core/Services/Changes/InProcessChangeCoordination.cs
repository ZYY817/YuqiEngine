using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Changes;

internal sealed class InMemoryChangePlanStore : IChangePlanStore
{
    private readonly ConcurrentDictionary<Guid, string> _issued = new();
    private readonly ConcurrentDictionary<Guid, byte> _claimed = new();

    public Task RegisterAsync(PreparedChangePlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_issued.TryAdd(plan.PlanId, plan.PlanHash))
        {
            throw new InvalidOperationException("计划标识重复，拒绝覆盖已签发计划。");
        }

        return Task.CompletedTask;
    }

    public Task<PlanClaimStatus> TryClaimAsync(
        Guid planId,
        string planHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_issued.TryGetValue(planId, out var issuedHash))
        {
            return Task.FromResult(PlanClaimStatus.Missing);
        }

        if (!string.Equals(issuedHash, planHash, StringComparison.Ordinal))
        {
            return Task.FromResult(PlanClaimStatus.HashMismatch);
        }

        return Task.FromResult(
            _claimed.TryAdd(planId, 0)
                ? PlanClaimStatus.Claimed
                : PlanClaimStatus.AlreadyClaimed);
    }
}

internal sealed class InMemoryRecoveryClaimStore : IRecoveryClaimStore
{
    private enum State
    {
        Available,
        Restoring,
        Restored
    }

    private sealed record Entry(string IntegrityHash, State State, Guid? ClaimId);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    public Task RegisterAvailableAsync(
        Guid recoveryId,
        string integrityHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_entries.TryAdd(recoveryId, new Entry(integrityHash, State.Available, null))
            && (!_entries.TryGetValue(recoveryId, out var existing)
                || !string.Equals(existing.IntegrityHash, integrityHash, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("恢复材料状态已存在且完整性哈希不一致，拒绝覆盖。");
        }

        return Task.CompletedTask;
    }

    public Task<RecoveryClaimResult> TryBeginRestoreAsync(
        Guid recoveryId,
        string integrityHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            if (!_entries.TryGetValue(recoveryId, out var current))
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Missing, null));
            }

            if (!string.Equals(current.IntegrityHash, integrityHash, StringComparison.Ordinal))
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.IntegrityMismatch, null));
            }

            if (current.State == State.Restored)
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestored, null));
            }

            if (current.State == State.Restoring)
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.AlreadyRestoring, null));
            }

            var claimId = Guid.NewGuid();
            if (_entries.TryUpdate(recoveryId, current with { State = State.Restoring, ClaimId = claimId }, current))
            {
                return Task.FromResult(new RecoveryClaimResult(RecoveryClaimStatus.Claimed, claimId));
            }
        }
    }

    public Task MarkRestoredAsync(
        Guid recoveryId,
        string integrityHash,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            if (!_entries.TryGetValue(recoveryId, out var current)
                || !string.Equals(current.IntegrityHash, integrityHash, StringComparison.Ordinal)
                || current.State != State.Restoring
                || current.ClaimId != claimId)
            {
                throw new InvalidOperationException("恢复材料未被当前恢复流程领取，不能标记完成。");
            }

            if (_entries.TryUpdate(recoveryId, current with { State = State.Restored, ClaimId = null }, current))
            {
                return Task.CompletedTask;
            }
        }
    }

    public Task ReleaseAsync(
        Guid recoveryId,
        string integrityHash,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            if (!_entries.TryGetValue(recoveryId, out var current)
                || !string.Equals(current.IntegrityHash, integrityHash, StringComparison.Ordinal)
                || current.State != State.Restoring
                || current.ClaimId != claimId)
            {
                return Task.CompletedTask;
            }

            if (_entries.TryUpdate(recoveryId, current with { State = State.Available, ClaimId = null }, current))
            {
                return Task.CompletedTask;
            }
        }
    }
}

internal sealed class InProcessChangeResourceLockProvider : IChangeResourceLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IChangeResourceLease?> TryAcquireAsync(
        string resourceKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(resourceKey, static _ => new SemaphoreSlim(1, 1));
        return await semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false)
            ? new Lease(semaphore)
            : null;
    }

    private sealed class Lease : IChangeResourceLease
    {
        private SemaphoreSlim? _semaphore;

        public Lease(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
