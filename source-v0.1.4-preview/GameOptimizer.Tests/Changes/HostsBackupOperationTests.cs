using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Services.Hosts;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameOptimizer.Tests.Changes;

public sealed class HostsBackupOperationTests
{
    [Fact]
    public async Task Apply_WithoutOwnerFailsClosed()
    {
        var operation = CreateOperation(owner: null);
        var result = await operation.ApplyAsync(NewContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Changed);
    }

    [Fact]
    public async Task Apply_DelegatesToOwnerAndReportsNoChangeWhenContentUnchanged()
    {
        var owner = new FakeHostsBackupOwner { BackupResult = new(true, false, "no-change", "内容一致") };
        var operation = CreateOperation(owner);

        var result = await operation.ApplyAsync(NewContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Equal(1, owner.BackupCalls);
    }

    [Fact]
    public async Task Apply_PermissionDenialBlocksBeforeBackup()
    {
        var owner = new FakeHostsBackupOwner
        {
            ProbeResult = new(false, "permission.administrator-required", "需要管理员权限。")
        };
        var operation = CreateOperation(owner);

        var result = await operation.ApplyAsync(NewContext(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, owner.BackupCalls);
    }

    [Fact]
    public async Task Restore_DelegatesToOwnerAndVerifiesHash()
    {
        var owner = new FakeHostsBackupOwner { RestoreResult = new(true, true, "ok", "已逐字节恢复") };
        var operation = CreateOperation(owner);

        var result = await operation.RestoreAsync(NewContext(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Changed);
        Assert.Equal(1, owner.RestoreCalls);
    }

    [Fact]
    public async Task GetStatus_ReflectsBackupFreshness()
    {
        var missing = CreateOperation(new FakeHostsBackupOwner());
        var missingStatus = await missing.GetStatusAsync(CancellationToken.None);
        Assert.False(missingStatus.IsCompliant);

        var stale = new FakeHostsBackupOwner
        {
            Hosts = new(true, 10, DateTimeOffset.UtcNow, "AAAA"),
            Backup = new(true, "BBBB", DateTimeOffset.UtcNow)
        };
        var staleStatus = await CreateOperation(stale).GetStatusAsync(CancellationToken.None);
        Assert.False(staleStatus.IsCompliant);

        var fresh = new FakeHostsBackupOwner
        {
            Hosts = new(true, 10, DateTimeOffset.UtcNow, "AAAA"),
            Backup = new(true, "AAAA", DateTimeOffset.UtcNow)
        };
        var freshStatus = await CreateOperation(fresh).GetStatusAsync(CancellationToken.None);
        Assert.True(freshStatus.IsCompliant);
    }

    private static OrchestratedHostsBackupOperation CreateOperation(IHostsBackupOwner? owner) =>
        new(
            "cleanup.hosts_backup",
            "备份/恢复 hosts 文件",
            "对系统 hosts 文件创建 SHA-256 校验的原子备份副本。",
            "系统清理",
            OperationRiskLevel.Balanced,
            "修改网络配置前留有可靠回退点",
            "恢复会覆盖当前 hosts 中未被备份记录的更改。",
            owner);

    private static OperationExecutionContext NewContext() =>
        new(new CountingRestoreStore(), NullLogger.Instance);

    private sealed class CountingRestoreStore : GameOptimizer.Core.Optimization.IOperationRestoreStore
    {
        public int Calls { get; private set; }
        public Task SaveSnapshotAsync(string operationId, string payload, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
        public Task<string?> LoadSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.FromResult<string?>(null); }
        public Task DeleteSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
        public Task<bool> HasSnapshotAsync(string operationId, CancellationToken cancellationToken) { Calls++; return Task.FromResult(false); }
    }

    private sealed class FakeHostsBackupOwner : IHostsBackupOwner
    {
        public int BackupCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public HostsPlatformAssessment ProbeResult { get; set; } = new(true, "applicable", "fake");
        public HostsSnapshot Hosts { get; set; } = new(false, 0, DateTimeOffset.MinValue, string.Empty);
        public HostsBackupState Backup { get; set; } = new(false, string.Empty, DateTimeOffset.MinValue);
        public HostsMutationResult BackupResult { get; set; } = new(true, false, "no-change", "no-change");
        public HostsMutationResult RestoreResult { get; set; } = new(true, false, "no-change", "no-change");

        public Task<HostsPlatformAssessment> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProbeResult);

        public Task<HostsSnapshot> ReadHostsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Hosts);

        public Task<HostsBackupState> ReadBackupAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Backup);

        public Task<HostsMutationResult> BackupAsync(CancellationToken cancellationToken)
        {
            BackupCalls++;
            return Task.FromResult(BackupResult);
        }

        public Task<HostsMutationResult> RestoreAsync(CancellationToken cancellationToken)
        {
            RestoreCalls++;
            return Task.FromResult(RestoreResult);
        }
    }
}
