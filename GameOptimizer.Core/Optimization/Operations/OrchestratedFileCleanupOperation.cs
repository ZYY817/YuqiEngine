// File: GameOptimizer.Core/Optimization/Operations/OrchestratedFileCleanupOperation.cs
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Services.SystemCleanup;

namespace GameOptimizer.Core.Optimization.Operations;

/// <summary>
/// Typed owner for recoverable file-cleanup catalog entries. It delegates to the single public
/// <see cref="ISystemCleanupService"/> (preview → confirmation → quarantine-based clean) and to the
/// central <see cref="ICleanupQuarantineManager"/> for byte-exact SHA-256 verified restore.
/// It never deletes files directly and never fabricates recovery material.
/// </summary>
public sealed class OrchestratedFileCleanupOperation : OrchestratedChangeOperation
{
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(10);

    private readonly string _cleanupItemId;
    private readonly ISystemCleanupService? _cleanupService;
    private readonly ICleanupQuarantineManager? _quarantineManager;
    private readonly ICleanupAuthorizationGate? _authorizationGate;
    private readonly TimeProvider _timeProvider;

    public OrchestratedFileCleanupOperation(
        IChangeOrchestrator orchestrator,
        string id,
        string title,
        string description,
        string category,
        OperationRiskLevel riskLevel,
        string cleanupItemId,
        string? benefit,
        string? drawback,
        ISystemCleanupService? cleanupService,
        ICleanupQuarantineManager? quarantineManager = null,
        ICleanupAuthorizationGate? authorizationGate = null,
        TimeProvider? timeProvider = null)
        : base(
            orchestrator,
            id,
            title,
            description,
            category,
            OperationType.Other,
            riskLevel,
            benefit,
            drawback,
            requiresExplicitConfirmation: true,
            scopeKind: TargetScopeKind.Machine,
            requiresAdministrator: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cleanupItemId);
        _cleanupItemId = cleanupItemId;
        _cleanupService = cleanupService;
        _quarantineManager = quarantineManager;
        _authorizationGate = authorizationGate;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>清理类操作不写入操作快照库：恢复材料由中央隔离区的持久化清单承载。</summary>
    public override bool RequiresSnapshotForRestore => false;

    public override string OptimizedValue => "Quarantined clean";

    public override async Task<OperationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_cleanupService is null)
        {
            return new OperationStatus(
                false,
                "系统清理服务未接入，该清理项保持阻断。",
                SupportState: OperationSupportState.Unknown,
                SupportReasonCode: "cleanup.owner-unavailable");
        }

        var item = ResolveItem();
        if (item is null)
        {
            return new OperationStatus(
                false,
                "系统清理目录中不存在该项，已阻断。",
                SupportState: OperationSupportState.NotApplicable,
                SupportReasonCode: "cleanup.item-not-found");
        }

        try
        {
            var preview = await _cleanupService.CreatePreviewAsync(item, cancellationToken).ConfigureAwait(false);
            return preview.Entries.Count == 0
                ? new OperationStatus(true, "当前没有可清理的文件。")
                : new OperationStatus(false, $"发现 {preview.Entries.Count} 个可清理文件（{FormatBytes(preview.TotalBytes)}），需显式确认后执行。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperationStatus(
                false,
                $"清理预览失败：{ex.Message}",
                SupportState: OperationSupportState.Unknown,
                SupportReasonCode: "cleanup.preview-failed");
        }
    }

    public override async Task<OperationInvokeResult> ApplyAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (_cleanupService is null)
        {
            return new OperationInvokeResult(false, false, "系统清理服务未接入，未执行任何文件操作。");
        }

        var item = ResolveItem();
        if (item is null)
        {
            return new OperationInvokeResult(false, false, "系统清理目录中不存在该项，未执行任何文件操作。");
        }

        CleanupPreview preview;
        try
        {
            preview = await _cleanupService.CreatePreviewAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, $"创建清理预览失败：{ex.Message}");
        }

        if (preview.Entries.Count == 0)
        {
            return new OperationInvokeResult(true, false, "没有需要清理的文件。");
        }

        if (_authorizationGate is not null)
        {
            var decision = await _authorizationGate.AuthorizeAsync(preview, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                return new OperationInvokeResult(false, false, $"{decision.ReasonCode}: {decision.Message}");
            }
        }

        var confirmation = CleanupPreviewConfirmation.Create(preview, _timeProvider.GetUtcNow(), ConfirmationLifetime);
        CleanupResult result;
        try
        {
            result = await _cleanupService.CleanPreviewAsync(item, preview, confirmation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, $"清理执行失败：{ex.Message}");
        }

        if (result.FilesFailed > 0 || result.Errors.Count > 0)
        {
            var firstError = result.Errors.Count > 0 ? result.Errors[0] : "存在失败文件";
            return new OperationInvokeResult(
                false,
                result.FilesDeleted > 0,
                $"清理部分完成：已隔离 {result.FilesDeleted} 个文件，{result.FilesFailed} 个失败（{firstError}）。已隔离文件可通过恢复流程找回。");
        }

        return new OperationInvokeResult(
            true,
            result.FilesDeleted > 0,
            $"已安全清理 {result.FilesDeleted} 个文件（释放 {FormatBytes(result.TotalCleaned)}），全部进入可校验隔离区，可随时恢复。");
    }

    public override async Task<OperationInvokeResult> RestoreAsync(
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (_quarantineManager is null)
        {
            return new OperationInvokeResult(false, false, "中央隔离区管理器未接入，拒绝恢复。");
        }

        try
        {
            var preview = await _quarantineManager.CreateRestorePreviewAsync(cancellationToken).ConfigureAwait(false);
            if (preview.Entries.Count == 0)
            {
                return new OperationInvokeResult(true, false, "隔离区中没有可恢复的历史文件。");
            }

            var confirmation = CleanupQuarantineConfirmation.Create(preview, _timeProvider.GetUtcNow(), ConfirmationLifetime);
            var result = await _quarantineManager.RestoreAsync(preview, confirmation, cancellationToken).ConfigureAwait(false);
            if (result.Failed > 0)
            {
                var blocked = result.Items.FirstOrDefault(item =>
                    item.Outcome is CleanupQuarantineOutcome.ContentMismatch or CleanupQuarantineOutcome.RecoveryRequired);
                return new OperationInvokeResult(
                    false,
                    result.Succeeded > 0,
                    $"恢复部分完成：成功 {result.Succeeded} 个，失败 {result.Failed} 个{(blocked is null ? "" : $"（{blocked.ReasonCode}）")}。失败的条目保留在隔离区等待人工核查。");
            }

            return new OperationInvokeResult(
                true,
                result.Succeeded > 0,
                $"已从隔离区逐字节校验并恢复 {result.Succeeded} 个文件。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new OperationInvokeResult(false, false, $"恢复失败：{ex.Message}");
        }
    }

    private CleanupItem? ResolveItem()
    {
        var items = _cleanupService?.GetCleanupItems();
        foreach (var item in items ?? [])
        {
            if (string.Equals(item.Id, _cleanupItemId, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "未知";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
