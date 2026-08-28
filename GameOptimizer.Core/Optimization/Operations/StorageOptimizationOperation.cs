using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// 禁用 NTFS 8.3 短文件名 - 提升文件系统性能
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisableNtfs8Dot3Operation : IOptimizationOperation
{
    public string Id => "storage.disable_8dot3";
    public string Title => "禁用 NTFS 短文件名";
    public string Description => "禁用 8.3 短文件名生成，提升 NTFS 文件系统性能，减少磁盘写入。";
    public string Category => "存储优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit => "可能提升文件系统性能，减少磁盘写入";
    public string? Drawback => "极少数老程序可能无法识别长文件名";
    public bool RequiresAdmin => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct) =>
        Task.FromResult(new OperationStatus(false, "NTFS 8.3 mutation 已由存储安全边界阻断。"));

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct) =>
        Blocked("禁用 NTFS 8.3 名称会改变卷级兼容行为，缺少跨版本恢复证据。");

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct) =>
        Blocked("旧快照不是 durable typed receipt，不能安全恢复 NTFS 8.3 设置。");

    private static Task<OperationInvokeResult> Blocked(string message) =>
        Task.FromResult(new OperationInvokeResult(false, false, message));
}


/// <summary>
/// 优化 IoPageLockLimit - 提升大文件 I/O 性能
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OptimizeIoPageLockLimitOperation : IOptimizationOperation
{
    public string Id => "storage.optimize_io_page_lock";
    public string Title => "优化 I/O 页面锁定";
    public string Description => "增加 IoPageLockLimit 值，提升大文件读写性能，适合 16GB+ 内存用户。";
    public string Category => "存储优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit => "可能提升大文件读写性能";
    public string? Drawback => "占用更多内存用于 I/O 缓冲";
    public bool RequiresAdmin => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct) =>
        Task.FromResult(new OperationStatus(false, "IoPageLockLimit 的现代 Windows 目标语义未被证明。"));

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct) =>
        Blocked("IoPageLockLimit 固定值缺少版本化语义、适用性和恢复证据，已阻断。");

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct) =>
        Blocked("旧快照不是 durable typed receipt，不能安全恢复 IoPageLockLimit。");

    private static Task<OperationInvokeResult> Blocked(string message) =>
        Task.FromResult(new OperationInvokeResult(false, false, message));
}

/// <summary>
/// 清理待机内存列表 - 释放缓存内存
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClearStandbyListOperation : IOptimizationOperation
{
    public string Id => "storage.clear_standby_list";
    public string Title => "清理待机内存";
    public string Description => "清理 Windows 待机内存列表，释放被缓存占用的内存，适合游戏前执行。";
    public string Category => "存储优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Safe;
    public string? Benefit => "释放被缓存占用的内存";
    public string? Drawback => "清理后首次访问文件会稍慢";
    public bool RequiresAdmin => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationStatus(
            false,
            "Standby List 没有可持久化的原始材料和真实逆操作，当前不可执行。"));
    }

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct) =>
        Blocked(ct, "Standby List 清理不可逆，无法生成真实 RecoveryId；未执行系统写入。");

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct) =>
        Blocked(ct, "不存在与旧 Standby List 清理绑定的真实恢复材料，拒绝伪造恢复成功。");

    private static Task<OperationInvokeResult> Blocked(CancellationToken cancellationToken, string message)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new OperationInvokeResult(false, false, message));
    }
}

/// <summary>
/// 禁用 NTFS 加密 - 减少文件系统开销
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisableNtfsEncryptionOperation : IOptimizationOperation
{
    public string Id => "storage.disable_ntfs_encryption";
    public string Title => "禁用 NTFS 加密";
    public string Description => "禁用 NTFS 文件系统加密功能，减少 CPU 开销。⚠️ 注意：已加密文件不受影响，但无法新建加密文件。";
    public string Category => "存储优化";
    public OperationRiskLevel RiskLevel { get; init; } = OperationRiskLevel.Balanced;
    public string? Benefit => "可能减少 CPU 开销";
    public string? Drawback => "无法新建 EFS 加密文件";
    public bool RequiresAdmin => true;

    public Task<OperationStatus> GetStatusAsync(CancellationToken ct) =>
        Task.FromResult(new OperationStatus(false, "NTFS/EFS 可用性只由存储安全 Owner 只读诊断。"));

    public Task<OperationInvokeResult> ApplyAsync(OperationExecutionContext ctx, CancellationToken ct) =>
        Blocked("禁用 NTFS/EFS 会影响密钥与用户数据，缺少完整恢复证明，已阻断。");

    public Task<OperationInvokeResult> RestoreAsync(OperationExecutionContext ctx, CancellationToken ct) =>
        Blocked("旧快照不包含密钥、卷与文件身份，不能冒充安全恢复。");

    private static Task<OperationInvokeResult> Blocked(string message) =>
        Task.FromResult(new OperationInvokeResult(false, false, message));
}
