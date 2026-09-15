using System.Security.Cryptography;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services.Hosts;
using GameOptimizer.Core.Services.SystemCleanup;

namespace GameOptimizer.Infrastructure.Windows.Services;

public sealed class WindowsHostsBackupOwner : IHostsBackupOwner
{
    private static readonly string HostsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
    private static readonly string BackupDirectory = Path.Combine(AppPaths.BackupsDir, "Hosts");
    private static readonly string BackupPath = Path.Combine(BackupDirectory, "hosts.backup");
    private static readonly string ManifestPath = Path.Combine(BackupDirectory, "hosts.backup.manifest");

    public Task<HostsPlatformAssessment> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return Task.FromResult(new HostsPlatformAssessment(false, "os.unsupported", "需要 Windows 10 2004 或更高版本。"));

        var administrator = IsAdministrator();
        return Task.FromResult(new HostsPlatformAssessment(
            administrator,
            administrator ? "applicable" : "permission.administrator-required",
            administrator
                ? "当前进程已提升，可备份与恢复 hosts 文件。"
                : "恢复 hosts 需要管理员权限。"));
    }

    public async Task<HostsSnapshot> ReadHostsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(HostsPath))
            return new HostsSnapshot(false, 0, DateTimeOffset.MinValue, string.Empty);

        var info = new FileInfo(HostsPath);
        var hash = await ComputeSha256Async(HostsPath, cancellationToken).ConfigureAwait(false);
        return new HostsSnapshot(true, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), hash);
    }

    public async Task<HostsBackupState> ReadBackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(BackupPath) || !File.Exists(ManifestPath))
            return new HostsBackupState(false, string.Empty, DateTimeOffset.MinValue);

        try
        {
            var manifestLines = await File.ReadAllLinesAsync(ManifestPath, cancellationToken).ConfigureAwait(false);
            if (manifestLines.Length == 0 || !CleanupResourceIdentity.IsSha256(manifestLines[0].Trim()))
                return new HostsBackupState(false, string.Empty, DateTimeOffset.MinValue);

            var hash = await ComputeSha256Async(BackupPath, cancellationToken).ConfigureAwait(false);
            var createdAt = File.GetCreationTimeUtc(BackupPath);
            return new HostsBackupState(
                true,
                hash,
                new DateTimeOffset(createdAt, TimeSpan.Zero));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return new HostsBackupState(false, string.Empty, DateTimeOffset.MinValue);
        }
    }

    public async Task<HostsMutationResult> BackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(HostsPath))
            return new HostsMutationResult(false, false, "hosts.not-found", "hosts 文件不存在，无法备份。");

        var currentHash = await ComputeSha256Async(HostsPath, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(BackupDirectory);

        if (File.Exists(BackupPath) && File.Exists(ManifestPath))
        {
            var existing = await ReadBackupAsync(cancellationToken).ConfigureAwait(false);
            if (existing.BackupExists && string.Equals(existing.ContentSha256, currentHash, StringComparison.Ordinal))
                return new HostsMutationResult(true, false, "no-change", "备份副本已存在且与当前 hosts 内容一致。");
        }

        var temporary = BackupPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var source = new FileStream(HostsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var copiedHash = await ComputeSha256Async(temporary, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(copiedHash, currentHash, StringComparison.Ordinal))
                return new HostsMutationResult(false, false, "backup.hash-mismatch", "备份副本哈希与源不一致，已丢弃临时文件。");

            File.Move(temporary, BackupPath, overwrite: true);
            await File.WriteAllTextAsync(
                ManifestPath,
                currentHash + Environment.NewLine + DateTimeOffset.UtcNow.ToString("O"),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        var after = await ReadBackupAsync(cancellationToken).ConfigureAwait(false);
        return after.BackupExists && string.Equals(after.ContentSha256, currentHash, StringComparison.Ordinal)
            ? new HostsMutationResult(true, true, "ok", $"hosts 已原子备份（SHA-256 校验通过，{FormatBytes(new FileInfo(BackupPath).Length)}）。")
            : new HostsMutationResult(false, true, "verification.mismatch", "备份回读校验失败。");
    }

    public async Task<HostsMutationResult> RestoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(BackupPath) || !File.Exists(ManifestPath))
            return new HostsMutationResult(false, false, "backup.not-found", "尚无 hosts 备份副本，拒绝恢复。");
        if (!File.Exists(HostsPath))
            return new HostsMutationResult(false, false, "hosts.not-found", "hosts 文件不存在，拒绝盲目重建。");

        var backupHash = await ComputeSha256Async(BackupPath, cancellationToken).ConfigureAwait(false);
        var manifestLines = await File.ReadAllLinesAsync(ManifestPath, cancellationToken).ConfigureAwait(false);
        if (manifestLines.Length == 0 || !string.Equals(manifestLines[0].Trim(), backupHash, StringComparison.Ordinal))
            return new HostsMutationResult(false, false, "backup.integrity-failed", "备份副本完整性校验失败（清单哈希不匹配），拒绝覆盖 hosts。");

        var current = await ReadHostsAsync(cancellationToken).ConfigureAwait(false);
        if (current.HostsExists && string.Equals(current.ContentSha256, backupHash, StringComparison.Ordinal))
            return new HostsMutationResult(true, false, "no-change", "当前 hosts 已经等于备份内容。");

        var temporary = HostsPath + ".yuqi-restore.tmp";
        try
        {
            File.Copy(BackupPath, temporary, overwrite: true);
            File.Replace(temporary, HostsPath, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            return new HostsMutationResult(false, false, "restore.failed", $"hosts 原子替换失败：{ex.Message}");
        }

        var after = await ReadHostsAsync(cancellationToken).ConfigureAwait(false);
        return after.HostsExists && string.Equals(after.ContentSha256, backupHash, StringComparison.Ordinal)
            ? new HostsMutationResult(true, true, "ok", "hosts 已从备份副本逐字节恢复并回读确认。")
            : new HostsMutationResult(false, true, "verification.mismatch", "恢复后的 hosts 与备份不一致。");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static bool IsAdministrator() => GameOptimizer.Core.Services.WindowsElevation.IsElevated;

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
