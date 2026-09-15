namespace GameOptimizer.Core.Services.Hosts;

public sealed record HostsSnapshot(
    bool HostsExists,
    long SizeBytes,
    DateTimeOffset LastWriteTimeUtc,
    string ContentSha256);

public sealed record HostsBackupState(
    bool BackupExists,
    string ContentSha256,
    DateTimeOffset CreatedAtUtc);

public sealed record HostsPlatformAssessment(
    bool HasRequiredPermission,
    string ReasonCode,
    string Details);

public sealed record HostsMutationResult(
    bool Success,
    bool MutationOccurred,
    string ReasonCode,
    string Message);

/// <summary>中央 hosts 备份 Owner：原子备份、哈希校验、原子替换恢复；禁止联网拉取与追加语义。</summary>
public interface IHostsBackupOwner
{
    Task<HostsPlatformAssessment> ProbeAsync(CancellationToken cancellationToken);

    Task<HostsSnapshot> ReadHostsAsync(CancellationToken cancellationToken);

    Task<HostsBackupState> ReadBackupAsync(CancellationToken cancellationToken);

    /// <summary>创建或刷新备份副本并回读校验；内容未变化时返回 no-change。</summary>
    Task<HostsMutationResult> BackupAsync(CancellationToken cancellationToken);

    /// <summary>校验备份完整性后用临时文件原子替换回 hosts；恢复后回读哈希确认。</summary>
    Task<HostsMutationResult> RestoreAsync(CancellationToken cancellationToken);
}
