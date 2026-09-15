using System.Security.Cryptography;
using System.Text.Json;
using GameOptimizer.Core.SystemRestore;

namespace GameOptimizer.Infrastructure.Windows.SystemRestore;

public sealed partial class FileSystemRestorePointWorkflowStore
{
    public Task RotateAuthenticationKeyAsync(byte[]? nextKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = nextKey?.ToArray() ?? RandomNumberGenerator.GetBytes(AuthenticationKeyBytes);
        if (key.Length != AuthenticationKeyBytes)
        {
            throw new ArgumentException($"The rotated System Restore authentication key must be {AuthenticationKeyBytes} bytes.", nameof(nextKey));
        }

        Directory.CreateDirectory(_directory);
        using var crossProcessLock = new FileStream(
            Path.Combine(_directory, "authentication-keyring.rotation.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        lock (_keyGate)
        {
            var current = GetAuthenticationKeyRing();
            var nextId = ComputeKeyId(key);
            if (current.Keys.ContainsKey(nextId))
            {
                throw new InvalidOperationException("The rotated System Restore authentication key is already present in the key ring.");
            }

            var keys = current.Keys.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
            keys.Add(nextId, key);
            var rotated = new AuthenticationKeyRing(nextId, keys);
            if (!_usesSuppliedKeys)
            {
                PersistProtectedKeyRing(rotated);
            }
            _cachedKeyRing = rotated;
        }
        return Task.CompletedTask;
    }

    public async Task<SystemRestorePointStoreMigrationDisposition> MigrateLegacyDocumentAsync(
        string kind,
        Guid identity,
        CancellationToken cancellationToken)
    {
        var path = kind switch
        {
            "create-plan" => CreatePlanPath(identity),
            "create-claim" => ClaimPath(identity),
            "restore-plan" => RestorePlanPath(identity),
            "receipt" => ReceiptPath(identity),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "The System Restore document kind is not migratable.")
        };
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The legacy System Restore workflow document does not exist.", path);
        }

        await using var migrationLock = new FileStream(
            path + ".migration.lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        var document = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        using (var parsed = JsonDocument.Parse(document))
        {
            if (!parsed.RootElement.TryGetProperty("schemaVersion", out var schemaElement))
            {
                throw new InvalidDataException("The legacy System Restore workflow schema is missing.");
            }
            if (schemaElement.GetInt32() == SchemaVersion)
            {
                await AuthenticateCurrentDocumentAsync(kind, identity, cancellationToken).ConfigureAwait(false);
                return SystemRestorePointStoreMigrationDisposition.AlreadyCurrent;
            }
            if (schemaElement.GetInt32() != LegacySchemaVersion)
            {
                throw new InvalidDataException("The System Restore workflow schema is unknown and cannot be migrated.");
            }
        }

        LegacyEnvelope legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<LegacyEnvelope>(document, JsonOptions)
                ?? throw new InvalidDataException("The legacy System Restore workflow envelope is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The legacy System Restore workflow envelope is malformed.", ex);
        }
        if (legacy.SchemaVersion != LegacySchemaVersion
            || legacy.Identity != identity
            || !string.Equals(legacy.Kind, kind, StringComparison.Ordinal)
            || legacy.Payload.Length <= 0
            || legacy.Payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The legacy System Restore workflow envelope identity is invalid.");
        }

        var payloadHash = Convert.ToHexString(SHA256.HashData(legacy.Payload));
        var keyRing = GetAuthenticationKeyRing();
        var authenticated = FixedEquals(payloadHash, legacy.PayloadSha256)
            && keyRing.Keys.Values.Any(key => FixedEquals(
                ComputeMac(LegacySchemaVersion, kind, identity, payloadHash, key),
                legacy.EnvelopeHmacSha256));
        if (!authenticated)
        {
            throw new InvalidDataException("The legacy System Restore workflow document authentication failed; migration is blocked.");
        }

        object payload;
        try
        {
            payload = (kind switch
            {
                "create-plan" => (object?)JsonSerializer.Deserialize<SystemRestorePointCreatePlan>(legacy.Payload, JsonOptions),
                "create-claim" => JsonSerializer.Deserialize<SystemRestorePointCreateClaim>(legacy.Payload, JsonOptions),
                "restore-plan" => JsonSerializer.Deserialize<SystemRestoreExecutionPlan>(legacy.Payload, JsonOptions),
                "receipt" => JsonSerializer.Deserialize<SystemRestorePointReceipt>(legacy.Payload, JsonOptions),
                _ => null
            }) ?? throw new InvalidDataException("The legacy System Restore workflow payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The legacy System Restore workflow payload is incompatible.", ex);
        }
        ValidatePayload(payload);

        var backupPath = path + $".schema2-{Convert.ToHexString(SHA256.HashData(document))}.bak";
        if (!File.Exists(backupPath))
        {
            File.Copy(path, backupPath, overwrite: false);
        }
        var backup = await File.ReadAllBytesAsync(backupPath, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(document, backup))
        {
            throw new InvalidDataException("The legacy System Restore workflow backup failed read-back verification.");
        }

        switch (payload)
        {
            case SystemRestorePointCreatePlan value:
                await WriteAsync(path, kind, identity, value, overwrite: true, cancellationToken).ConfigureAwait(false);
                break;
            case SystemRestorePointCreateClaim value:
                await WriteAsync(path, kind, identity, value, overwrite: true, cancellationToken).ConfigureAwait(false);
                break;
            case SystemRestoreExecutionPlan value:
                await WriteAsync(path, kind, identity, value, overwrite: true, cancellationToken).ConfigureAwait(false);
                break;
            case SystemRestorePointReceipt value:
                await WriteAsync(path, kind, identity, value, overwrite: true, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException("The legacy System Restore workflow payload type is unsupported.");
        }
        return SystemRestorePointStoreMigrationDisposition.Migrated;
    }

    private Task AuthenticateCurrentDocumentAsync(string kind, Guid identity, CancellationToken cancellationToken) => kind switch
    {
        "create-plan" => RequireReadAsync<SystemRestorePointCreatePlan>(CreatePlanPath(identity), kind, identity, cancellationToken),
        "create-claim" => RequireReadAsync<SystemRestorePointCreateClaim>(ClaimPath(identity), kind, identity, cancellationToken),
        "restore-plan" => RequireReadAsync<SystemRestoreExecutionPlan>(RestorePlanPath(identity), kind, identity, cancellationToken),
        "receipt" => RequireReadAsync<SystemRestorePointReceipt>(ReceiptPath(identity), kind, identity, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private async Task RequireReadAsync<T>(string path, string kind, Guid identity, CancellationToken cancellationToken)
    {
        _ = await ReadAsync<T>(path, kind, identity, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The System Restore workflow document disappeared during migration.");
    }

    private AuthenticationKeyRing LoadOrCreateProtectedAuthenticationKeyRing()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The durable System Restore authentication key requires Windows DPAPI.");
        }

        Directory.CreateDirectory(_directory);
        var ringPath = Path.Combine(_directory, KeyRingFileName);
        if (File.Exists(ringPath))
        {
            return DeserializeProtectedKeyRing(File.ReadAllBytes(ringPath));
        }

        var legacyPath = Path.Combine(_directory, LegacyKeyFileName);
        var key = File.Exists(legacyPath)
            ? UnprotectKey(File.ReadAllBytes(legacyPath))
            : RandomNumberGenerator.GetBytes(AuthenticationKeyBytes);
        var ring = CreateKeyRing(key, Array.Empty<byte[]>());
        try
        {
            PersistProtectedKeyRing(ring, FileMode.CreateNew);
        }
        catch (IOException) when (File.Exists(ringPath))
        {
            return DeserializeProtectedKeyRing(File.ReadAllBytes(ringPath));
        }

        var readBack = DeserializeProtectedKeyRing(File.ReadAllBytes(ringPath));
        if (!string.Equals(ring.ActiveKeyId, readBack.ActiveKeyId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The durable System Restore authentication key ring failed read-back verification.");
        }
        return ring;
    }

    private void PersistProtectedKeyRing(AuthenticationKeyRing ring, FileMode mode = FileMode.Create)
    {
        var serialized = JsonSerializer.SerializeToUtf8Bytes(
            new PersistedKeyRing(1, ring.ActiveKeyId, ring.Keys.Select(pair => new PersistedKey(pair.Key, pair.Value)).ToArray()),
            JsonOptions);
        var protectedBytes = ProtectKeyRing(serialized);
        var path = Path.Combine(_directory, KeyRingFileName);
        if (mode == FileMode.CreateNew)
        {
            WriteDurableFile(path, protectedBytes, mode);
            return;
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteDurableFile(temporary, protectedBytes, FileMode.CreateNew);
            _ = DeserializeProtectedKeyRing(File.ReadAllBytes(temporary));
            File.Move(temporary, path, overwrite: true);
            _ = DeserializeProtectedKeyRing(File.ReadAllBytes(path));
        }
        finally
        {
            TryDeleteTemporary(temporary);
        }
    }

    private static AuthenticationKeyRing DeserializeProtectedKeyRing(byte[] protectedBytes)
    {
        try
        {
            var serialized = UnprotectKeyRing(protectedBytes);
            var persisted = JsonSerializer.Deserialize<PersistedKeyRing>(serialized, JsonOptions)
                ?? throw new InvalidDataException("The protected System Restore key ring is empty.");
            if (persisted.SchemaVersion != 1 || persisted.Keys.Length == 0)
            {
                throw new InvalidDataException("The protected System Restore key ring schema is unsupported.");
            }
            var keys = persisted.Keys.ToDictionary(
                item => item.KeyId,
                item => item.Key.Length == AuthenticationKeyBytes && ComputeKeyId(item.Key) == item.KeyId
                    ? item.Key.ToArray()
                    : throw new InvalidDataException("The protected System Restore key ring contains an invalid key."),
                StringComparer.Ordinal);
            return keys.ContainsKey(persisted.ActiveKeyId)
                ? new AuthenticationKeyRing(persisted.ActiveKeyId, keys)
                : throw new InvalidDataException("The protected System Restore key ring has no active key.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The protected System Restore key ring is malformed.", ex);
        }
    }

    private static AuthenticationKeyRing CreateKeyRing(byte[] active, IReadOnlyList<byte[]> previous)
    {
        var activeId = ComputeKeyId(active);
        var keys = previous.Append(active)
            .ToDictionary(ComputeKeyId, key => key.ToArray(), StringComparer.Ordinal);
        return new AuthenticationKeyRing(activeId, keys);
    }

    private static string ComputeKeyId(byte[] key) => Convert.ToHexString(SHA256.HashData(key))[..32];
}
