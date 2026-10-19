using System.Security.Cryptography;
using System.Text;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Changes;

public sealed partial class FileChangeStateStore : IRecoveryRepository, IChangeJournal, IChangeAuditStore, IChangePlanStore, IRecoveryIndexQuery
{
    private const string RecoveryDocument = "yuqi.change.recovery-material";
    private const string JournalDocument = "yuqi.change.journal-entry";
    private const string ReceiptDocument = "yuqi.change.operation-receipt";
    private const string PlanDocument = "yuqi.change.prepared-plan";

    private readonly string _rootDirectory;
    private readonly string _rootPrefix;

    public FileChangeStateStore()
        : this(Path.Combine(AppPaths.StateDataRoot, "Changes"))
    {
    }

    internal FileChangeStateStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public async Task SaveAsync(RecoveryMaterial material, CancellationToken cancellationToken)
    {
        var path = PathFor("recovery", material.RecoveryId.ToString("N") + ".json");
        try
        {
            await DurableChangeDocument.WriteAsync(
                path,
                RecoveryDocument,
                material,
                overwrite: false,
                ValidateRecoveryMaterial,
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(path))
        {
            var existing = await DurableChangeDocument.ReadAsync<RecoveryMaterial>(
                path,
                RecoveryDocument,
                ValidateRecoveryMaterial,
                cancellationToken).ConfigureAwait(false);
            if (existing is null
                || !FixedTimeHashEquals(
                    DurableChangeDocument.PayloadHash(existing),
                    DurableChangeDocument.PayloadHash(material)))
            {
                throw new InvalidDataException("A different recovery document already exists for this identity.");
            }
        }
    }

    async Task<RecoveryMaterial?> IRecoveryRepository.LoadAsync(Guid recoveryId, CancellationToken cancellationToken)
    {
        EnsureNonEmpty(recoveryId, nameof(recoveryId));
        var material = await DurableChangeDocument.ReadAsync<RecoveryMaterial>(
            PathFor("recovery", recoveryId.ToString("N") + ".json"),
            RecoveryDocument,
            ValidateRecoveryMaterial,
            cancellationToken).ConfigureAwait(false);
        if (material is not null && material.RecoveryId != recoveryId)
        {
            throw new InvalidDataException("The recovery document identity does not match its file name.");
        }

        return material;
    }

    public Task AppendAsync(JournalEntry entry, CancellationToken cancellationToken) =>
        DurableChangeDocument.WriteAsync(
            PathFor(
                "journal",
                entry.PlanId.ToString("N"),
                $"{entry.TimestampUtc.UtcTicks:D19}-{Guid.NewGuid():N}.json"),
            JournalDocument,
            entry,
            overwrite: false,
            ValidateJournalEntry,
            cancellationToken);

    async Task IChangeAuditStore.SaveAsync(OperationReceipt receipt, CancellationToken cancellationToken)
    {
        var path = PathFor("receipts", receipt.ReceiptId.ToString("N") + ".json");
        using var receiptLease = await DurableChangeDocument.AcquireCommitLeaseAsync(path, cancellationToken).ConfigureAwait(false);
        var existing = await DurableChangeDocument.ReadAsync<OperationReceipt>(
                path,
                ReceiptDocument,
                ValidateReceipt,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (!HasSameReceiptIdentity(existing, receipt))
            {
                throw new InvalidDataException("A receipt update cannot change the operation identity or immutable fingerprints.");
            }

            if (ShouldPreserveExistingReceipt(existing, receipt))
            {
                return;
            }
        }

        await DurableChangeDocument.WriteAsync(
                path,
                ReceiptDocument,
                receipt,
                overwrite: true,
                ValidateReceipt,
                cancellationToken,
                acquireCommitLease: false)
            .ConfigureAwait(false);
    }

    async Task<OperationReceipt?> IChangeAuditStore.LoadAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        EnsureNonEmpty(receiptId, nameof(receiptId));
        var receipt = await DurableChangeDocument.ReadAsync<OperationReceipt>(
            PathFor("receipts", receiptId.ToString("N") + ".json"),
            ReceiptDocument,
            ValidateReceipt,
            cancellationToken).ConfigureAwait(false);
        if (receipt is not null && receipt.ReceiptId != receiptId)
        {
            throw new InvalidDataException("The receipt identity does not match its file name.");
        }

        return receipt;
    }

    public async Task RegisterAsync(PreparedChangePlan plan, CancellationToken cancellationToken)
    {
        var path = PathFor("plans", "issued", plan.PlanId.ToString("N") + ".json");
        try
        {
            await DurableChangeDocument.WriteAsync(
                path,
                PlanDocument,
                plan,
                overwrite: false,
                ValidatePlan,
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(path))
        {
            throw new InvalidOperationException("The prepared plan identity has already been issued and cannot be overwritten.");
        }
    }

    public async Task<PlanClaimStatus> TryClaimAsync(
        Guid planId,
        string planHash,
        CancellationToken cancellationToken)
    {
        EnsureNonEmpty(planId, nameof(planId));
        EnsureSha256(planHash, nameof(planHash));
        var issued = await DurableChangeDocument.ReadAsync<PreparedChangePlan>(
            PathFor("plans", "issued", planId.ToString("N") + ".json"),
            PlanDocument,
            ValidatePlan,
            cancellationToken).ConfigureAwait(false);
        if (issued is null)
        {
            return PlanClaimStatus.Missing;
        }

        if (issued.PlanId != planId
            || !FixedTimeHashEquals(issued.PlanHash, planHash))
        {
            return PlanClaimStatus.HashMismatch;
        }

        var claimPath = PathFor("plans", "claims", planId.ToString("N") + ".claim");
        Directory.CreateDirectory(Path.GetDirectoryName(claimPath)!);
        try
        {
            await WriteClaimAsync(claimPath, planId, planHash, cancellationToken).ConfigureAwait(false);
            return PlanClaimStatus.Claimed;
        }
        catch (IOException) when (File.Exists(claimPath))
        {
            var claim = await DurableChangeDocument.ReadAsync<PlanClaim>(
                claimPath,
                "yuqi.change.plan-claim",
                ValidatePlanClaim,
                cancellationToken).ConfigureAwait(false);
            return claim is not null
                && claim.PlanId == planId
                && FixedTimeHashEquals(claim.PlanHash, planHash)
                    ? PlanClaimStatus.AlreadyClaimed
                    : PlanClaimStatus.HashMismatch;
        }
    }

    private Task WriteClaimAsync(string path, Guid planId, string planHash, CancellationToken cancellationToken) =>
        DurableChangeDocument.WriteAsync(
            path,
            "yuqi.change.plan-claim",
            new PlanClaim(planId, planHash),
            overwrite: false,
            ValidatePlanClaim,
            cancellationToken);

    private string PathFor(params string[] segments)
    {
        if (segments.Length == 0
            || segments.Any(static segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0))
        {
            throw new InvalidOperationException("The persistence path contains an unsafe segment.");
        }

        var candidate = Path.GetFullPath(Path.Combine(new[] { _rootDirectory }.Concat(segments).ToArray()));
        if (!candidate.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The persistence path escapes the configured root.");
        }

        return candidate;
    }

    private static void ValidateRecoveryMaterial(RecoveryMaterial material)
    {
        EnsureNonEmpty(material.RecoveryId, nameof(material.RecoveryId));
        EnsureNonEmpty(material.PlanId, nameof(material.PlanId));
        EnsureCommon(material.CapabilityId, material.DefinitionVersion, material.Scope, material.ResourceKey);
        EnsureText(material.BeforePayload, nameof(material.BeforePayload));
        EnsureText(material.BeforeFingerprint, nameof(material.BeforeFingerprint));
        EnsureText(material.TargetFingerprint, nameof(material.TargetFingerprint));
        EnsureTimestamp(material.CreatedAtUtc, nameof(material.CreatedAtUtc));
        EnsureSha256(material.IntegrityHash, nameof(material.IntegrityHash));
        var expectedHash = Hash(
            material.RecoveryId.ToString("N"),
            material.PlanId.ToString("N"),
            material.CapabilityId,
            material.DefinitionVersion,
            material.Scope.Kind.ToString(),
            material.Scope.Identity,
            material.BeforePayload,
            material.BeforeFingerprint,
            material.TargetFingerprint,
            material.ResourceKey,
            material.CreatedAtUtc.UtcTicks.ToString());
        if (!FixedTimeHashEquals(material.IntegrityHash, expectedHash))
        {
            throw new InvalidDataException("The recovery material integrity hash does not match its content.");
        }

        if (material.ReceiptId != Guid.Empty
            && material.ReceiptId != RecoveryMaterialBinding.ComputeReceiptId(
                material.RecoveryId,
                material.ResourceKey,
                material.IntegrityHash))
        {
            throw new InvalidDataException("The recovery material ReceiptId binding does not match its identity.");
        }
    }

    private static void ValidatePlan(PreparedChangePlan plan)
    {
        EnsureNonEmpty(plan.PlanId, nameof(plan.PlanId));
        EnsureSha256(plan.PlanHash, nameof(plan.PlanHash));
        EnsureTimestamp(plan.PreparedAtUtc, nameof(plan.PreparedAtUtc));
        EnsureTimestamp(plan.ExpiresAtUtc, nameof(plan.ExpiresAtUtc));
        if (plan.ExpiresAtUtc <= plan.PreparedAtUtc)
        {
            throw new InvalidDataException("The prepared plan expiration must be after its creation time.");
        }

        EnsureCommon(plan.Change.CapabilityId, plan.Change.DefinitionVersion, plan.Change.Scope, plan.Change.ResourceKey);
        EnsureText(plan.Change.BeforePayload, nameof(plan.Change.BeforePayload));
        EnsureText(plan.Change.BeforeFingerprint, nameof(plan.Change.BeforeFingerprint));
        EnsureText(plan.Change.TargetPayload, nameof(plan.Change.TargetPayload));
        EnsureText(plan.Change.TargetFingerprint, nameof(plan.Change.TargetFingerprint));
        EnsureText(plan.ExecutionPolicyVersion, nameof(plan.ExecutionPolicyVersion));
        EnsureText(plan.ExecutionPolicyFingerprint, nameof(plan.ExecutionPolicyFingerprint));
        if (!Enum.IsDefined(plan.Risk))
        {
            throw new InvalidDataException("The prepared plan risk is invalid.");
        }

        var expectedHash = Hash(
            plan.PlanId.ToString("N"),
            plan.Change.CapabilityId,
            plan.Change.DefinitionVersion,
            plan.Change.Scope.Kind.ToString(),
            plan.Change.Scope.Identity,
            plan.Change.BeforePayload,
            plan.Change.BeforeFingerprint,
            plan.Change.TargetPayload,
            plan.Change.TargetFingerprint,
            plan.Change.ResourceKey,
            plan.PreparedAtUtc.UtcTicks.ToString(),
            plan.ExpiresAtUtc.UtcTicks.ToString(),
            plan.RequiresAdministrator.ToString(),
            plan.RequiresExplicitConfirmation.ToString(),
            plan.RequiresRecoveryMaterial.ToString(),
            plan.Risk.ToString(),
            plan.ExecutionPolicyVersion,
            plan.ExecutionPolicyFingerprint);
        if (!FixedTimeHashEquals(plan.PlanHash, expectedHash))
        {
            throw new InvalidDataException("The prepared plan hash does not match its content.");
        }
    }

    private static void ValidateReceipt(OperationReceipt receipt)
    {
        EnsureNonEmpty(receipt.ReceiptId, nameof(receipt.ReceiptId));
        EnsureNonEmpty(receipt.PlanId, nameof(receipt.PlanId));
        EnsureText(receipt.CapabilityId, nameof(receipt.CapabilityId));
        EnsureScope(receipt.Scope);
        EnsureText(receipt.BeforeFingerprint, nameof(receipt.BeforeFingerprint));
        EnsureText(receipt.TargetFingerprint, nameof(receipt.TargetFingerprint));
        EnsureText(receipt.ReasonCode, nameof(receipt.ReasonCode));
        EnsureText(receipt.Message, nameof(receipt.Message));
        EnsureTimestamp(receipt.StartedAtUtc, nameof(receipt.StartedAtUtc));
        EnsureTimestamp(receipt.CompletedAtUtc, nameof(receipt.CompletedAtUtc));
        if (receipt.CompletedAtUtc < receipt.StartedAtUtc)
        {
            throw new InvalidDataException("The receipt completion time precedes its start time.");
        }
    }

    private static void ValidateJournalEntry(JournalEntry entry)
    {
        EnsureNonEmpty(entry.PlanId, nameof(entry.PlanId));
        EnsureText(entry.CapabilityId, nameof(entry.CapabilityId));
        EnsureText(entry.Stage, nameof(entry.Stage));
        EnsureText(entry.Details, nameof(entry.Details));
        EnsureTimestamp(entry.TimestampUtc, nameof(entry.TimestampUtc));
    }

    private static bool HasSameReceiptIdentity(OperationReceipt left, OperationReceipt right) =>
        left.ReceiptId == right.ReceiptId
        && left.PlanId == right.PlanId
        && left.RecoveryId == right.RecoveryId
        && string.Equals(left.CapabilityId, right.CapabilityId, StringComparison.Ordinal)
        && left.Scope == right.Scope
        && string.Equals(left.BeforeFingerprint, right.BeforeFingerprint, StringComparison.Ordinal)
        && string.Equals(left.TargetFingerprint, right.TargetFingerprint, StringComparison.Ordinal)
        && left.StartedAtUtc == right.StartedAtUtc;

    private static bool ShouldPreserveExistingReceipt(OperationReceipt existing, OperationReceipt incoming)
    {
        var existingRank = ReceiptSafetyRank(existing.Outcome);
        var incomingRank = ReceiptSafetyRank(incoming.Outcome);
        return existingRank > incomingRank
            || (existingRank == incomingRank && existing.CompletedAtUtc > incoming.CompletedAtUtc);
    }

    private static int ReceiptSafetyRank(ChangeOutcome outcome) => outcome switch
    {
        ChangeOutcome.RecoveryRequired => 100,
        ChangeOutcome.FailedPartial or ChangeOutcome.CancelledAfterPartialWrite or ChangeOutcome.AppliedButUnverified => 90,
        ChangeOutcome.RestoreFailed => 80,
        ChangeOutcome.FailedNoChange or ChangeOutcome.CancelledBeforeWrite => 70,
        ChangeOutcome.Blocked or ChangeOutcome.Denied or ChangeOutcome.Conflict => 60,
        ChangeOutcome.SucceededVerified or ChangeOutcome.RestoredVerified => 40,
        ChangeOutcome.NoChange => 30,
        _ => 50
    };

    private static void ValidatePlanClaim(PlanClaim claim)
    {
        EnsureNonEmpty(claim.PlanId, nameof(claim.PlanId));
        EnsureSha256(claim.PlanHash, nameof(claim.PlanHash));
    }

    private static void EnsureCommon(string capabilityId, string definitionVersion, TargetScope scope, string resourceKey)
    {
        EnsureText(capabilityId, nameof(capabilityId));
        EnsureText(definitionVersion, nameof(definitionVersion));
        EnsureScope(scope);
        EnsureText(resourceKey, nameof(resourceKey));
    }

    private static void EnsureScope(TargetScope scope)
    {
        if (!Enum.IsDefined(scope.Kind))
        {
            throw new InvalidDataException("The target scope kind is invalid.");
        }

        EnsureText(scope.Identity, nameof(scope.Identity));
    }

    private static void EnsureText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{name} is required.");
        }
    }

    private static void EnsureNonEmpty(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new InvalidDataException($"{name} must not be empty.");
        }
    }

    private static void EnsureTimestamp(DateTimeOffset value, string name)
    {
        if (value == default)
        {
            throw new InvalidDataException($"{name} must not be the default timestamp.");
        }
    }

    private static void EnsureSha256(string value, string name)
    {
        if (value.Length != 64 || !value.All(static character => char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException($"{name} must be a SHA-256 hexadecimal value.");
        }
    }

    private static bool FixedTimeHashEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    private static string Hash(params string[] values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001F', values))));

    private sealed record PlanClaim(Guid PlanId, string PlanHash);
}
