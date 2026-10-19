using GameOptimizer.Core.Services.Snapshot;

namespace GameOptimizer.Shell.Services.Baseline;

/// <summary>
/// Snapshot restore is a recovery intent. It must remain available when the first-run baseline is
/// missing; snapshot identity, integrity, confirmation and capability checks remain authoritative.
/// </summary>
public sealed class BaselineSnapshotRestoreAuthorizationGate : ISnapshotRestoreAuthorizationGate
{
    public Task<SnapshotRestoreAuthorizationDecision> AuthorizeAsync(
        SnapshotRestorePlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SnapshotRestoreAuthorizationDecision(
            true,
            "snapshot.recovery-intent-authorized",
            "恢复入口不受首次安全基线 Apply 策略阻断；其余恢复安全检查保持生效。"));
    }
}
