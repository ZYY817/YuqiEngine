using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.Services.Startup;

public enum ManagedAutostartKind { RegistryRun, StartupFolder, ScheduledTask }
public enum ManagedAutostartAction { Enable, Disable, Delete }

public sealed record ManagedAutostartIdentity(
    ManagedAutostartKind Kind,
    string Scope,
    string ResourceId);

public sealed record StartupEntrySnapshot(
    bool Exists,
    bool Enabled,
    string CommandPath,
    string Arguments,
    string ValueKind,
    string Version,
    string Fingerprint);

public sealed record ScheduledTaskSnapshot(
    bool Exists,
    bool Enabled,
    string Xml,
    string Principal,
    string Triggers,
    string Actions,
    string Settings,
    string Version,
    string Owner,
    string Fingerprint);

public sealed record ManagedAutostartPlan(
    Guid PlanId,
    ManagedAutostartIdentity Identity,
    ManagedAutostartAction Action,
    string SnapshotFingerprint,
    string PlanHash,
    DateTimeOffset ExpiresAtUtc,
    string Risk,
    bool RequiresExplicitConfirmation,
    bool RequiresAdministrator);

public sealed record ManagedAutostartConfirmation(Guid PlanId, string PlanHash, Guid Nonce, DateTimeOffset ExpiresAtUtc, string Signature);
public sealed record ManagedAutostartConfirmationEvidence(Guid PlanId, string PlanHash, DateTimeOffset ConfirmedAtUtc, bool UserConfirmed);

public sealed record ManagedAutostartReceipt(
    Guid ReceiptId,
    ManagedAutostartPlan Plan,
    object OriginalSnapshot,
    string AppliedFingerprint,
    bool Verified,
    bool Restored,
    string? Failure);

public sealed record ManagedAutostartResult(
    bool Success,
    bool Changed,
    string Status,
    string Message,
    ManagedAutostartPlan? Plan = null,
    ManagedAutostartReceipt? Receipt = null);

public interface IManagedAutostartPlatform
{
    Task<object> ReadAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken);
    Task ApplyAsync(ManagedAutostartIdentity identity, ManagedAutostartAction action, CancellationToken cancellationToken);
    Task RestoreAsync(ManagedAutostartIdentity identity, object snapshot, CancellationToken cancellationToken);
    Task<bool> HasRequiredPermissionAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken);
    Task<bool> IsTrustedTargetAsync(ManagedAutostartIdentity identity, object snapshot, CancellationToken cancellationToken);
}

public interface IManagedAutostartGate
{
    Task<bool> AuthorizeBaselineAsync(ManagedAutostartPlan plan, CancellationToken cancellationToken);
}

public interface IManagedAutostartConfirmationSecretProvider
{
    byte[] GetConfirmationSecret();
}

public interface IManagedAutostartStore
{
    Task SavePlanAsync(ManagedAutostartPlan plan, object snapshot, CancellationToken cancellationToken);
    Task<(ManagedAutostartPlan Plan, object Snapshot)?> LoadPlanAsync(Guid planId, CancellationToken cancellationToken);
    Task<bool> TryClaimPlanAsync(Guid planId, string planHash, CancellationToken cancellationToken);
    Task SaveReceiptAsync(ManagedAutostartReceipt receipt, CancellationToken cancellationToken);
    Task<ManagedAutostartReceipt?> LoadReceiptAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<bool> TryClaimRestoreAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<IAsyncDisposable> AcquireLeaseAsync(string resourceId, CancellationToken cancellationToken);
}

public interface IStartupEntryOwner : IManagedAutostartOwner
{
    Task<StartupEntrySnapshot> InspectAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken);
}
public interface IScheduledTaskOwner : IManagedAutostartOwner { }

public interface IManagedAutostartOwner
{
    Task<ManagedAutostartResult> PrepareAsync(ManagedAutostartIdentity identity, ManagedAutostartAction action, CancellationToken cancellationToken);
    ManagedAutostartConfirmation Confirm(ManagedAutostartPlan plan, ManagedAutostartConfirmationEvidence evidence, TimeSpan lifetime);
    Task<ManagedAutostartResult> ExecuteAsync(ManagedAutostartPlan plan, ManagedAutostartConfirmation confirmation, CancellationToken cancellationToken);
    Task<ManagedAutostartResult> RestoreAsync(Guid receiptId, CancellationToken cancellationToken);
}

/// <summary>Single typed owner for startup entries and scheduled-task state changes.</summary>
public sealed class ManagedAutostartOwner : IStartupEntryOwner, IScheduledTaskOwner
{
    private readonly IManagedAutostartPlatform _platform;
    private readonly IManagedAutostartGate _gate;
    private readonly IManagedAutostartStore _store;
    private readonly TimeProvider _clock;
    private readonly Lazy<byte[]> _confirmationSecret;

    public ManagedAutostartOwner(IManagedAutostartPlatform platform, IManagedAutostartGate gate, IManagedAutostartStore store,
        IManagedAutostartConfirmationSecretProvider secretProvider, TimeProvider? clock = null)
    {
        _platform = platform;
        _gate = gate;
        _store = store;
        _clock = clock ?? TimeProvider.System;
        ArgumentNullException.ThrowIfNull(secretProvider);
        _confirmationSecret = new Lazy<byte[]>(() =>
        {
            var secret = secretProvider.GetConfirmationSecret();
            return secret.Length >= 32
                ? secret
                : throw new InvalidDataException("A durable autostart confirmation secret is required.");
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<StartupEntrySnapshot> InspectAsync(
        ManagedAutostartIdentity identity,
        CancellationToken cancellationToken)
    {
        if (identity.Kind != ManagedAutostartKind.RegistryRun)
            throw new InvalidOperationException("Startup entry inspection only supports the owned registry Run entry.");
        var validation = ValidateIdentity(identity);
        if (validation is not null) throw new InvalidOperationException(validation);
        var snapshot = await _platform.ReadAsync(identity, cancellationToken).ConfigureAwait(false);
        return snapshot as StartupEntrySnapshot
            ?? throw new InvalidDataException("Startup entry inspection returned an invalid snapshot.");
    }

    public async Task<ManagedAutostartResult> PrepareAsync(ManagedAutostartIdentity identity, ManagedAutostartAction action, CancellationToken cancellationToken)
    {
        var validation = ValidateIdentity(identity);
        if (validation is not null) return Failed("Blocked", validation);
        if (!await _platform.HasRequiredPermissionAsync(identity, cancellationToken).ConfigureAwait(false))
            return Failed("PermissionDenied", "Required permission is unavailable.");
        var snapshot = await _platform.ReadAsync(identity, cancellationToken).ConfigureAwait(false);
        if (!await _platform.IsTrustedTargetAsync(identity, snapshot, cancellationToken).ConfigureAwait(false))
            return Failed("UntrustedTarget", "Path, signature, owner or task identity validation failed.");
        var fingerprint = Fingerprint(snapshot);
        var plan = new ManagedAutostartPlan(Guid.NewGuid(), identity, action, fingerprint, string.Empty,
            _clock.GetUtcNow().AddMinutes(5), "Medium", true, identity.Scope == "machine");
        plan = plan with { PlanHash = PlanHash(plan) };
        await _store.SavePlanAsync(plan, snapshot, cancellationToken).ConfigureAwait(false);
        return new(true, false, "ConfirmationRequired", "Explicit confirmation is required.", plan);
    }

    public ManagedAutostartConfirmation Confirm(ManagedAutostartPlan plan, ManagedAutostartConfirmationEvidence evidence, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Confirmation lifetime must be positive and no longer than the plan lifetime.");
        if (!evidence.UserConfirmed || evidence.PlanId != plan.PlanId || evidence.PlanHash != plan.PlanHash
            || evidence.ConfirmedAtUtc > _clock.GetUtcNow() || evidence.ConfirmedAtUtc < _clock.GetUtcNow().AddMinutes(-5))
            throw new InvalidOperationException("Explicit confirmation evidence does not bind the current immutable plan.");
        var token = new ManagedAutostartConfirmation(plan.PlanId, plan.PlanHash, Guid.NewGuid(), _clock.GetUtcNow().Add(lifetime), string.Empty);
        return token with { Signature = ConfirmationMac(token) };
    }

    public async Task<ManagedAutostartResult> ExecuteAsync(ManagedAutostartPlan plan, ManagedAutostartConfirmation confirmation, CancellationToken cancellationToken)
    {
        if (confirmation.PlanId != plan.PlanId || confirmation.PlanHash != plan.PlanHash || confirmation.ExpiresAtUtc <= _clock.GetUtcNow()
            || !Fixed(confirmation.Signature, ConfirmationMac(confirmation with { Signature = string.Empty }))
            || plan.ExpiresAtUtc <= _clock.GetUtcNow() || PlanHash(plan) != plan.PlanHash)
            return Failed("ConfirmationRejected", "Confirmation or plan integrity validation failed.");
        var stored = await _store.LoadPlanAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
        if (stored is null || stored.Value.Plan.PlanHash != plan.PlanHash) return Failed("PlanMissing", "Prepared plan is unavailable.");
        if (!await _store.TryClaimPlanAsync(plan.PlanId, plan.PlanHash, cancellationToken).ConfigureAwait(false))
            return Failed("ReplayBlocked", "Prepared plan was already claimed.");
        await using var lease = await _store.AcquireLeaseAsync(plan.Identity.ResourceId, cancellationToken).ConfigureAwait(false);
        if (!await _gate.AuthorizeBaselineAsync(plan, cancellationToken).ConfigureAwait(false))
            return Failed("BaselineBlocked", "The baseline risk decision was not authorized.");
        var current = await _platform.ReadAsync(plan.Identity, cancellationToken).ConfigureAwait(false);
        if (Fingerprint(current) != plan.SnapshotFingerprint) return Failed("DriftBlocked", "Target changed after preparation.");
        try
        {
            await _platform.ApplyAsync(plan.Identity, plan.Action, cancellationToken).ConfigureAwait(false);
            var applied = await _platform.ReadAsync(plan.Identity, CancellationToken.None).ConfigureAwait(false);
            var appliedFingerprint = Fingerprint(applied);
            if (appliedFingerprint == plan.SnapshotFingerprint)
                return Failed("ReadBackMismatch", "Mutation was not proven by authoritative read-back.");
            var receipt = new ManagedAutostartReceipt(Guid.NewGuid(), plan, stored.Value.Snapshot, appliedFingerprint, true, false, null);
            await _store.SaveReceiptAsync(receipt, CancellationToken.None).ConfigureAwait(false);
            return new(true, true, "Applied", "Change applied and verified; durable recovery is available.", plan, receipt);
        }
        catch (Exception ex)
        {
            return Failed("RecoveryRequired", $"Mutation failed or is uncertain: {ex.Message}");
        }
    }

    public async Task<ManagedAutostartResult> RestoreAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await _store.LoadReceiptAsync(receiptId, cancellationToken).ConfigureAwait(false);
        if (receipt is null || !receipt.Verified || receipt.Restored) return Failed("RecoveryUnavailable", "Verified recovery receipt is unavailable.");
        if (!await _store.TryClaimRestoreAsync(receiptId, cancellationToken).ConfigureAwait(false)) return Failed("ReplayBlocked", "Recovery was already claimed.");
        await using var lease = await _store.AcquireLeaseAsync(receipt.Plan.Identity.ResourceId, cancellationToken).ConfigureAwait(false);
        var current = await _platform.ReadAsync(receipt.Plan.Identity, cancellationToken).ConfigureAwait(false);
        if (Fingerprint(current) != receipt.AppliedFingerprint) return Failed("DriftBlocked", "Target changed after apply; restore is blocked.");
        await _platform.RestoreAsync(receipt.Plan.Identity, receipt.OriginalSnapshot, cancellationToken).ConfigureAwait(false);
        var restored = await _platform.ReadAsync(receipt.Plan.Identity, CancellationToken.None).ConfigureAwait(false);
        if (Fingerprint(restored) != receipt.Plan.SnapshotFingerprint) return Failed("ReadBackMismatch", "Restore read-back mismatch.");
        var terminal = receipt with { Restored = true };
        await _store.SaveReceiptAsync(terminal, CancellationToken.None).ConfigureAwait(false);
        return new(true, true, "Restored", "Original state restored and verified.", receipt.Plan, terminal);
    }

    internal static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty)));
    private static string PlanHash(ManagedAutostartPlan plan) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{plan.PlanId:D}|{plan.Identity.Kind}|{plan.Identity.Scope}|{plan.Identity.ResourceId}|{plan.Action}|{plan.SnapshotFingerprint}|{plan.ExpiresAtUtc.UtcTicks}|{plan.Risk}|{plan.RequiresExplicitConfirmation}|{plan.RequiresAdministrator}")));
    private string ConfirmationMac(ManagedAutostartConfirmation token) => Convert.ToHexString(HMACSHA256.HashData(
        _confirmationSecret.Value, Encoding.UTF8.GetBytes($"{token.PlanId:D}|{token.PlanHash}|{token.Nonce:D}|{token.ExpiresAtUtc.UtcTicks}")));
    private static bool Fixed(string left, string right) { try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); } catch { return false; } }

    private static string? ValidateIdentity(ManagedAutostartIdentity identity)
    {
        if (identity.Scope is not ("current-user" or "machine")) return "Scope is not supported.";
        if (string.IsNullOrWhiteSpace(identity.ResourceId) || identity.ResourceId.IndexOfAny(['\r', '\n', '\0']) >= 0) return "Resource identity is invalid.";
        if (identity.Kind == ManagedAutostartKind.ScheduledTask && identity.ResourceId.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase))
            return "Microsoft/system tasks are blocked by default.";
        return null;
    }

    private static ManagedAutostartResult Failed(string status, string message) => new(false, false, status, message);
}
