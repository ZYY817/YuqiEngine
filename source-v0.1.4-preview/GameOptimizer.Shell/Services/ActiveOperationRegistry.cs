namespace GameOptimizer.Shell.Services;

public enum ActiveOperationRisk
{
    Informational,
    Mutating,
    RecoveryCritical
}

public enum ActiveOperationClosePolicy
{
    CancelAndDrain,
    BlockUntilComplete
}

public sealed record ActiveOperationHandoff(
    IReadOnlyList<string> ReceiptIds,
    string? RecoveryInstruction = null);

public sealed record ActiveOperationSnapshot(
    Guid InstanceId,
    string Id,
    string DisplayName,
    ActiveOperationRisk Risk,
    ActiveOperationClosePolicy ClosePolicy,
    DateTimeOffset StartedAt,
    ActiveOperationHandoff? Handoff);

public sealed record ActiveOperationCloseResult(
    bool CanClose,
    bool TimedOut,
    IReadOnlyList<ActiveOperationSnapshot> Remaining,
    IReadOnlyList<string> Errors);

public interface IActiveOperationLease : IDisposable
{
    void UpdateHandoff(ActiveOperationHandoff handoff);
    void Complete(ActiveOperationHandoff? handoff = null);
}

public interface IActiveOperationRegistry
{
    IActiveOperationLease Begin(
        string id,
        string displayName,
        ActiveOperationRisk risk,
        ActiveOperationClosePolicy closePolicy,
        Action? requestCancellation = null);

    IReadOnlyList<ActiveOperationSnapshot> Snapshot();
    Task<ActiveOperationCloseResult> CancelAndDrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class ActiveOperationRegistry : IActiveOperationRegistry
{
    private sealed class Entry
    {
        public required ActiveOperationSnapshot Snapshot { get; set; }
        public Action? Cancel { get; init; }
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationRequested { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    public IActiveOperationLease Begin(string id, string displayName, ActiveOperationRisk risk,
        ActiveOperationClosePolicy closePolicy, Action? requestCancellation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (closePolicy == ActiveOperationClosePolicy.CancelAndDrain && requestCancellation is null)
            throw new ArgumentException("CancelAndDrain operations require a cancellation callback.", nameof(requestCancellation));

        var instanceId = Guid.NewGuid();
        var entry = new Entry
        {
            Snapshot = new(instanceId, id, displayName, risk, closePolicy, DateTimeOffset.UtcNow, null),
            Cancel = requestCancellation
        };
        lock (_gate) _entries.Add(instanceId, entry);
        return new Lease(this, instanceId);
    }

    public IReadOnlyList<ActiveOperationSnapshot> Snapshot()
    {
        lock (_gate) return _entries.Values.Select(entry => entry.Snapshot).ToArray();
    }

    public async Task<ActiveOperationCloseResult> CancelAndDrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Entry[] entries;
        lock (_gate) entries = _entries.Values.ToArray();
        var blockers = entries.Where(entry => entry.Snapshot.ClosePolicy == ActiveOperationClosePolicy.BlockUntilComplete).ToArray();
        if (blockers.Length > 0)
            return new(false, false, blockers.Select(entry => entry.Snapshot).ToArray(), []);

        var errors = new List<string>();
        foreach (var entry in entries)
        {
            try
            {
                lock (_gate)
                {
                    if (entry.CancellationRequested) continue;
                    entry.CancellationRequested = true;
                }
                entry.Cancel?.Invoke();
            }
            catch (Exception ex)
            {
                errors.Add($"{entry.Snapshot.DisplayName}: {ex.Message}");
            }
        }

        try
        {
            await Task.WhenAll(entries.Select(entry => entry.Completion.Task))
                .WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new(false, true, Snapshot(), errors);
        }
        catch (OperationCanceledException)
        {
            return new(false, true, Snapshot(), errors);
        }

        var remaining = Snapshot();
        return new(remaining.Count == 0 && errors.Count == 0, false, remaining, errors);
    }

    private void Update(Guid instanceId, ActiveOperationHandoff handoff)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(instanceId, out var entry))
                entry.Snapshot = entry.Snapshot with { Handoff = handoff };
        }
    }

    private void Complete(Guid instanceId, ActiveOperationHandoff? handoff)
    {
        Entry? entry;
        lock (_gate)
        {
            if (!_entries.Remove(instanceId, out entry)) return;
            if (handoff is not null) entry.Snapshot = entry.Snapshot with { Handoff = handoff };
        }
        entry.Completion.TrySetResult();
    }

    private sealed class Lease(ActiveOperationRegistry owner, Guid instanceId) : IActiveOperationLease
    {
        private int _completed;
        public void UpdateHandoff(ActiveOperationHandoff handoff) => owner.Update(instanceId, handoff);
        public void Complete(ActiveOperationHandoff? handoff = null)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0) owner.Complete(instanceId, handoff);
        }
        public void Dispose() => Complete();
    }
}
