using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Constants;

namespace GameOptimizer.Core.Services.Changes;

public sealed class FileRecoveryClaimStore : IRecoveryClaimStore, IRecoveryClaimStateReader
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaximumStateBytes = 16 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string _rootDirectory;
    private readonly string _rootPrefix;

    public FileRecoveryClaimStore()
        : this(Path.Combine(AppPaths.AppDataRoot, "Changes", "recovery-claims"))
    {
    }

    public FileRecoveryClaimStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public async Task RegisterAvailableAsync(
        Guid recoveryId,
        string integrityHash,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(recoveryId, integrityHash);
        using var stateLease = await AcquireStateLeaseAsync(recoveryId, cancellationToken).ConfigureAwait(false);
        var availablePath = PathFor(recoveryId, RecoveryState.Available);
        Directory.CreateDirectory(_rootDirectory);
        try
        {
            await WriteNewAsync(
                availablePath,
                new StateDocument(CurrentSchemaVersion, RecoveryState.Available, integrityHash, null, StateHash(RecoveryState.Available, integrityHash, null)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(availablePath))
        {
            var existing = await ReadAsync(availablePath, RecoveryState.Available, cancellationToken).ConfigureAwait(false);
            if (!FixedTimeHashEquals(existing.IntegrityHash, integrityHash))
            {
                throw new InvalidDataException("The recovery availability state has a different integrity hash.");
            }
        }
    }

    public async Task<RecoveryClaimResult> TryBeginRestoreAsync(
        Guid recoveryId,
        string integrityHash,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(recoveryId, integrityHash);
        using var stateLease = await AcquireStateLeaseAsync(recoveryId, cancellationToken).ConfigureAwait(false);
        var restoredPath = PathFor(recoveryId, RecoveryState.Restored);
        if (File.Exists(restoredPath))
        {
            var restored = await ReadAsync(restoredPath, RecoveryState.Restored, cancellationToken).ConfigureAwait(false);
            return new RecoveryClaimResult(
                FixedTimeHashEquals(restored.IntegrityHash, integrityHash)
                    ? RecoveryClaimStatus.AlreadyRestored
                    : RecoveryClaimStatus.IntegrityMismatch,
                null);
        }

        var availablePath = PathFor(recoveryId, RecoveryState.Available);
        if (!File.Exists(availablePath))
        {
            return new RecoveryClaimResult(RecoveryClaimStatus.Missing, null);
        }

        var available = await ReadAsync(availablePath, RecoveryState.Available, cancellationToken).ConfigureAwait(false);
        if (!FixedTimeHashEquals(available.IntegrityHash, integrityHash))
        {
            return new RecoveryClaimResult(RecoveryClaimStatus.IntegrityMismatch, null);
        }

        var restoringPath = PathFor(recoveryId, RecoveryState.Restoring);
        var claimId = Guid.NewGuid();
        try
        {
            await WriteNewAsync(
                restoringPath,
                new StateDocument(CurrentSchemaVersion, RecoveryState.Restoring, integrityHash, claimId, StateHash(RecoveryState.Restoring, integrityHash, claimId)),
                cancellationToken).ConfigureAwait(false);
            return new RecoveryClaimResult(RecoveryClaimStatus.Claimed, claimId);
        }
        catch (IOException) when (File.Exists(restoringPath))
        {
            var existing = await ReadAsync(restoringPath, RecoveryState.Restoring, cancellationToken).ConfigureAwait(false);
            return new RecoveryClaimResult(
                FixedTimeHashEquals(existing.IntegrityHash, integrityHash)
                    ? RecoveryClaimStatus.AlreadyRestoring
                    : RecoveryClaimStatus.IntegrityMismatch,
                null);
        }
    }

    public async Task<RecoveryClaimStateSnapshot> ReadStateAsync(
        Guid recoveryId,
        string integrityHash,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(recoveryId, integrityHash);
        using var stateLease = await AcquireStateLeaseAsync(recoveryId, cancellationToken).ConfigureAwait(false);

        var restored = await TryReadStateAsync(
            recoveryId,
            integrityHash,
            RecoveryState.Restored,
            cancellationToken).ConfigureAwait(false);
        if (restored is not null)
        {
            return restored;
        }

        var restoring = await TryReadStateAsync(
            recoveryId,
            integrityHash,
            RecoveryState.Restoring,
            cancellationToken).ConfigureAwait(false);
        if (restoring is not null)
        {
            return restoring;
        }

        var available = await TryReadStateAsync(
            recoveryId,
            integrityHash,
            RecoveryState.Available,
            cancellationToken).ConfigureAwait(false);
        return available ?? new RecoveryClaimStateSnapshot(RecoveryClaimReadStatus.Missing, null);
    }

    public async Task MarkRestoredAsync(
        Guid recoveryId,
        string integrityHash,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(recoveryId, integrityHash);
        EnsureNonEmpty(claimId, nameof(claimId));
        using var stateLease = await AcquireStateLeaseAsync(recoveryId, cancellationToken).ConfigureAwait(false);
        var restoringPath = PathFor(recoveryId, RecoveryState.Restoring);
        if (!File.Exists(restoringPath))
        {
            throw new InvalidOperationException("The recovery material is not claimed by the current restore operation.");
        }

        var restoring = await ReadAsync(restoringPath, RecoveryState.Restoring, cancellationToken).ConfigureAwait(false);
        if (!FixedTimeHashEquals(restoring.IntegrityHash, integrityHash) || restoring.ClaimId != claimId)
        {
            throw new InvalidOperationException("The recovery material is not claimed by the current restore operation.");
        }

        var restoredPath = PathFor(recoveryId, RecoveryState.Restored);
        try
        {
            await WriteNewAsync(
                restoredPath,
                new StateDocument(CurrentSchemaVersion, RecoveryState.Restored, integrityHash, null, StateHash(RecoveryState.Restored, integrityHash, null)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(restoredPath))
        {
            var restored = await ReadAsync(restoredPath, RecoveryState.Restored, cancellationToken).ConfigureAwait(false);
            if (!FixedTimeHashEquals(restored.IntegrityHash, integrityHash))
            {
                throw new InvalidDataException("The restored marker has a different recovery integrity hash.");
            }
        }

        // The restored marker is now durable. Remove the in-flight marker so a
        // successful production restore does not leave the claim looking active.
        File.Delete(restoringPath);
    }

    public async Task ReleaseAsync(
        Guid recoveryId,
        string integrityHash,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(recoveryId, integrityHash);
        EnsureNonEmpty(claimId, nameof(claimId));
        using var stateLease = await AcquireStateLeaseAsync(recoveryId, cancellationToken).ConfigureAwait(false);
        var restoringPath = PathFor(recoveryId, RecoveryState.Restoring);
        if (!File.Exists(restoringPath))
        {
            return;
        }

        var restoring = await ReadAsync(restoringPath, RecoveryState.Restoring, cancellationToken).ConfigureAwait(false);
        if (!FixedTimeHashEquals(restoring.IntegrityHash, integrityHash) || restoring.ClaimId != claimId)
        {
            throw new InvalidDataException("The recovery claim being released does not match the current claim.");
        }

        File.Delete(restoringPath);
    }

    private string PathFor(Guid recoveryId, RecoveryState state)
    {
        EnsureNonEmpty(recoveryId, nameof(recoveryId));
        var candidate = Path.GetFullPath(Path.Combine(_rootDirectory, $"{recoveryId:N}.{state.ToString().ToLowerInvariant()}"));
        if (!candidate.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The recovery state path escapes the configured root.");
        }

        return candidate;
    }

    private async Task<FileStream> AcquireStateLeaseAsync(
        Guid recoveryId,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootDirectory);
        var lockPath = Path.Combine(_rootDirectory, $"{recoveryId:N}.lock");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<RecoveryClaimStateSnapshot?> TryReadStateAsync(
        Guid recoveryId,
        string integrityHash,
        RecoveryState state,
        CancellationToken cancellationToken)
    {
        var path = PathFor(recoveryId, state);
        if (!File.Exists(path))
        {
            return null;
        }

        StateDocument document;
        try
        {
            document = await ReadAsync(path, state, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        var updatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(path));
        if (!FixedTimeHashEquals(document.IntegrityHash, integrityHash))
        {
            return new RecoveryClaimStateSnapshot(RecoveryClaimReadStatus.IntegrityMismatch, updatedAt);
        }

        var status = state switch
        {
            RecoveryState.Available => RecoveryClaimReadStatus.Available,
            RecoveryState.Restoring => RecoveryClaimReadStatus.Restoring,
            RecoveryState.Restored => RecoveryClaimReadStatus.Restored,
            _ => throw new InvalidDataException("The recovery claim state is invalid.")
        };
        return new RecoveryClaimStateSnapshot(status, updatedAt);
    }

    private static async Task<StateDocument> ReadAsync(
        string path,
        RecoveryState expectedState,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var length = new FileInfo(path).Length;
                if (length <= 0 || length > MaximumStateBytes)
                {
                    throw new InvalidDataException("The recovery state file is empty or exceeds the size limit.");
                }

                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var document = await JsonSerializer.DeserializeAsync<StateDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                ValidateDocument(document, expectedState);
                return document!;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteNewAsync(
        string path,
        StateDocument document,
        CancellationToken cancellationToken)
    {
        ValidateDocument(document, document.State);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        if (bytes.Length > MaximumStateBytes)
        {
            throw new InvalidDataException("The recovery state file exceeds the size limit.");
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The recovery state path has no parent directory.");
        Directory.CreateDirectory(directory);
        CleanupStaleTemporaryFiles(directory, Path.GetFileName(path));
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.tmp-{Guid.NewGuid():N}");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            _ = await ReadAsync(temporaryPath, document.State, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path);
        }
        finally
        {
            TryDeleteOwnTemporaryFile(temporaryPath);
        }
    }

    private static void ValidateDocument(StateDocument? document, RecoveryState expectedState)
    {
        if (document is null
            || document.SchemaVersion != CurrentSchemaVersion
            || document.State != expectedState
            || !Enum.IsDefined(document.State)
            || !IsSha256(document.IntegrityHash)
            || !IsSha256(document.PayloadSha256)
            || (document.State == RecoveryState.Restoring) != document.ClaimId.HasValue
            || document.ClaimId == Guid.Empty)
        {
            throw new InvalidDataException("The recovery state envelope is missing, incompatible, or invalid.");
        }

        var expectedHash = StateHash(document.State, document.IntegrityHash, document.ClaimId);
        if (!FixedTimeHashEquals(document.PayloadSha256, expectedHash))
        {
            throw new InvalidDataException("The recovery state payload hash does not match its content.");
        }
    }

    private static string StateHash(RecoveryState state, string integrityHash, Guid? claimId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\u001F', state.ToString(), integrityHash, claimId?.ToString("N") ?? string.Empty))));

    private static void ValidateIdentity(Guid recoveryId, string integrityHash)
    {
        EnsureNonEmpty(recoveryId, nameof(recoveryId));
        if (!IsSha256(integrityHash))
        {
            throw new InvalidDataException("The recovery integrity hash must be a SHA-256 hexadecimal value.");
        }
    }

    private static void EnsureNonEmpty(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new InvalidDataException($"{name} must not be empty.");
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static character => char.IsAsciiHexDigit(character));

    private static bool FixedTimeHashEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));

    private static void TryDeleteOwnTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void CleanupStaleTemporaryFiles(string directory, string fileName)
    {
        var pattern = $".{fileName}.tmp-*";
        foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= TimeSpan.FromMinutes(5))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    internal enum RecoveryState
    {
        Available,
        Restoring,
        Restored
    }

    internal sealed record StateDocument(
        int SchemaVersion,
        RecoveryState State,
        string IntegrityHash,
        Guid? ClaimId,
        string PayloadSha256);
}
