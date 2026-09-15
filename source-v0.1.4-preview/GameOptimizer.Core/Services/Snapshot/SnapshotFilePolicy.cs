using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameOptimizer.Core.Services.Snapshot;

internal sealed class SnapshotFilePolicy
{
    internal const long MaximumSnapshotBytes = 50 * 1024 * 1024;

    internal static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    private readonly string _root;
    private readonly string _rootPrefix;

    internal SnapshotFilePolicy(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _rootPrefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    internal string Root => _root;

    internal static void ValidateSnapshotId(string snapshotId) =>
        SnapshotRestoreCoordinator.ValidateSnapshotId(snapshotId);

    internal string GetSnapshotPath(string snapshotId)
    {
        ValidateSnapshotId(snapshotId);
        return EnsureContained(Path.Combine(_root, $"{snapshotId}.json"));
    }

    internal string GetLegacyRegistryDirectory(string snapshotId)
    {
        ValidateSnapshotId(snapshotId);
        return EnsureContained(Path.Combine(_root, snapshotId));
    }

    internal bool TryGetIdFromSnapshotFile(string path, out string snapshotId, out string diagnostic)
    {
        snapshotId = Path.GetFileNameWithoutExtension(path);
        try
        {
            var canonical = EnsureContained(path);
            ValidateSnapshotId(snapshotId);
            if (!string.Equals(canonical, GetSnapshotPath(snapshotId), StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = "snapshot.path.filename-mismatch";
                return false;
            }
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            diagnostic = "snapshot.path.invalid";
            return false;
        }
    }

    internal string EnsureContained(string path)
    {
        var canonical = Path.GetFullPath(path);
        if (!canonical.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Snapshot path escapes the configured root.");
        }
        if (File.Exists(canonical) || Directory.Exists(canonical))
        {
            FileSystemInfo info = Directory.Exists(canonical)
                ? new DirectoryInfo(canonical)
                : new FileInfo(canonical);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                var resolved = Path.GetFullPath(target.FullName);
                if (!resolved.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Snapshot link target escapes the configured root.");
                }
            }
        }
        return canonical;
    }

    internal static bool FixedChecksumEquals(string expected, string actual)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
