using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameOptimizer.Infrastructure.Windows.Changes;

internal static class DurableChangeDocument
{
    internal const int CurrentSchemaVersion = 3;
    internal const int MaximumDocumentBytes = 1024 * 1024;

    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static async Task<T?> ReadAsync<T>(
        string path,
        string expectedDocumentType,
        Action<T> validate,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            if (!File.Exists(path))
            {
                if (attempt >= maximumAttempts)
                {
                    return default;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var fileLength = new FileInfo(path).Length;
                if (fileLength <= 0 || fileLength > MaximumDocumentBytes)
                {
                    throw new InvalidDataException("The persisted document is empty or exceeds the size limit.");
                }

                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var document = await JsonSerializer.DeserializeAsync<Envelope<T>>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                ValidateEnvelope(document, expectedDocumentType, validate);
                return document!.Payload;
            }
            catch (FileNotFoundException) when (attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal static async Task WriteAsync<T>(
        string path,
        string documentType,
        T value,
        bool overwrite,
        Action<T> validate,
        CancellationToken cancellationToken,
        bool acquireCommitLease = true)
    {
        validate(value);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        var envelope = new Envelope<T>(
            CurrentSchemaVersion,
            documentType,
            Convert.ToHexString(SHA256.HashData(payloadBytes)),
            value);
        var documentBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        if (documentBytes.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException("The persisted document exceeds the size limit.");
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The persistence path has no parent directory.");
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
                await stream.WriteAsync(documentBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            _ = await ReadAsync(temporaryPath, documentType, validate, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The temporary persistence document could not be read back.");

            FileStream? commitLease = null;
            if (acquireCommitLease)
            {
                commitLease = await AcquireCommitLeaseAsync(path, cancellationToken).ConfigureAwait(false);
            }

            using (commitLease)
            {
                Commit(temporaryPath, path, overwrite);
            }
        }
        finally
        {
            TryDeleteOwnTemporaryFile(temporaryPath);
        }
    }

    internal static string PayloadHash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions)));

    private static void ValidateEnvelope<T>(Envelope<T>? document, string expectedDocumentType, Action<T> validate)
    {
        if (document is null
            || document.SchemaVersion != CurrentSchemaVersion
            || !string.Equals(document.DocumentType, expectedDocumentType, StringComparison.Ordinal)
            || document.Payload is null
            || !IsSha256(document.PayloadSha256))
        {
            throw new InvalidDataException("The persisted document envelope is missing, incompatible, or invalid.");
        }

        var actualHash = PayloadHash(document.Payload);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(document.PayloadSha256),
                Convert.FromHexString(actualHash)))
        {
            throw new InvalidDataException("The persisted document payload hash does not match its content.");
        }

        validate(document.Payload);
    }

    private static void Commit(string temporaryPath, string destinationPath, bool overwrite)
    {
        if (!overwrite)
        {
            File.Move(temporaryPath, destinationPath);
            return;
        }

        if (!File.Exists(destinationPath))
        {
            try
            {
                File.Move(temporaryPath, destinationPath);
                return;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // Another writer won creation. Replace only with a fully flushed document.
            }
        }

        File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
    }

    internal static async Task<FileStream> AcquireCommitLeaseAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var lockPath = destinationPath + ".lock";
        var directory = Path.GetDirectoryName(lockPath)
            ?? throw new InvalidOperationException("The persistence lock path has no parent directory.");
        Directory.CreateDirectory(directory);
        var attempts = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++attempts > 2000)
            {
                throw new IOException("Timed out acquiring the persistence commit lease.");
            }
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
            // The unique temporary file is never treated as committed state. A later maintenance pass may remove it.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the original persistence failure; do not mask it with cleanup failure.
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

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static character => char.IsAsciiHexDigit(character));

    private sealed record Envelope<T>(
        int SchemaVersion,
        string DocumentType,
        string PayloadSha256,
        T Payload);
}
