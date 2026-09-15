using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.Services.Startup;
using GameOptimizer.Infrastructure.Windows.PowerManagement;
using Microsoft.Win32;

namespace GameOptimizer.Infrastructure.Windows.Startup;

public static class ManagedAutostartApplicationIdentity
{
    public const string ValueName = "YuqiEngine";
    public const string ResourceId = "HKCU:SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run:YuqiEngine";
    public const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string AutostartArgument = "--autostart";
}

public sealed class WindowsManagedAutostartPlatform : IManagedAutostartPlatform
{
    private readonly Func<string> _executablePath;

    public WindowsManagedAutostartPlatform(Func<string>? executablePath = null) =>
        _executablePath = executablePath ?? (() => Environment.ProcessPath ?? string.Empty);

    public Task<object> ReadAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSupported(identity);
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ManagedAutostartApplicationIdentity.RegistryPath, writable: false);
        var raw = key?.GetValue(ManagedAutostartApplicationIdentity.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
        var kind = raw is null ? "None" : key!.GetValueKind(ManagedAutostartApplicationIdentity.ValueName).ToString();
        var (path, arguments) = ParseCommand(raw ?? string.Empty);
        return Task.FromResult<object>(CreateSnapshot(raw is not null, raw is not null, path, arguments, kind));
    }

    public Task ApplyAsync(ManagedAutostartIdentity identity, ManagedAutostartAction action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSupported(identity);
        if (action == ManagedAutostartAction.Delete)
            throw new InvalidOperationException("Deletion is not permitted; disable retains recovery semantics.");
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ManagedAutostartApplicationIdentity.RegistryPath, writable: true)
            ?? throw new InvalidOperationException("The application Run key is unavailable.");
        if (action == ManagedAutostartAction.Enable)
        {
            var path = CanonicalExecutable();
            key.SetValue(
                ManagedAutostartApplicationIdentity.ValueName,
                BuildAutostartCommand(path),
                RegistryValueKind.String);
        }
        else key.DeleteValue(ManagedAutostartApplicationIdentity.ValueName, throwOnMissingValue: false);
        return Task.CompletedTask;
    }

    public Task RestoreAsync(ManagedAutostartIdentity identity, object snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSupported(identity);
        if (snapshot is not StartupEntrySnapshot original) throw new InvalidDataException("Startup recovery snapshot type is invalid.");
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ManagedAutostartApplicationIdentity.RegistryPath, writable: true)
            ?? throw new InvalidOperationException("The application Run key is unavailable.");
        if (!original.Exists) key.DeleteValue(ManagedAutostartApplicationIdentity.ValueName, false);
        else
        {
            if (!string.Equals(Path.GetFullPath(original.CommandPath), CanonicalExecutable(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recovery command identity does not match this application executable.");
            key.SetValue(ManagedAutostartApplicationIdentity.ValueName,
                string.IsNullOrWhiteSpace(original.Arguments) ? $"\"{original.CommandPath}\"" : $"\"{original.CommandPath}\" {original.Arguments}",
                RegistryValueKind.String);
        }
        return Task.CompletedTask;
    }

    public Task<bool> HasRequiredPermissionAsync(ManagedAutostartIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupported(identity)) return Task.FromResult(false);
        try { using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ManagedAutostartApplicationIdentity.RegistryPath, writable: true); return Task.FromResult(key is not null); }
        catch (UnauthorizedAccessException) { return Task.FromResult(false); }
        catch (System.Security.SecurityException) { return Task.FromResult(false); }
    }

    public Task<bool> IsTrustedTargetAsync(ManagedAutostartIdentity identity, object snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupported(identity) || snapshot is not StartupEntrySnapshot entry) return Task.FromResult(false);
        if (!entry.Exists) return Task.FromResult(true);
        try { return Task.FromResult(string.Equals(Path.GetFullPath(entry.CommandPath), CanonicalExecutable(), StringComparison.OrdinalIgnoreCase)); }
        catch { return Task.FromResult(false); }
    }

    private string CanonicalExecutable()
    {
        var path = _executablePath();
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Application executable path is unavailable.");
        return Path.GetFullPath(path);
    }

    internal static string BuildAutostartCommand(string canonicalExecutable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalExecutable);
        if (!Path.IsPathFullyQualified(canonicalExecutable))
            throw new ArgumentException("Autostart executable path must be fully qualified.", nameof(canonicalExecutable));
        return $"\"{Path.GetFullPath(canonicalExecutable)}\" {ManagedAutostartApplicationIdentity.AutostartArgument}";
    }

    private static bool IsSupported(ManagedAutostartIdentity identity) => identity.Kind == ManagedAutostartKind.RegistryRun
        && identity.Scope == "current-user" && identity.ResourceId == ManagedAutostartApplicationIdentity.ResourceId;
    private static void EnsureSupported(ManagedAutostartIdentity identity)
    {
        if (!IsSupported(identity)) throw new InvalidOperationException("Only this application's HKCU Run entry is allowlisted.");
    }
    private static StartupEntrySnapshot CreateSnapshot(bool exists, bool enabled, string path, string args, string kind)
    {
        var canonical = $"{exists}|{enabled}|{path}|{args}|{kind}|1";
        return new(exists, enabled, path, args, kind, "1", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }
    private static (string Path, string Arguments) ParseCommand(string value)
    {
        value = value.Trim();
        if (value.StartsWith('"')) { var end = value.IndexOf('"', 1); if (end > 0) return (value[1..end], value[(end + 1)..].Trim()); }
        var split = value.IndexOf(' '); return split > 0 ? (value[..split], value[(split + 1)..]) : (value, string.Empty);
    }
}

public sealed class FileManagedAutostartStore : IManagedAutostartStore, IManagedAutostartConfirmationSecretProvider
{
    private const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly string _root;
    private readonly IPowerDataProtector _protector;
    private byte[]? _secret;

    public FileManagedAutostartStore(string root, IPowerDataProtector protector)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A durable autostart store root is required.", nameof(root));
        _root = Path.GetFullPath(root);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public Task SavePlanAsync(ManagedAutostartPlan plan, object snapshot, CancellationToken ct) =>
        WriteAsync(PathFor("plan", plan.PlanId), "plan", new StoredPlan(plan, RequireStartup(snapshot)), ct);
    public async Task<(ManagedAutostartPlan Plan, object Snapshot)?> LoadPlanAsync(Guid id, CancellationToken ct)
    {
        var value = await ReadAsync<StoredPlan>(PathFor("plan", id), "plan", ct).ConfigureAwait(false);
        return value is null ? null : (value.Plan, value.Snapshot);
    }
    public Task<bool> TryClaimPlanAsync(Guid id, string hash, CancellationToken ct) => ClaimAsync($"plan-{id:D}-{hash}.claim", ct);
    public Task SaveReceiptAsync(ManagedAutostartReceipt receipt, CancellationToken ct) =>
        WriteAsync(PathFor("receipt", receipt.ReceiptId), "receipt", new StoredReceipt(
            receipt.ReceiptId, receipt.Plan, RequireStartup(receipt.OriginalSnapshot), receipt.AppliedFingerprint,
            receipt.Verified, receipt.Restored, receipt.Failure), ct, replace: true);
    public Task<ManagedAutostartReceipt?> LoadReceiptAsync(Guid id, CancellationToken ct) => ReadReceiptAsync(id, ct);
    public Task<bool> TryClaimRestoreAsync(Guid id, CancellationToken ct) => ClaimAsync($"restore-{id:D}.claim", ct);
    public async Task<IAsyncDisposable> AcquireLeaseAsync(string resourceId, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceId)));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new Lease(new FileStream(Path.Combine(_root, $"resource-{name}.lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough)); }
            catch (IOException) { await Task.Delay(25, ct).ConfigureAwait(false); }
        }
    }

    private async Task<ManagedAutostartReceipt?> ReadReceiptAsync(Guid id, CancellationToken ct)
    {
        var stored = await ReadAsync<StoredReceipt>(PathFor("receipt", id), "receipt", ct).ConfigureAwait(false);
        return stored is null ? null : new ManagedAutostartReceipt(stored.ReceiptId, stored.Plan, stored.OriginalSnapshot,
            stored.AppliedFingerprint, stored.Verified, stored.Restored, stored.Failure);
    }
    private async Task WriteAsync<T>(string path, string type, T payload, CancellationToken ct, bool replace = false)
    {
        Directory.CreateDirectory(_root);
        var element = JsonSerializer.SerializeToElement(payload, Json);
        var mac = Convert.ToHexString(HMACSHA256.HashData(await SecretAsync(ct), JsonSerializer.SerializeToUtf8Bytes(element, Json)));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, type, element, mac), Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Autostart document is too large.");
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await WriteThroughAsync(temp, bytes, ct, FileMode.CreateNew).ConfigureAwait(false);
            _ = Parse<T>(await File.ReadAllBytesAsync(temp, ct), type, await SecretAsync(ct));
            if (replace && File.Exists(path)) File.Replace(temp, path, null, true); else File.Move(temp, path, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private async Task<T?> ReadAsync<T>(string path, string type, CancellationToken ct)
    {
        if (!File.Exists(path)) return default;
        var info = new FileInfo(path); if (info.Length is <= 0 or > MaxBytes) throw new InvalidDataException("Autostart document size is invalid.");
        return Parse<T>(await File.ReadAllBytesAsync(path, ct), type, await SecretAsync(ct));
    }
    private static T Parse<T>(byte[] bytes, string type, byte[] secret)
    {
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(bytes, Json) ?? throw new InvalidDataException("Envelope is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Envelope schema is invalid.", ex); }
        if (envelope.Schema != 1 || envelope.Type != type) throw new InvalidDataException("Envelope identity is invalid.");
        var expected = Convert.ToHexString(HMACSHA256.HashData(secret, JsonSerializer.SerializeToUtf8Bytes(envelope.Payload, Json)));
        if (!Fixed(envelope.Mac, expected)) throw new InvalidDataException("Envelope HMAC is invalid.");
        return envelope.Payload.Deserialize<T>(Json) ?? throw new InvalidDataException("Payload is empty.");
    }
    private async Task<byte[]> SecretAsync(CancellationToken ct)
    {
        if (_secret is not null) return _secret;
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, ".autostart-secret.dpapi");
        if (File.Exists(path)) _secret = _protector.Unprotect(await File.ReadAllBytesAsync(path, ct));
        else
        {
            _secret = RandomNumberGenerator.GetBytes(32);
            try { await WriteThroughAsync(path, _protector.Protect(_secret), ct, FileMode.CreateNew); }
            catch (IOException) when (File.Exists(path))
            {
                _secret = _protector.Unprotect(await File.ReadAllBytesAsync(path, ct));
            }
        }
        if (_secret.Length != 32) throw new InvalidDataException("Autostart HMAC secret is invalid.");
        return _secret;
    }
    private async Task<bool> ClaimAsync(string name, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        try { await WriteThroughAsync(Path.Combine(_root, name), Encoding.ASCII.GetBytes("claimed-v1"), ct, FileMode.CreateNew); return true; }
        catch (IOException) when (File.Exists(Path.Combine(_root, name))) { return false; }
    }
    private static async Task WriteThroughAsync(string path, byte[] bytes, CancellationToken ct, FileMode mode)
    {
        await using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct); stream.Flush(true);
    }
    private static StartupEntrySnapshot RequireStartup(object value) => value as StartupEntrySnapshot ?? throw new InvalidDataException("Only application StartupEntry snapshots are currently durable.");
    public byte[] GetConfirmationSecret()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return HMACSHA256.HashData(SecretAsync(cts.Token).GetAwaiter().GetResult(), Encoding.UTF8.GetBytes("GameOptimizer.ManagedAutostart/confirmation"));
    }
    private static bool Fixed(string left, string right) { try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); } catch { return false; } }
    private string PathFor(string type, Guid id) => Path.Combine(_root, $"{type}-{id:D}.json");
    private sealed record Envelope(int Schema, string Type, JsonElement Payload, string Mac);
    private sealed record StoredPlan(ManagedAutostartPlan Plan, StartupEntrySnapshot Snapshot);
    private sealed record StoredReceipt(Guid ReceiptId, ManagedAutostartPlan Plan, StartupEntrySnapshot OriginalSnapshot,
        string AppliedFingerprint, bool Verified, bool Restored, string? Failure);
    private sealed class Lease(FileStream stream) : IAsyncDisposable { public ValueTask DisposeAsync() => stream.DisposeAsync(); }
}
