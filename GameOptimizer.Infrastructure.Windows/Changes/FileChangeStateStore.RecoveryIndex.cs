using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.Changes;

namespace GameOptimizer.Infrastructure.Windows.Changes;

public sealed partial class FileChangeStateStore
{
    public async Task<IReadOnlyList<RecoveryIndexEntry>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var receipts = await ReadReceiptIndexAsync(cancellationToken).ConfigureAwait(false);
        var claimReader = new FileRecoveryClaimStore(Path.Combine(_rootDirectory, "recovery-claims"));
        var recoveryDirectory = Path.Combine(_rootDirectory, "recovery");
        if (!Directory.Exists(recoveryDirectory))
        {
            return Array.Empty<RecoveryIndexEntry>();
        }

        var entries = new List<RecoveryIndexEntry>();
        foreach (var path in Directory.EnumerateFiles(recoveryDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var recoveryId))
            {
                continue;
            }

            var entry = await ReadIndexEntryAsync(
                path,
                recoveryId,
                receipts,
                claimReader,
                cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries
            .OrderByDescending(static entry => entry.UpdatedAtUtc)
            .ThenBy(static entry => entry.RecoveryId)
            .ToArray();
    }

    private async Task<RecoveryIndexEntry?> ReadIndexEntryAsync(
        string path,
        Guid recoveryId,
        ReceiptIndex receipts,
        IRecoveryClaimStateReader claimReader,
        CancellationToken cancellationToken)
    {
        RecoveryMaterial? material;
        try
        {
            material = await ((IRecoveryRepository)this).LoadAsync(recoveryId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return InvalidMaterialEntry(path, recoveryId);
        }

        if (material is null)
        {
            return InvalidMaterialEntry(path, recoveryId);
        }

        var matchingReceipts = receipts.Valid
            .Where(receipt => IsBoundToMaterial(receipt, material))
            .OrderByDescending(static receipt => receipt.CompletedAtUtc)
            .ThenBy(static receipt => receipt.ReceiptId)
            .ToArray();
        var terminal = matchingReceipts.FirstOrDefault();
        var receiptId = material.ReceiptId != Guid.Empty
            ? material.ReceiptId
            : matchingReceipts.OrderBy(static receipt => receipt.StartedAtUtc).Select(static receipt => (Guid?)receipt.ReceiptId).FirstOrDefault();
        var updatedAt = Max(material.CreatedAtUtc, terminal?.CompletedAtUtc);

        if (material.ReceiptId != Guid.Empty)
        {
            if (receipts.InvalidIds.Contains(material.ReceiptId))
            {
                return Entry(material, receiptId, RecoveryIndexStatus.ManualRecovery, "index.receipt-invalid", updatedAt);
            }

            if (receipts.ById.TryGetValue(material.ReceiptId, out var sourceReceipt)
                && !IsBoundToMaterial(sourceReceipt, material))
            {
                return Entry(material, receiptId, RecoveryIndexStatus.ManualRecovery, "index.receipt-material-mismatch", updatedAt);
            }
        }

        RecoveryClaimStateSnapshot claim;
        try
        {
            claim = await claimReader.ReadStateAsync(
                material.RecoveryId,
                material.IntegrityHash,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Entry(material, receiptId, RecoveryIndexStatus.ManualRecovery, "index.claim-invalid", updatedAt);
        }

        updatedAt = Max(updatedAt, claim.StateUpdatedAtUtc);
        return claim.Status switch
        {
            RecoveryClaimReadStatus.Restored => null,
            RecoveryClaimReadStatus.Missing => Entry(material, receiptId, RecoveryIndexStatus.ManualRecovery, "index.claim-missing", updatedAt),
            RecoveryClaimReadStatus.IntegrityMismatch => Entry(material, receiptId, RecoveryIndexStatus.ManualRecovery, "index.claim-integrity-mismatch", updatedAt),
            RecoveryClaimReadStatus.Available when receiptId is null => Entry(material, null, RecoveryIndexStatus.ManualRecovery, "index.receipt-binding-missing", updatedAt),
            RecoveryClaimReadStatus.Available => Entry(
                material,
                receiptId,
                RecoveryIndexStatus.Recoverable,
                SafeReasonCode(terminal?.ReasonCode, "audit.receipt-missing"),
                updatedAt),
            RecoveryClaimReadStatus.Restoring when HasUncertainTerminal(terminal) => Entry(
                material,
                receiptId,
                RecoveryIndexStatus.ManualRecovery,
                SafeReasonCode(terminal?.ReasonCode, "restore.state-unknown"),
                updatedAt),
            RecoveryClaimReadStatus.Restoring when terminal?.Outcome is ChangeOutcome.RestoredVerified or ChangeOutcome.NoChange => Entry(
                material,
                receiptId,
                RecoveryIndexStatus.ManualRecovery,
                "index.claim-terminal-inconsistent",
                updatedAt),
            RecoveryClaimReadStatus.Restoring => Entry(
                material,
                receiptId,
                RecoveryIndexStatus.Restoring,
                "restore.in-progress",
                updatedAt),
            _ => Entry(material, receiptId, RecoveryIndexStatus.ManualRecovery, "index.state-unknown", updatedAt)
        };
    }

    private async Task<ReceiptIndex> ReadReceiptIndexAsync(CancellationToken cancellationToken)
    {
        var receiptDirectory = Path.Combine(_rootDirectory, "receipts");
        if (!Directory.Exists(receiptDirectory))
        {
            return ReceiptIndex.Empty;
        }

        var valid = new List<OperationReceipt>();
        var byId = new Dictionary<Guid, OperationReceipt>();
        var invalidIds = new HashSet<Guid>();
        foreach (var path in Directory.EnumerateFiles(receiptDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var receiptId))
            {
                continue;
            }

            try
            {
                var receipt = await ((IChangeAuditStore)this).LoadAsync(receiptId, cancellationToken).ConfigureAwait(false);
                if (receipt is not null)
                {
                    valid.Add(receipt);
                    byId[receipt.ReceiptId] = receipt;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                invalidIds.Add(receiptId);
            }
        }

        return new ReceiptIndex(valid, byId, invalidIds);
    }

    private static bool IsBoundToMaterial(OperationReceipt receipt, RecoveryMaterial material) =>
        receipt.RecoveryId == material.RecoveryId
        && receipt.PlanId == material.PlanId
        && string.Equals(receipt.CapabilityId, material.CapabilityId, StringComparison.OrdinalIgnoreCase)
        && receipt.Scope == material.Scope
        && string.Equals(receipt.BeforeFingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
        && string.Equals(receipt.TargetFingerprint, material.TargetFingerprint, StringComparison.Ordinal);

    private static bool HasUncertainTerminal(OperationReceipt? receipt) =>
        receipt?.Outcome is ChangeOutcome.RecoveryRequired
            or ChangeOutcome.FailedPartial
            or ChangeOutcome.CancelledAfterPartialWrite
            or ChangeOutcome.AppliedButUnverified
            or ChangeOutcome.RestoreFailed;

    private static RecoveryIndexEntry InvalidMaterialEntry(string path, Guid recoveryId) =>
        new(
            null,
            recoveryId,
            "index.unavailable",
            "index.unavailable",
            RecoveryIndexStatus.ManualRecovery,
            "index.material-invalid",
            File.GetLastWriteTimeUtc(path));

    private static RecoveryIndexEntry Entry(
        RecoveryMaterial material,
        Guid? receiptId,
        RecoveryIndexStatus status,
        string reasonCode,
        DateTimeOffset updatedAt) =>
        new(
            receiptId,
            material.RecoveryId,
            SafeIdentifier(material.CapabilityId, "index.capability-redacted"),
            SafeIdentifier(material.ResourceKey, "index.resource-redacted"),
            status,
            reasonCode,
            updatedAt);

    private static string SafeReasonCode(string? value, string fallback) =>
        IsSafeIdentifier(value) ? value! : fallback;

    private static string SafeIdentifier(string? value, string fallback) =>
        IsSafeIdentifier(value) ? value! : fallback;

    private static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':')
        && !ContainsSensitiveLabel(value);

    private static bool ContainsSensitiveLabel(string value) =>
        value.Contains("token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || value.Contains("password", StringComparison.OrdinalIgnoreCase)
        || value.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || value.Contains("authorization", StringComparison.OrdinalIgnoreCase)
        || value.Contains("cookie", StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset? right) =>
        right is not null && right.Value > left ? right.Value : left;

    private sealed record ReceiptIndex(
        IReadOnlyList<OperationReceipt> Valid,
        IReadOnlyDictionary<Guid, OperationReceipt> ById,
        IReadOnlySet<Guid> InvalidIds)
    {
        internal static ReceiptIndex Empty { get; } = new(
            Array.Empty<OperationReceipt>(),
            new Dictionary<Guid, OperationReceipt>(),
            new HashSet<Guid>());
    }
}
