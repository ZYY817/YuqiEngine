using System.Collections.Concurrent;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Core.Services.Changes;

/// <summary>
/// In-memory fallback for the centralized change state stores. Used only when no durable
/// platform store (e.g. <c>FileChangeStateStore</c>) has been composed into the container,
/// so that <c>ChangeOrchestrator</c> can still be constructed in Core-only compositions.
/// The Shell always composes the durable Windows store first, which takes precedence.
/// </summary>
internal sealed class InMemoryChangeStateStore : IRecoveryRepository, IChangeJournal, IChangeAuditStore
{
    private readonly ConcurrentDictionary<Guid, RecoveryMaterial> _materials = new();
    private readonly ConcurrentDictionary<Guid, OperationReceipt> _receipts = new();
    private readonly ConcurrentBag<JournalEntry> _journal = [];

    public Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_materials.TryAdd(material.RecoveryId, material))
        {
            throw new InvalidOperationException("恢复材料标识重复，拒绝覆盖。");
        }

        return Task.CompletedTask;
    }

    public Task<RecoveryMaterial?> LoadAsync(Guid recoveryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_materials.GetValueOrDefault(recoveryId));
    }

    public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _journal.Add(entry);
        return Task.CompletedTask;
    }

    public Task SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_receipts.TryAdd(receipt.ReceiptId, receipt))
        {
            throw new InvalidOperationException("操作回执标识重复，拒绝覆盖。");
        }

        return Task.CompletedTask;
    }

    async Task<OperationReceipt?> IChangeAuditStore.LoadAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _receipts.GetValueOrDefault(receiptId);
    }
}
