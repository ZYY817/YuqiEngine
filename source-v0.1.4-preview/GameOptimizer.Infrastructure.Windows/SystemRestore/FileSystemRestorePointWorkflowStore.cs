using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.SystemRestore;

namespace GameOptimizer.Infrastructure.Windows.SystemRestore;

public enum SystemRestorePointStoreMigrationDisposition
{
    AlreadyCurrent,
    Migrated
}

public sealed partial class FileSystemRestorePointWorkflowStore : ISystemRestorePointWorkflowStore
{
    private const int SchemaVersion = 3;
    private const int LegacySchemaVersion = 2;
    private const int AuthenticationKeyBytes = 32;
    private const int MaximumPayloadBytes = 1024 * 1024;
    private const int MaximumDocumentBytes = 2 * 1024 * 1024;
    private const string LegacyKeyFileName = ".system-restore-auth-key.v1";
    private const string KeyRingFileName = ".system-restore-auth-keyring.v2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    private readonly string _directory;
    private readonly bool _usesSuppliedKeys;
    private readonly object _keyGate = new();
    private AuthenticationKeyRing? _cachedKeyRing;

    public FileSystemRestorePointWorkflowStore(string directory)
        : this(directory, null)
    {
    }

    public FileSystemRestorePointWorkflowStore(string directory, byte[]? authenticationKey)
        : this(directory, authenticationKey, Array.Empty<byte[]>())
    {
    }

    public FileSystemRestorePointWorkflowStore(
        string directory,
        byte[]? authenticationKey,
        IReadOnlyList<byte[]> previousAuthenticationKeys)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A durable System Restore workflow directory is required.", nameof(directory));
        }
        if (authenticationKey is { Length: not AuthenticationKeyBytes })
        {
            throw new ArgumentException($"The System Restore workflow authentication key must be {AuthenticationKeyBytes} bytes.", nameof(authenticationKey));
        }

        ArgumentNullException.ThrowIfNull(previousAuthenticationKeys);
        if (previousAuthenticationKeys.Any(key => key.Length != AuthenticationKeyBytes))
        {
            throw new ArgumentException($"Every previous System Restore workflow authentication key must be {AuthenticationKeyBytes} bytes.", nameof(previousAuthenticationKeys));
        }

        _directory = Path.GetFullPath(directory);
        _usesSuppliedKeys = authenticationKey is not null;
        if (authenticationKey is not null)
        {
            _cachedKeyRing = CreateKeyRing(authenticationKey, previousAuthenticationKeys);
        }
    }

    public Task SaveCreatePlanAsync(SystemRestorePointCreatePlan plan, CancellationToken cancellationToken) =>
        WriteAsync(CreatePlanPath(plan.PlanId), "create-plan", plan.PlanId, plan, overwrite: false, cancellationToken);

    public Task<SystemRestorePointCreatePlan?> LoadCreatePlanAsync(Guid planId, CancellationToken cancellationToken) =>
        ReadAsync<SystemRestorePointCreatePlan>(CreatePlanPath(planId), "create-plan", planId, cancellationToken);

    public async Task<SystemRestorePointCreateClaim?> TryClaimCreatePlanAsync(
        SystemRestorePointCreateClaim claim,
        CancellationToken cancellationToken)
    {
        ValidateClaim(claim);
        try
        {
            await WriteAsync(ClaimPath(claim.PlanId), "create-claim", claim.PlanId, claim, overwrite: false, cancellationToken)
                .ConfigureAwait(false);
            return claim;
        }
        catch (IOException) when (File.Exists(ClaimPath(claim.PlanId)))
        {
            var existing = await LoadCreateClaimAsync(claim.PlanId, claim.PlanHash, cancellationToken).ConfigureAwait(false);
            return existing?.ClaimId == claim.ClaimId ? claim : null;
        }
    }

    public async Task<SystemRestorePointCreateClaim?> LoadCreateClaimAsync(
        Guid planId,
        string planHash,
        CancellationToken cancellationToken)
    {
        var claim = await ReadAsync<SystemRestorePointCreateClaim>(
            ClaimPath(planId), "create-claim", planId, cancellationToken).ConfigureAwait(false);
        if (claim is not null && !FixedEquals(ValidateHash(planHash), claim.PlanHash))
        {
            throw new InvalidDataException("The System Restore create claim does not match the requested plan hash.");
        }
        return claim;
    }

    public async Task SaveCreateClaimAsync(
        SystemRestorePointCreateClaim claim,
        CancellationToken cancellationToken)
    {
        ValidateClaim(claim);
        var existing = await LoadCreateClaimAsync(claim.PlanId, claim.PlanHash, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The System Restore create claim does not exist.");
        var validTransition = existing.State == SystemRestorePointCreateClaimState.Executing
            ? claim.State != SystemRestorePointCreateClaimState.Executing
            : existing.State is SystemRestorePointCreateClaimState.Interrupted or SystemRestorePointCreateClaimState.Unknown
                && claim.State == SystemRestorePointCreateClaimState.Verified;
        if (existing.ClaimId != claim.ClaimId
            || existing.ReceiptId != claim.ReceiptId
            || !validTransition
            || claim.UpdatedAtUtc < existing.UpdatedAtUtc)
        {
            throw new InvalidDataException("The System Restore create claim transition is invalid or replayed.");
        }

        await WriteAsync(ClaimPath(claim.PlanId), "create-claim", claim.PlanId, claim, overwrite: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SystemRestorePointCreateClaim>> ListCreateClaimsAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_directory))
        {
            try
            {
                _ = File.GetAttributes(_directory);
            }
            catch (DirectoryNotFoundException)
            {
                return Array.Empty<SystemRestorePointCreateClaim>();
            }
            catch (FileNotFoundException)
            {
                return Array.Empty<SystemRestorePointCreateClaim>();
            }
        }

        var claims = new List<SystemRestorePointCreateClaim>();
        foreach (var path in Directory.EnumerateFiles(_directory, "create-plan-*.claim.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            const string prefix = "create-plan-";
            const string suffix = ".claim.json";
            var fileName = Path.GetFileName(path);
            var text = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
            if (!Guid.TryParseExact(text, "D", out var planId))
            {
                throw new InvalidDataException($"The System Restore claim filename is invalid: {fileName}");
            }

            var claim = await ReadAsync<SystemRestorePointCreateClaim>(path, "create-claim", planId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException($"The System Restore claim disappeared while being enumerated: {fileName}");
            claims.Add(claim);
        }

        return claims;
    }

    public Task SaveRestorePlanAsync(SystemRestoreExecutionPlan plan, CancellationToken cancellationToken) =>
        WriteAsync(RestorePlanPath(plan.PlanId), "restore-plan", plan.PlanId, plan, overwrite: false, cancellationToken);

    public Task<SystemRestoreExecutionPlan?> LoadRestorePlanAsync(Guid planId, CancellationToken cancellationToken) =>
        ReadAsync<SystemRestoreExecutionPlan>(RestorePlanPath(planId), "restore-plan", planId, cancellationToken);

    public async Task SaveReceiptAsync(SystemRestorePointReceipt receipt, CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var path = ReceiptPath(receipt.ReceiptId);
        var existing = await ReadAsync<SystemRestorePointReceipt>(path, "receipt", receipt.ReceiptId, cancellationToken)
            .ConfigureAwait(false);
        var validTransition = existing is null
            || existing.State == SystemRestorePointReceiptState.Executing
                && receipt.State != SystemRestorePointReceiptState.Executing
            || existing.State == SystemRestorePointReceiptState.Unknown
                && receipt.State == SystemRestorePointReceiptState.Verified;
        if (existing is not null
            && (!validTransition
                || existing.PlanId != receipt.PlanId
                || !FixedEquals(existing.PlanHash, receipt.PlanHash)
                || existing.ClaimId != receipt.ClaimId))
        {
            throw new InvalidDataException("The System Restore receipt transition is invalid or replayed.");
        }

        await WriteAsync(path, "receipt", receipt.ReceiptId, receipt, overwrite: existing is not null, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<SystemRestorePointReceipt?> LoadReceiptAsync(Guid receiptId, CancellationToken cancellationToken) =>
        ReadAsync<SystemRestorePointReceipt>(ReceiptPath(receiptId), "receipt", receiptId, cancellationToken);

    public async Task<SystemRestorePointReceiptRecoveryResult> RecoverReceiptAsync(
        SystemRestorePointReceipt replacement,
        CancellationToken cancellationToken)
    {
        ValidateReceipt(replacement);
        if (replacement.State == SystemRestorePointReceiptState.Executing)
        {
            throw new InvalidDataException("Receipt recovery requires a terminal replacement.");
        }

        Directory.CreateDirectory(_directory);
        var path = ReceiptPath(replacement.ReceiptId);
        var recoveryLockPath = path + ".recovery.lock";
        await using var recoveryLock = new FileStream(
            recoveryLockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        try
        {
            var existing = await ReadAsync<SystemRestorePointReceipt>(path, "receipt", replacement.ReceiptId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                EnsureReceiptIdentity(existing, replacement);
                if (existing != replacement)
                {
                    throw new InvalidDataException("A different valid receipt appeared during recovery; automatic replacement is blocked.");
                }
                return new(SystemRestorePointReceiptRecoveryDisposition.ExistingValid, existing);
            }
        }
        catch (InvalidDataException) when (File.Exists(path))
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var digest = Convert.ToHexString(SHA256.HashData(bytes));
            var quarantine = path + $".corrupt-{digest}.json";
            if (File.Exists(quarantine))
            {
                throw new InvalidDataException("The corrupt receipt has already been quarantined; automatic overwrite is blocked.");
            }

            File.Move(path, quarantine, overwrite: false);
            try
            {
                await WriteAsync(path, "receipt", replacement.ReceiptId, replacement, overwrite: false, cancellationToken)
                    .ConfigureAwait(false);
                return new(SystemRestorePointReceiptRecoveryDisposition.ReplacedCorrupt, replacement, quarantine);
            }
            catch
            {
                if (!File.Exists(path) && File.Exists(quarantine))
                {
                    File.Move(quarantine, path, overwrite: false);
                }
                throw;
            }
        }

        await WriteAsync(path, "receipt", replacement.ReceiptId, replacement, overwrite: false, cancellationToken)
            .ConfigureAwait(false);
        return new(SystemRestorePointReceiptRecoveryDisposition.CreatedMissing, replacement);
    }

    public async Task<ISystemRestorePointResourceLease> AcquireResourceLeaseAsync(
        string resourceKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(resourceKey, SystemRestorePointIdentityConstants.ResourceKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The System Restore resource key is not owned by this store.");
        }
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "system-volume.lock");
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new ResourceLease(resourceKey, stream);
            }
            catch (IOException) when (stopwatch.Elapsed < timeout)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("The System Restore resource lease timed out.");
    }

    private async Task WriteAsync<T>(
        string path,
        string kind,
        Guid identity,
        T payload,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        if (payloadBytes.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The System Restore workflow payload is too large.");
        }

        var payloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes));
        var keyRing = GetAuthenticationKeyRing();
        var envelope = new Envelope(
            SchemaVersion,
            kind,
            identity,
            payloadHash,
            keyRing.ActiveKeyId,
            ComputeMac(SchemaVersion, kind, identity, payloadHash, keyRing.Keys[keyRing.ActiveKeyId]),
            payloadBytes);
        var document = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (document.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException("The System Restore workflow document is too large.");
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await WriteDurableFileAsync(temporary, document, FileMode.CreateNew, cancellationToken).ConfigureAwait(false);
            _ = await ReadAsync<T>(temporary, kind, identity, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The durable System Restore temporary document could not be read back.");
            File.Move(temporary, path, overwrite);
            _ = await ReadAsync<T>(path, kind, identity, CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidDataException("The committed System Restore workflow document could not be read back.");
        }
        finally
        {
            TryDeleteTemporary(temporary);
        }
    }

    private async Task<T?> ReadAsync<T>(
        string path,
        string expectedKind,
        Guid expectedIdentity,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException("The System Restore workflow document size is invalid.");
        }

        try
        {
            var document = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<Envelope>(document, JsonOptions)
                ?? throw new InvalidDataException("The System Restore workflow envelope is empty.");
            if (envelope.SchemaVersion != SchemaVersion
                || !string.Equals(envelope.Kind, expectedKind, StringComparison.Ordinal)
                || envelope.Identity != expectedIdentity)
            {
                throw envelope.SchemaVersion == LegacySchemaVersion
                    ? new InvalidDataException("Legacy System Restore workflow schema 2 requires explicit authenticated migration; automatic loading is blocked.")
                    : new InvalidDataException("The System Restore workflow schema or envelope identity is invalid.");
            }

            var payloadBytes = envelope.Payload;
            if (payloadBytes.Length <= 0 || payloadBytes.Length > MaximumPayloadBytes)
            {
                throw new InvalidDataException("The System Restore workflow payload size is invalid.");
            }
            var actualHash = Convert.ToHexString(SHA256.HashData(payloadBytes));
            var keyRing = GetAuthenticationKeyRing();
            if (string.IsNullOrWhiteSpace(envelope.KeyId)
                || !keyRing.Keys.TryGetValue(envelope.KeyId, out var authenticationKey)
                || !FixedEquals(actualHash, envelope.PayloadSha256)
                || !FixedEquals(
                    ComputeMac(envelope.SchemaVersion, envelope.Kind, envelope.Identity, actualHash, authenticationKey),
                    envelope.EnvelopeHmacSha256))
            {
                throw new InvalidDataException("The System Restore workflow document authentication is invalid.");
            }

            var payload = JsonSerializer.Deserialize<T>(payloadBytes, JsonOptions)
                ?? throw new InvalidDataException("The System Restore workflow payload is empty.");
            ValidatePayload(payload);
            return payload;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The System Restore workflow document is malformed or incompatible.", ex);
        }
    }

    private static string ComputeMac(int schemaVersion, string kind, Guid identity, string payloadHash, byte[] key)
    {
        var canonical = Encoding.UTF8.GetBytes($"{schemaVersion}\n{kind}\n{identity:D}\n{ValidateHash(payloadHash)}");
        return Convert.ToHexString(HMACSHA256.HashData(key, canonical));
    }

    private AuthenticationKeyRing GetAuthenticationKeyRing()
    {
        if (_cachedKeyRing is not null)
        {
            return _cachedKeyRing;
        }

        lock (_keyGate)
        {
            return _cachedKeyRing ??= LoadOrCreateProtectedAuthenticationKeyRing();
        }
    }

    private static async Task WriteDurableFileAsync(
        string path,
        byte[] data,
        FileMode mode,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            mode,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void WriteDurableFile(string path, byte[] data, FileMode mode)
    {
        using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    private static byte[] ProtectKey(byte[] key) => TransformWithDpapi(key, protect: true);
    private static byte[] ProtectKeyRing(byte[] serializedKeyRing) => TransformWithDpapi(serializedKeyRing, protect: true);
    private static byte[] UnprotectKeyRing(byte[] protectedKeyRing) => TransformWithDpapi(protectedKeyRing, protect: false);
    private static byte[] UnprotectKey(byte[] protectedKey)
    {
        var key = TransformWithDpapi(protectedKey, protect: false);
        return key.Length == AuthenticationKeyBytes
            ? key
            : throw new InvalidDataException("The protected System Restore authentication key has an invalid length.");
    }

    private static byte[] TransformWithDpapi(byte[] input, bool protect)
    {
        var inputPointer = Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var inputBlob = new DataBlob { Size = input.Length, Data = inputPointer };
            DataBlob outputBlob;
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out outputBlob);
            if (!succeeded)
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            try
            {
                var output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, output.Length);
                return output;
            }
            finally
            {
                LocalFree(outputBlob.Data);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inputPointer);
        }
    }

    private static void ValidateClaim(SystemRestorePointCreateClaim claim)
    {
        if (claim.ClaimId == Guid.Empty
            || claim.PlanId == Guid.Empty
            || claim.ReceiptId == Guid.Empty
            || claim.OwnerId != SystemRestorePointIdentityConstants.OwnerId
            || claim.Scope != SystemRestorePointIdentityConstants.Scope
            || claim.ResourceKey != SystemRestorePointIdentityConstants.ResourceKey)
        {
            throw new InvalidDataException("The System Restore create claim identity is invalid.");
        }
        _ = ValidateHash(claim.PlanHash);
        var validState = claim.State switch
        {
            SystemRestorePointCreateClaimState.Executing => claim.MutationState == SystemRestorePointMutationState.Unknown,
            SystemRestorePointCreateClaimState.Verified => claim.MutationState == SystemRestorePointMutationState.Changed,
            SystemRestorePointCreateClaimState.Failed => claim.MutationState == SystemRestorePointMutationState.NotChanged,
            SystemRestorePointCreateClaimState.Interrupted => claim.MutationState == SystemRestorePointMutationState.Unknown,
            SystemRestorePointCreateClaimState.Unknown => claim.MutationState != SystemRestorePointMutationState.NotChanged,
            _ => false
        };
        if (!validState || claim.UpdatedAtUtc < claim.ClaimedAtUtc)
        {
            throw new InvalidDataException("The System Restore create claim state is contradictory.");
        }
    }

    private static void ValidateReceipt(SystemRestorePointReceipt receipt)
    {
        if (receipt.ReceiptId == Guid.Empty
            || receipt.PlanId == Guid.Empty
            || receipt.ClaimId is null
            || receipt.ClaimId == Guid.Empty
            || receipt.OwnerId != SystemRestorePointIdentityConstants.OwnerId
            || receipt.Scope != SystemRestorePointIdentityConstants.Scope
            || receipt.ResourceKey != SystemRestorePointIdentityConstants.ResourceKey)
        {
            throw new InvalidDataException("The System Restore receipt identity is invalid.");
        }
        _ = ValidateHash(receipt.PlanHash);
        var validState = receipt.State switch
        {
            SystemRestorePointReceiptState.Executing => receipt.CompletedAtUtc is null
                && receipt.MutationOutcome == SystemRestorePointMutationOutcome.Unknown
                && receipt.MutationState == SystemRestorePointMutationState.Unknown
                && receipt.CreatedIdentity is null
                && !receipt.ReadBackVerified,
            SystemRestorePointReceiptState.Verified => receipt.CompletedAtUtc is not null
                && receipt.MutationOutcome == SystemRestorePointMutationOutcome.Succeeded
                && receipt.MutationState == SystemRestorePointMutationState.Changed
                && receipt.CreatedIdentity is not null
                && receipt.ReadBackVerified,
            SystemRestorePointReceiptState.Failed => receipt.CompletedAtUtc is not null
                && receipt.MutationState == SystemRestorePointMutationState.NotChanged
                && !receipt.ReadBackVerified,
            SystemRestorePointReceiptState.Unknown => receipt.CompletedAtUtc is not null
                && receipt.MutationState != SystemRestorePointMutationState.NotChanged
                && !receipt.ReadBackVerified,
            _ => false
        };
        if (!validState
            || (receipt.CompletedAtUtc is { } completedAt && completedAt < receipt.StartedAtUtc))
        {
            throw new InvalidDataException("The System Restore receipt state is contradictory.");
        }
    }

    private static void EnsureReceiptIdentity(
        SystemRestorePointReceipt existing,
        SystemRestorePointReceipt replacement)
    {
        if (existing.PlanId != replacement.PlanId
            || existing.ClaimId != replacement.ClaimId
            || !FixedEquals(existing.PlanHash, replacement.PlanHash))
        {
            throw new InvalidDataException("The existing receipt does not match the authenticated recovery identity.");
        }
    }

    private static void ValidatePayload<T>(T payload)
    {
        switch (payload)
        {
            case SystemRestorePointCreateClaim claim:
                ValidateClaim(claim);
                break;
            case SystemRestorePointReceipt receipt:
                ValidateReceipt(receipt);
                break;
            case SystemRestorePointCreatePlan plan:
                ValidatePlanIdentity(plan.PlanId, plan.PlanHash, plan.OwnerId, plan.Scope, plan.ResourceKey);
                break;
            case SystemRestoreExecutionPlan plan:
                ValidatePlanIdentity(plan.PlanId, plan.PlanHash, plan.OwnerId, plan.Scope, plan.ResourceKey);
                break;
        }
    }

    private static void ValidatePlanIdentity(Guid planId, string planHash, string ownerId, string scope, string resourceKey)
    {
        if (planId == Guid.Empty
            || ownerId != SystemRestorePointIdentityConstants.OwnerId
            || scope != SystemRestorePointIdentityConstants.Scope
            || resourceKey != SystemRestorePointIdentityConstants.ResourceKey)
        {
            throw new InvalidDataException("The System Restore plan identity is invalid.");
        }
        _ = ValidateHash(planHash);
    }

    private static string ValidateHash(string value)
    {
        try
        {
            return Convert.FromHexString(value).Length == 32
                ? value.ToUpperInvariant()
                : throw new InvalidDataException("A SHA-256 hash is required.");
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("A SHA-256 hash is required.", ex);
        }
    }

    private static bool FixedEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A failed temporary cleanup never changes the authoritative commit result.
        }
    }

    private string CreatePlanPath(Guid id) => Path.Combine(_directory, $"create-plan-{id:D}.json");
    private string ClaimPath(Guid id) => Path.Combine(_directory, $"create-plan-{id:D}.claim.json");
    private string RestorePlanPath(Guid id) => Path.Combine(_directory, $"restore-plan-{id:D}.json");
    private string ReceiptPath(Guid id) => Path.Combine(_directory, $"receipt-{id:D}.json");

    private sealed record Envelope(
        int SchemaVersion,
        string Kind,
        Guid Identity,
        string PayloadSha256,
        string KeyId,
        string EnvelopeHmacSha256,
        byte[] Payload);

    private sealed record LegacyEnvelope(
        int SchemaVersion,
        string Kind,
        Guid Identity,
        string PayloadSha256,
        string EnvelopeHmacSha256,
        byte[] Payload);

    private sealed record PersistedKeyRing(
        int SchemaVersion,
        string ActiveKeyId,
        PersistedKey[] Keys);

    private sealed record PersistedKey(string KeyId, byte[] Key);

    private sealed record AuthenticationKeyRing(
        string ActiveKeyId,
        IReadOnlyDictionary<string, byte[]> Keys);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    private sealed class ResourceLease(string resourceKey, FileStream stream) : ISystemRestorePointResourceLease
    {
        public string ResourceKey { get; } = resourceKey;
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
