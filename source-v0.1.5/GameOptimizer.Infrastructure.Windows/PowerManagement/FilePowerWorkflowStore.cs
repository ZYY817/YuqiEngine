using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.PowerManagement;

namespace GameOptimizer.Infrastructure.Windows.PowerManagement;

public interface IPowerDataProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}

public sealed class DpapiPowerDataProtector : IPowerDataProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GameOptimizer.PowerManagement.v1");

    public byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);
    public byte[] Unprotect(byte[] protectedData) => Transform(protectedData, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI is available only on Windows.");
        using var inputBlob = DataBlob.FromBytes(input);
        using var entropyBlob = DataBlob.FromBytes(Entropy);
        DataBlob outputBlob;
        var success = protect
            ? CryptProtectData(ref inputBlob.Value, null, ref entropyBlob.Value, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob)
            : CryptUnprotectData(ref inputBlob.Value, IntPtr.Zero, ref entropyBlob.Value, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outputBlob);
        if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "DPAPI operation failed.");
        try
        {
            var output = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally { _ = LocalFree(outputBlob.Data); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;

        public static BlobOwner FromBytes(byte[] bytes)
        {
            var pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return new BlobOwner(new DataBlob { Length = bytes.Length, Data = pointer });
        }
    }

    private sealed class BlobOwner : IDisposable
    {
        public BlobOwner(DataBlob value) => Value = value;
        public DataBlob Value;
        public void Dispose()
        {
            if (Value.Data == IntPtr.Zero) return;
            Marshal.Copy(new byte[Value.Length], 0, Value.Data, Value.Length);
            Marshal.FreeHGlobal(Value.Data);
            Value.Data = IntPtr.Zero;
        }
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

public sealed class FilePowerWorkflowStore : IPowerWorkflowStore, IPowerConfirmationSecretProvider
{
    private const int MaximumDocumentBytes = 1024 * 1024;
    private static readonly TimeSpan PersistenceTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _directory;
    private readonly IPowerDataProtector _protector;
    private byte[]? _masterSecret;

    public FilePowerWorkflowStore(string directory, IPowerDataProtector? protector = null)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A power workflow directory is required.", nameof(directory));
        _directory = Path.GetFullPath(directory);
        _protector = protector ?? new DpapiPowerDataProtector();
    }

    public Task SavePreparedAsync(PowerPreparedPlan plan, CancellationToken cancellationToken)
    {
        ValidatePlan(plan);
        return WriteDocumentAsync(PlanPath(plan.PlanId), "prepared-plan", plan.OwnerId, plan.Scope, plan.ResourceKey, plan, cancellationToken);
    }

    public async Task<PowerPreparedPlan?> GetPreparedAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await ReadDocumentAsync<PowerPreparedPlan>(PlanPath(planId), "prepared-plan", cancellationToken).ConfigureAwait(false);
        if (plan is not null) ValidatePlan(plan);
        return plan;
    }

    public Task<bool> TryClaimPlanAsync(Guid planId, string planHash, CancellationToken cancellationToken)
    {
        EnsureSha256(planHash, nameof(planHash));
        return TryClaimAsync(Path.Combine(_directory, $"plan-{planId:D}-{planHash}.claim"), cancellationToken);
    }

    public async Task SaveReceiptAsync(PowerReceipt receipt, CancellationToken cancellationToken)
    {
        ValidateReceipt(receipt);
        var path = ReceiptPath(receipt.ReceiptId);
        await using var updateLock = await AcquireFileLockAsync(path + ".update.lock", PersistenceTimeout, cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
        {
            var existing = await ReadDocumentUnlockedAsync<PowerReceipt>(path, "receipt", cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Existing receipt disappeared during update.");
            ValidateReceipt(existing);
            EnsureReceiptTransition(existing, receipt);
        }
        await WriteDocumentUnlockedAsync(path, "receipt", receipt.OwnerId, receipt.Scope, receipt.ResourceKey, receipt, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PowerReceipt?> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await ReadDocumentAsync<PowerReceipt>(ReceiptPath(receiptId), "receipt", cancellationToken).ConfigureAwait(false);
        if (receipt is not null) ValidateReceipt(receipt);
        return receipt;
    }

    public async Task<IReadOnlyList<PowerReceipt>> ListReceiptsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_directory)) return Array.Empty<PowerReceipt>();
        var receipts = new List<PowerReceipt>();
        foreach (var path in Directory.EnumerateFiles(_directory, "receipt-*.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc).Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var receipt = await ReadDocumentAsync<PowerReceipt>(path, "receipt", cancellationToken).ConfigureAwait(false);
                if (receipt is not null)
                {
                    ValidateReceipt(receipt);
                    receipts.Add(receipt);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* Invalid/tampered receipts are never offered for recovery. */ }
        }
        return receipts.OrderByDescending(item => item.CreatedAtUtc).ToArray();
    }

    public Task<bool> TryClaimRecoveryAsync(Guid receiptId, CancellationToken cancellationToken) =>
        TryClaimAsync(Path.Combine(_directory, $"recovery-{receiptId:D}.claim"), cancellationToken);

    public async Task<IPowerResourceLease> AcquireResourceLeaseAsync(string resourceKey, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ValidateResourceKey(resourceKey);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceKey)));
        var stream = await AcquireFileLockAsync(Path.Combine(_directory, $"resource-{name}.lease"), timeout, cancellationToken).ConfigureAwait(false);
        return new FileResourceLease(resourceKey, stream);
    }

    private async Task WriteDocumentAsync<T>(string path, string documentType, string owner, string scope, string resource, T payload, CancellationToken cancellationToken)
    {
        await using var updateLock = await AcquireFileLockAsync(path + ".update.lock", PersistenceTimeout, cancellationToken).ConfigureAwait(false);
        await WriteDocumentUnlockedAsync(path, documentType, owner, scope, resource, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteDocumentUnlockedAsync<T>(string path, string documentType, string owner, string scope, string resource, T payload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var secret = await GetMasterSecretAsync(cancellationToken).ConfigureAwait(false);
        var payloadElement = JsonSerializer.SerializeToElement(payload, JsonOptions);
        var mac = ComputeMac(secret, documentType, owner, scope, resource, payloadElement);
        var envelope = new StoredEnvelope(PowerManagementIdentity.SchemaVersion, documentType, owner, scope, resource, payloadElement, mac);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (bytes.Length > MaximumDocumentBytes) throw new InvalidDataException("Power workflow document exceeds the size limit.");
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await WriteThroughAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
            var readBack = await File.ReadAllBytesAsync(temp, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), SHA256.HashData(readBack)))
                throw new IOException("Power workflow temporary-file read-back did not match.");
            _ = ParseEnvelope<T>(readBack, documentType, secret);
            if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private async Task<T?> ReadDocumentAsync<T>(string path, string documentType, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return default;
        return await ReadDocumentUnlockedAsync<T>(path, documentType, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> ReadDocumentUnlockedAsync<T>(string path, string documentType, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return default;
        if (info.Length is <= 0 or > MaximumDocumentBytes) throw new InvalidDataException("Power workflow document size is invalid.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var secret = await GetMasterSecretAsync(cancellationToken).ConfigureAwait(false);
        return ParseEnvelope<T>(bytes, documentType, secret);
    }

    private static T ParseEnvelope<T>(byte[] bytes, string documentType, byte[] masterSecret)
    {
        StoredEnvelope envelope;
        try { envelope = JsonSerializer.Deserialize<StoredEnvelope>(bytes, JsonOptions) ?? throw new InvalidDataException("Power workflow envelope is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Power workflow envelope schema is invalid.", ex); }
        if (envelope.SchemaVersion != PowerManagementIdentity.SchemaVersion
            || envelope.DocumentType != documentType
            || envelope.OwnerId != PowerManagementIdentity.OwnerId
            || envelope.Scope != PowerManagementIdentity.Scope)
            throw new InvalidDataException("Power workflow envelope identity, type, scope, or schema is invalid.");
        ValidateResourceKey(envelope.ResourceKey);
        var expected = ComputeMac(masterSecret, envelope.DocumentType, envelope.OwnerId, envelope.Scope, envelope.ResourceKey, envelope.Payload);
        if (!FixedEquals(envelope.IntegrityMac, expected))
            throw new InvalidDataException("Power workflow envelope HMAC validation failed.");
        try { return envelope.Payload.Deserialize<T>(JsonOptions) ?? throw new InvalidDataException("Power workflow payload is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Power workflow payload schema is invalid.", ex); }
    }

    private async Task<byte[]> GetMasterSecretAsync(CancellationToken cancellationToken)
    {
        if (_masterSecret is not null) return _masterSecret;
        Directory.CreateDirectory(_directory);
        var secretPath = Path.Combine(_directory, ".power-secret.dpapi");
        await using var secretLock = await AcquireFileLockAsync(secretPath + ".lock", PersistenceTimeout, cancellationToken).ConfigureAwait(false);
        if (_masterSecret is not null) return _masterSecret;
        byte[] secret;
        if (File.Exists(secretPath))
        {
            var protectedBytes = await File.ReadAllBytesAsync(secretPath, cancellationToken).ConfigureAwait(false);
            if (protectedBytes.Length is <= 0 or > 16_384) throw new InvalidDataException("Protected Power HMAC secret size is invalid.");
            secret = _protector.Unprotect(protectedBytes);
            if (secret.Length != 32) throw new InvalidDataException("Protected Power HMAC secret has an invalid length.");
        }
        else
        {
            secret = RandomNumberGenerator.GetBytes(32);
            var protectedBytes = _protector.Protect(secret);
            await WriteThroughAsync(secretPath, protectedBytes, cancellationToken, FileMode.CreateNew).ConfigureAwait(false);
            var protectedReadBack = await File.ReadAllBytesAsync(secretPath, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(protectedBytes), SHA256.HashData(protectedReadBack))
                || !CryptographicOperations.FixedTimeEquals(secret, _protector.Unprotect(protectedReadBack)))
                throw new IOException("Protected Power HMAC secret read-back validation failed.");
        }
        _masterSecret = secret;
        return secret;
    }

    public byte[] GetConfirmationSecret()
    {
        using var cts = new CancellationTokenSource(PersistenceTimeout);
        var master = GetMasterSecretAsync(cts.Token).GetAwaiter().GetResult();
        return DeriveKey(master, "confirmation");
    }

    private async Task<bool> TryClaimAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        try
        {
            await WriteThroughAsync(path, Encoding.ASCII.GetBytes("claimed-v1"), cancellationToken, FileMode.CreateNew).ConfigureAwait(false);
            return true;
        }
        catch (IOException) when (File.Exists(path)) { return false; }
    }

    private static async Task<FileStream> AcquireFileLockAsync(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new TimeoutException($"Timed out acquiring Power resource lock '{Path.GetFileName(path)}'.", ex);
            }
        }
    }

    private static async Task WriteThroughAsync(string path, byte[] bytes, CancellationToken cancellationToken, FileMode mode = FileMode.CreateNew)
    {
        await using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.None, 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static string ComputeMac(byte[] masterSecret, string type, string owner, string scope, string resource, JsonElement payload)
    {
        var key = DeriveKey(masterSecret, "envelope:" + type);
        try
        {
            var payloadHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));
            var canonical = Encoding.UTF8.GetBytes(string.Join('\n', PowerManagementIdentity.SchemaVersion, type, owner, scope, resource, payloadHash));
            return Convert.ToHexString(HMACSHA256.HashData(key, canonical));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] DeriveKey(byte[] masterSecret, string domain) =>
        HMACSHA256.HashData(masterSecret, Encoding.UTF8.GetBytes("GameOptimizer.PowerManagement/" + domain));

    private static void ValidatePlan(PowerPreparedPlan plan)
    {
        if (plan.OwnerId != PowerManagementIdentity.OwnerId || plan.Scope != PowerManagementIdentity.Scope
            || plan.ResourceKey != PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource)
            throw new InvalidDataException("Prepared plan owner, scope, or resource is invalid.");
        EnsureSha256(plan.PlanHash, nameof(plan.PlanHash));
        ValidateValues(plan.OriginalDisplayTimeout);
        ValidateValues(plan.TargetDisplayTimeout);
    }

    private static void ValidateReceipt(PowerReceipt receipt)
    {
        if (receipt.OwnerId != PowerManagementIdentity.OwnerId || receipt.Scope != PowerManagementIdentity.Scope
            || receipt.ResourceKey != PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource)
            throw new InvalidDataException("Receipt owner, scope, or resource is invalid.");
        if (receipt.ReceiptId == Guid.Empty || receipt.PlanId == Guid.Empty || receipt.TargetSchemeId == Guid.Empty || receipt.OriginalActiveSchemeId == Guid.Empty)
            throw new InvalidDataException("Receipt identifiers are invalid.");
        EnsureSha256(receipt.PlanHash, nameof(receipt.PlanHash));
        ValidateValues(receipt.OriginalDisplayTimeout);
        ValidateValues(receipt.AppliedDisplayTimeout);
        if (string.IsNullOrWhiteSpace(receipt.BaselineId) || receipt.BaselineId.Length > 256)
            throw new InvalidDataException("Receipt baseline identifier is invalid.");
    }

    private static void ValidateValues(PowerSettingValues values)
    {
        if (values.AcSeconds > PowerManagementIdentity.MaximumDisplayTimeoutSeconds
            || values.DcSeconds > PowerManagementIdentity.MaximumDisplayTimeoutSeconds)
            throw new InvalidDataException("Power display timeout value is outside the supported range.");
    }

    private static void EnsureReceiptTransition(PowerReceipt current, PowerReceipt next)
    {
        if (current.ReceiptId != next.ReceiptId || current.PlanId != next.PlanId || current.PlanHash != next.PlanHash
            || current.ResourceKey != next.ResourceKey || current.OriginalDisplayTimeout != next.OriginalDisplayTimeout
            || current.AppliedDisplayTimeout != next.AppliedDisplayTimeout || current.BaselineId != next.BaselineId)
            throw new InvalidDataException("Immutable receipt fields changed during update.");
        var allowed = current.State == PowerReceiptState.Executing && next.State is PowerReceiptState.Applied or PowerReceiptState.ApplyFailed
            || current.State == PowerReceiptState.Applied && next.State == PowerReceiptState.Restoring
            || current.State == PowerReceiptState.Restoring && next.State is PowerReceiptState.Restored or PowerReceiptState.RestoreFailed;
        if (!allowed) throw new InvalidDataException($"Receipt transition {current.State} -> {next.State} is invalid.");
    }

    private static void ValidateResourceKey(string resourceKey)
    {
        if (!string.Equals(
                resourceKey,
                PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource,
                StringComparison.Ordinal))
            throw new InvalidDataException("Power resource key is invalid.");
    }

    private static void EnsureSha256(string value, string name)
    {
        try { if (Convert.FromHexString(value).Length != 32) throw new InvalidDataException($"{name} is not SHA-256."); }
        catch (FormatException ex) { throw new InvalidDataException($"{name} is not hexadecimal SHA-256.", ex); }
    }

    private static bool FixedEquals(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }

    private string PlanPath(Guid id) => Path.Combine(_directory, $"plan-{id:D}.json");
    private string ReceiptPath(Guid id) => Path.Combine(_directory, $"receipt-{id:D}.json");

    private sealed record StoredEnvelope(
        int SchemaVersion,
        string DocumentType,
        string OwnerId,
        string Scope,
        string ResourceKey,
        JsonElement Payload,
        string IntegrityMac);

    private sealed class FileResourceLease : IPowerResourceLease
    {
        private FileStream? _stream;
        public FileResourceLease(string resourceKey, FileStream stream) { ResourceKey = resourceKey; _stream = stream; }
        public string ResourceKey { get; }
        public async ValueTask DisposeAsync()
        {
            var stream = Interlocked.Exchange(ref _stream, null);
            if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
