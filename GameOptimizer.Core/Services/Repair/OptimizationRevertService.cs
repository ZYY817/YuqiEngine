using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Core.Services.Repair;

public sealed class OptimizationRevertService : IOptimizationRevertService
{
    private const string ItemPrefix = "revert.";
    private const string StatusPrefix = "durable-revert-status.";
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PersistenceTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IOptimizationEngine _engine;
    private readonly IOperationRestoreStore? _restoreStore;
    private readonly IChangeAuditStore? _auditStore;
    private readonly IRecoveryRepository? _recoveryRepository;
    private readonly IChangeOrchestrator? _orchestrator;
    private readonly IChangeConfirmationPrompt? _confirmationPrompt;
    private readonly IOperationHistoryService? _history;
    private readonly TimeProvider _timeProvider;
    private readonly Lazy<IReadOnlyList<RepairItem>> _revertItems;
    private readonly ConcurrentDictionary<Guid, OptimizationRevertPlan> _issuedPlans = new();
    private bool _initFailed;
    private string? _initError;

    // Compatibility constructor: without the durable dependencies every revert remains visible but
    // fails closed. It must never fall back to the legacy engine restore path.
    public OptimizationRevertService(IOptimizationEngine engine)
        : this(engine, null, null, null, null, null, null, null)
    {
    }

    public OptimizationRevertService(
        IOptimizationEngine engine,
        IOperationRestoreStore? restoreStore,
        IChangeAuditStore? auditStore,
        IRecoveryRepository? recoveryRepository,
        IChangeOrchestrator? orchestrator,
        IChangeConfirmationPrompt? confirmationPrompt,
        IOperationHistoryService? history,
        TimeProvider? timeProvider = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _restoreStore = restoreStore;
        _auditStore = auditStore;
        _recoveryRepository = recoveryRepository;
        _orchestrator = orchestrator;
        _confirmationPrompt = confirmationPrompt;
        _history = history;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _revertItems = new Lazy<IReadOnlyList<RepairItem>>(InitializeItems, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool HasInitError => _initFailed;
    public string? InitErrorMessage => _initError;

    public IReadOnlyList<RepairItem> GetAllRevertItems()
    {
        try
        {
            return _revertItems.Value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OptimizationRevertService] GetAllRevertItems failed: {ex}");
            return [];
        }
    }

    public bool IsDynamicRevertItem(string repairItemId) =>
        !string.IsNullOrWhiteSpace(repairItemId)
        && repairItemId.StartsWith(ItemPrefix, StringComparison.OrdinalIgnoreCase)
        && FindOperation(repairItemId[ItemPrefix.Length..]) is not null;

    public async Task<OptimizationRevertStatus> GetStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        if (!HasUniqueOperation(operationId))
        {
            return Status(operationId, OptimizationRevertAvailability.Unrecoverable, "revert.operation-unknown", "优化操作不存在或 ID 不唯一。", now);
        }

        if (!HasDurableDependencies())
        {
            return Status(operationId, OptimizationRevertAvailability.Unrecoverable, "revert.adapter-unavailable", "durable recovery 适配器未完整接入，已安全阻断旧恢复入口。", now);
        }

        OptimizationRecoveryPointer? pointer;
        try
        {
            pointer = await LoadPointerAsync(operationId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Status(operationId, OptimizationRevertAvailability.Unrecoverable, "revert.pointer-invalid", ex.Message, now);
        }

        var durableRead = await LoadDurableStatusAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (durableRead.Error is not null)
        {
            return Status(
                operationId,
                OptimizationRevertAvailability.Unrecoverable,
                durableRead.ReasonCode!,
                durableRead.Error,
                now);
        }

        var durable = durableRead.Status;
        if (pointer?.RecoveryId is null)
        {
            return durable?.Availability == OptimizationRevertAvailability.Restored
                ? Status(operationId, OptimizationRevertAvailability.Restored, durable.ReasonCode, durable.Message, durable.UpdatedAtUtc)
                : Status(operationId, OptimizationRevertAvailability.Unrecoverable, "revert.binding-missing", "没有与该操作绑定的 durable recovery material；旧历史不能猜测恢复。", now);
        }

        if (durable is not null && durable.RecoveryId == pointer.RecoveryId)
        {
            if (durable.Availability == OptimizationRevertAvailability.Restored)
            {
                return Status(operationId, durable.Availability, durable.ReasonCode, durable.Message, durable.UpdatedAtUtc);
            }

            if (durable.Availability == OptimizationRevertAvailability.RestoringInterrupted)
            {
                return Status(operationId, durable.Availability, durable.ReasonCode, durable.Message, durable.UpdatedAtUtc);
            }
        }

        var resolved = await ResolveBindingAsync(operationId, pointer, cancellationToken).ConfigureAwait(false);
        return resolved.Error ?? Status(operationId, OptimizationRevertAvailability.Available, "revert.available", "存在完整且可验证的 durable recovery 凭据。", now);
    }

    public async Task<OptimizationRevertPreparation> PrepareAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (status.Availability != OptimizationRevertAvailability.Available)
        {
            return new OptimizationRevertPreparation(status, null);
        }

        var pointer = await LoadPointerAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (pointer?.RecoveryId is null)
        {
            return new OptimizationRevertPreparation(
                Status(operationId, OptimizationRevertAvailability.Unrecoverable, "revert.binding-missing", "恢复绑定缺失。", _timeProvider.GetUtcNow()),
                null);
        }

        var resolved = await ResolveBindingAsync(operationId, pointer, cancellationToken).ConfigureAwait(false);
        if (resolved.Error is not null || resolved.Material is null || resolved.Operation is null)
        {
            return new OptimizationRevertPreparation(
                resolved.Error ?? Status(operationId, OptimizationRevertAvailability.Unrecoverable, "revert.binding-invalid", "恢复绑定无效。", _timeProvider.GetUtcNow()),
                null);
        }

        var preparedAt = _timeProvider.GetUtcNow();
        var expiresAt = preparedAt.Add(PlanLifetime);
        var planId = Guid.NewGuid();
        var material = resolved.Material;
        var risk = ToChangeRisk(resolved.Operation.RiskLevel);
        var hash = ComputePlanHash(
            planId,
            operationId,
            pointer.ReceiptId,
            material,
            risk,
            preparedAt,
            expiresAt);
        var plan = new OptimizationRevertPlan(
            planId,
            operationId,
            pointer.ReceiptId,
            material.RecoveryId,
            material.CapabilityId,
            material.DefinitionVersion,
            material.ResourceKey,
            material.Scope,
            material.BeforeFingerprint,
            material.TargetFingerprint,
            material.IntegrityHash,
            risk,
            preparedAt,
            expiresAt,
            hash);
        _issuedPlans[plan.PlanId] = plan;
        RemoveExpiredPlans(preparedAt);
        return new OptimizationRevertPreparation(status, plan);
    }

    public async Task<RepairResult> ExecuteAsync(
        OptimizationRevertPlan plan,
        IProgress<RepairProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var startedAt = _timeProvider.GetTimestamp();
        if (!_issuedPlans.TryRemove(plan.PlanId, out var issued)
            || !PlansEqual(issued, plan)
            || plan.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return RepairResult.Failed("恢复计划不存在、已过期、已使用或内容被篡改。") with { Duration = _timeProvider.GetElapsedTime(startedAt) };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var pointer = await LoadPointerAsync(plan.OperationId, cancellationToken).ConfigureAwait(false);
        var resolved = pointer is null
            ? new ResolvedBinding(null, null, Status(plan.OperationId, OptimizationRevertAvailability.Unrecoverable, "revert.binding-missing", "恢复绑定已不存在。", _timeProvider.GetUtcNow()))
            : await ResolveBindingAsync(plan.OperationId, pointer, cancellationToken).ConfigureAwait(false);
        if (resolved.Error is not null || resolved.Material is null || !PlanMatchesMaterial(plan, pointer!, resolved.Material))
        {
            return RepairResult.Failed(resolved.Error?.Message ?? "恢复计划与当前 durable recovery 凭据不匹配。") with { Duration = _timeProvider.GetElapsedTime(startedAt) };
        }

        if (_confirmationPrompt is null || _orchestrator is null)
        {
            return RepairResult.Failed("显式确认或 durable recovery 服务未接入，已安全阻断。") with { Duration = _timeProvider.GetElapsedTime(startedAt) };
        }

        var confirmed = await _confirmationPrompt.ConfirmAsync(
            new ChangeConfirmationPrompt(
                plan.PlanId,
                plan.CapabilityId,
                plan.Risk,
                $"还原：{resolved.Operation!.Title}",
                "将使用原执行回执恢复到变更前状态；恢复材料只能成功使用一次。",
                plan.ExpiresAtUtc),
            cancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            return RepairResult.Failed("用户未确认或确认已取消，未执行任何恢复写入。") with { Duration = _timeProvider.GetElapsedTime(startedAt) };
        }

        progress?.Report(new RepairProgress { Message = "正在锁定一次性恢复材料…", PercentComplete = 30 });
        try
        {
            await SaveDurableStatusAsync(
                plan.OperationId,
                plan.RecoveryId,
                OptimizationRevertAvailability.RestoringInterrupted,
                "revert.restore-started",
                "恢复已经进入 durable 协调器；若进程中断，必须人工核查后处理。",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return RepairResult.Failed("无法持久化恢复开始状态，未调用平台恢复。", ex) with { Duration = _timeProvider.GetElapsedTime(startedAt) };
        }

        progress?.Report(new RepairProgress { Message = "正在通过统一协调器恢复并回读…", PercentComplete = 60 });
        BatchReceipt receipt;
        try
        {
            receipt = await _orchestrator.RestoreAsync(
                new RestoreRequest(plan.ReceiptId, plan.RecoveryId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await TryPersistInterruptedAsync(plan, "revert.orchestrator-exception", ex.Message).ConfigureAwait(false);
            RecordHistory(plan, false, true, OptimizationRevertAvailability.RestoringInterrupted, "revert.orchestrator-exception", ex.Message, _timeProvider.GetElapsedTime(startedAt));
            return RepairResult.Failed("恢复协调器异常；恢复状态未知，禁止自动重试。", ex) with { Duration = _timeProvider.GetElapsedTime(startedAt) };
        }

        var terminal = ClassifyTerminal(receipt.Operation);
        try
        {
            await SaveDurableStatusAsync(
                plan.OperationId,
                plan.RecoveryId,
                terminal.Availability,
                receipt.Operation.ReasonCode,
                receipt.Operation.Message,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RecordHistory(plan, false, true, OptimizationRevertAvailability.RestoringInterrupted, "revert.status-persist-failed", ex.Message, _timeProvider.GetElapsedTime(startedAt));
            return RepairResult.Failed("恢复已返回结果，但终态持久化失败，必须人工核查，禁止自动重试。", ex) with { Duration = _timeProvider.GetElapsedTime(startedAt) };
        }

        // A durable claim can report that another instance already restored the material. That
        // establishes the global status, but it is not a verified restore performed by this plan.
        // Keep the distinction so concurrent callers cannot both receive a success result.
        var success = IsVerifiedRestoreExecution(receipt.Operation);
        if (success)
        {
            await _restoreStore!.DeleteSnapshotAsync(plan.OperationId, CancellationToken.None).ConfigureAwait(false);
            progress?.Report(new RepairProgress { Message = "恢复完成并通过回读验证。", PercentComplete = 100 });
        }

        RecordHistory(plan, success, receipt.Operation.MutationOccurred, terminal.Availability, receipt.Operation.ReasonCode, receipt.Operation.Message, _timeProvider.GetElapsedTime(startedAt));
        return success
            ? RepairResult.Succeeded(receipt.Operation.Message) with { Duration = _timeProvider.GetElapsedTime(startedAt) }
            : RepairResult.Failed(terminal.Message) with { Duration = _timeProvider.GetElapsedTime(startedAt) };
    }

    public async Task<RepairResult> RevertOperationAsync(
        string operationId,
        IProgress<RepairProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (preparation.Plan is null)
        {
            return RepairResult.Failed(preparation.Status.Message);
        }

        return await ExecuteAsync(preparation.Plan, progress, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<RepairItem> InitializeItems()
    {
        var items = new List<RepairItem>();
        try
        {
            foreach (var module in _engine.Modules)
            {
                foreach (var operation in module.Operations)
                {
                    items.Add(CreateRevertItem(operation));
                }
            }
        }
        catch (Exception ex)
        {
            _initFailed = true;
            _initError = ex.Message;
            Debug.WriteLine($"[OptimizationRevertService] Init error: {ex}");
        }

        return items;
    }

    private RepairItem CreateRevertItem(IOptimizationOperation operation)
    {
        var operationId = operation.Id;
        return new RepairItem
        {
            Id = ItemPrefix + operationId,
            Name = $"还原：{operation.Title}",
            Description = $"仅使用该操作原执行时持久化的一次性恢复材料。{operation.Description}",
            Category = RepairCategory.OptimizationRevert,
            ExecutionIntent = RepairExecutionIntent.Restore,
            Icon = GetIconForCategory(operation.Category),
            IsRisky = true,
            RequiresAdmin = operation.RequiresAdmin,
            RelatedOptimization = operationId,
            CheckHealthAsync = async () =>
                (await GetStatusAsync(operationId, CancellationToken.None).ConfigureAwait(false)).Availability
                != OptimizationRevertAvailability.Available,
            // Compatibility-only delegate. RepairService must route dynamic IDs directly so the
            // caller cancellation token is preserved; this path is still durable and never legacy.
            RepairAsync = progress => RevertOperationAsync(operationId, progress, CancellationToken.None)
        };
    }

    private async Task<ResolvedBinding> ResolveBindingAsync(
        string operationId,
        OptimizationRecoveryPointer pointer,
        CancellationToken cancellationToken)
    {
        var operation = FindOperation(operationId);
        if (operation is null || !HasUniqueOperation(operationId))
        {
            return Error(operationId, "revert.operation-unknown", "优化操作不存在或 ID 不唯一。");
        }

        if (pointer.ReceiptId == Guid.Empty || pointer.RecoveryId is null)
        {
            return Error(operationId, "revert.binding-incomplete", "旧记录缺少 ReceiptId 或 RecoveryId，不能安全恢复。");
        }

        RecoveryMaterial? material;
        try
        {
            material = await _recoveryRepository!.LoadAsync(pointer.RecoveryId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error(operationId, "revert.recovery-read-failed", $"恢复材料读取失败：{ex.Message}");
        }

        if (material is null)
        {
            return Error(operationId, "revert.recovery-missing", "恢复材料不存在；禁止根据 operationId 猜测恢复。");
        }

        if (!string.Equals(material.CapabilityId, operationId, StringComparison.OrdinalIgnoreCase)
            || !PointerMatchesMaterial(pointer, operationId, material))
        {
            return Error(operationId, "revert.binding-mismatch", "操作、回执指针与恢复材料不匹配。");
        }

        OperationReceipt? sourceReceipt = null;
        try
        {
            sourceReceipt = await _auditStore!.LoadAsync(pointer.ReceiptId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[OptimizationRevertService] Audit unavailable; preserving recovery-id path: {ex.Message}");
        }

        if (sourceReceipt is not null && !ReceiptMatchesMaterial(sourceReceipt, material))
        {
            return Error(operationId, "revert.audit-recovery-mismatch", "审计回执与恢复材料不匹配。");
        }

        return new ResolvedBinding(operation, material, null);
    }

    private bool HasDurableDependencies() =>
        _restoreStore is not null
        && _auditStore is not null
        && _recoveryRepository is not null
        && _orchestrator is not null
        && _confirmationPrompt is not null;

    private IOptimizationOperation? FindOperation(string operationId) =>
        _engine.Modules.SelectMany(static module => module.Operations)
            .FirstOrDefault(operation => string.Equals(operation.Id, operationId, StringComparison.OrdinalIgnoreCase));

    private bool HasUniqueOperation(string operationId) =>
        !string.IsNullOrWhiteSpace(operationId)
        && _engine.Modules.SelectMany(static module => module.Operations)
            .Count(operation => string.Equals(operation.Id, operationId, StringComparison.OrdinalIgnoreCase)) == 1;

    private async Task<OptimizationRecoveryPointer?> LoadPointerAsync(string operationId, CancellationToken cancellationToken)
    {
        var payload = await _restoreStore!.LoadSnapshotAsync(operationId, cancellationToken).ConfigureAwait(false);
        return payload is null ? null : JsonSerializer.Deserialize<OptimizationRecoveryPointer>(payload, JsonOptions);
    }

    private async Task<DurableStatusRead> LoadDurableStatusAsync(string operationId, CancellationToken cancellationToken)
    {
        if (_restoreStore is null)
        {
            return new DurableStatusRead(null, null, null);
        }

        try
        {
            var payload = await _restoreStore.LoadSnapshotAsync(StatusPrefix + operationId, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                return new DurableStatusRead(null, null, null);
            }

            var status = JsonSerializer.Deserialize<OptimizationRevertDurableStatus>(payload, JsonOptions);
            return status is null
                ? new DurableStatusRead(null, "revert.status-invalid", "durable 恢复状态为空，已安全阻断。")
                : new DurableStatusRead(status, null, null);
        }
        catch (JsonException ex)
        {
            return new DurableStatusRead(null, "revert.status-invalid", $"durable 恢复状态损坏，已安全阻断：{ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DurableStatusRead(null, "revert.status-read-failed", $"durable 恢复状态读取失败，已安全阻断：{ex.Message}");
        }
    }

    private async Task SaveDurableStatusAsync(
        string operationId,
        Guid recoveryId,
        OptimizationRevertAvailability availability,
        string reasonCode,
        string message,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PersistenceTimeout);
        var status = new OptimizationRevertDurableStatus
        {
            OperationId = operationId,
            RecoveryId = recoveryId,
            Availability = availability,
            ReasonCode = reasonCode,
            Message = message,
            UpdatedAtUtc = _timeProvider.GetUtcNow()
        };
        await _restoreStore!.SaveSnapshotAsync(
            StatusPrefix + operationId,
            JsonSerializer.Serialize(status, JsonOptions),
            timeout.Token).ConfigureAwait(false);
    }

    private async Task TryPersistInterruptedAsync(OptimizationRevertPlan plan, string reasonCode, string message)
    {
        try
        {
            await SaveDurableStatusAsync(
                plan.OperationId,
                plan.RecoveryId,
                OptimizationRevertAvailability.RestoringInterrupted,
                reasonCode,
                message,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The orchestrator recovery claim remains authoritative and fail-closed.
        }
    }

    private void RecordHistory(
        OptimizationRevertPlan plan,
        bool success,
        bool changed,
        OptimizationRevertAvailability availability,
        string reasonCode,
        string message,
        TimeSpan duration)
    {
        _history?.Record(new OperationHistoryRecord
        {
            ModuleKey = "durable-revert",
            ModuleName = "优化恢复",
            OperationId = plan.OperationId,
            OperationTitle = FindOperation(plan.OperationId)?.Title ?? plan.OperationId,
            Action = OperationAction.Restore,
            Success = success,
            Changed = changed,
            Message = message,
            Duration = duration,
            ReceiptId = plan.ReceiptId,
            RecoveryId = plan.RecoveryId,
            CapabilityId = plan.CapabilityId,
            CapabilityVersion = plan.DefinitionVersion,
            ResourceKey = plan.ResourceKey,
            ScopeKind = plan.Scope.Kind.ToString(),
            ScopeIdentity = plan.Scope.Identity,
            RecoveryIntegrityHash = plan.RecoveryIntegrityHash,
            RecoveryState = availability.ToString(),
            ReasonCode = reasonCode
        });
    }

    private static OptimizationRevertStatus ClassifyTerminal(OperationReceipt receipt)
    {
        if (receipt.Outcome is ChangeOutcome.RestoredVerified or ChangeOutcome.NoChange
            || string.Equals(receipt.ReasonCode, "restore.already-restored", StringComparison.Ordinal))
        {
            return Status(receipt.CapabilityId, OptimizationRevertAvailability.Restored, receipt.ReasonCode, receipt.Message, receipt.CompletedAtUtc);
        }

        if (receipt.Outcome is ChangeOutcome.RecoveryRequired or ChangeOutcome.AppliedButUnverified
            or ChangeOutcome.FailedPartial or ChangeOutcome.CancelledAfterPartialWrite
            || string.Equals(receipt.ReasonCode, "restore.already-restoring", StringComparison.Ordinal))
        {
            return Status(receipt.CapabilityId, OptimizationRevertAvailability.RestoringInterrupted, receipt.ReasonCode, "恢复结果未知或正在被另一实例处理，禁止自动重试；请人工核查。", receipt.CompletedAtUtc);
        }

        return Status(receipt.CapabilityId, OptimizationRevertAvailability.Available, receipt.ReasonCode, receipt.Message, receipt.CompletedAtUtc);
    }

    private static bool IsVerifiedRestoreExecution(OperationReceipt receipt) =>
        receipt.Outcome is ChangeOutcome.RestoredVerified or ChangeOutcome.NoChange;

    private static bool PointerMatchesMaterial(OptimizationRecoveryPointer pointer, string operationId, RecoveryMaterial material)
    {
        if (pointer.SchemaVersion <= 1)
        {
            return true;
        }

        return string.Equals(pointer.OperationId, operationId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pointer.CapabilityId, material.CapabilityId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pointer.DefinitionVersion, material.DefinitionVersion, StringComparison.Ordinal)
            && string.Equals(pointer.ResourceKey, material.ResourceKey, StringComparison.OrdinalIgnoreCase)
            && pointer.Scope == material.Scope
            && string.Equals(pointer.BeforeFingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
            && string.Equals(pointer.TargetFingerprint, material.TargetFingerprint, StringComparison.Ordinal);
    }

    private static bool ReceiptMatchesMaterial(OperationReceipt receipt, RecoveryMaterial material) =>
        receipt.RecoveryId == material.RecoveryId
        && receipt.PlanId == material.PlanId
        && string.Equals(receipt.CapabilityId, material.CapabilityId, StringComparison.OrdinalIgnoreCase)
        && receipt.Scope == material.Scope
        && string.Equals(receipt.BeforeFingerprint, material.BeforeFingerprint, StringComparison.Ordinal)
        && string.Equals(receipt.TargetFingerprint, material.TargetFingerprint, StringComparison.Ordinal);

    private static bool PlanMatchesMaterial(
        OptimizationRevertPlan plan,
        OptimizationRecoveryPointer pointer,
        RecoveryMaterial material) =>
        pointer.ReceiptId == plan.ReceiptId
        && pointer.RecoveryId == plan.RecoveryId
        && material.RecoveryId == plan.RecoveryId
        && string.Equals(material.CapabilityId, plan.CapabilityId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(material.DefinitionVersion, plan.DefinitionVersion, StringComparison.Ordinal)
        && string.Equals(material.ResourceKey, plan.ResourceKey, StringComparison.OrdinalIgnoreCase)
        && material.Scope == plan.Scope
        && string.Equals(material.BeforeFingerprint, plan.BeforeFingerprint, StringComparison.Ordinal)
        && string.Equals(material.TargetFingerprint, plan.TargetFingerprint, StringComparison.Ordinal)
        && string.Equals(material.IntegrityHash, plan.RecoveryIntegrityHash, StringComparison.Ordinal)
        && string.Equals(
            ComputePlanHash(plan.PlanId, plan.OperationId, plan.ReceiptId, material, plan.Risk, plan.PreparedAtUtc, plan.ExpiresAtUtc),
            plan.PlanHash,
            StringComparison.Ordinal);

    private static bool PlansEqual(OptimizationRevertPlan left, OptimizationRevertPlan right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left.PlanHash);
        var rightBytes = Encoding.UTF8.GetBytes(right.PlanHash);
        return left == right
            && leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string ComputePlanHash(
        Guid planId,
        string operationId,
        Guid receiptId,
        RecoveryMaterial material,
        ChangeRisk risk,
        DateTimeOffset preparedAt,
        DateTimeOffset expiresAt)
    {
        var canonical = string.Join('\u001F',
            planId.ToString("N"),
            operationId,
            receiptId.ToString("N"),
            material.RecoveryId.ToString("N"),
            material.CapabilityId,
            material.DefinitionVersion,
            material.ResourceKey,
            material.Scope.Kind.ToString(),
            material.Scope.Identity,
            material.BeforeFingerprint,
            material.TargetFingerprint,
            material.IntegrityHash,
            risk.ToString(),
            preparedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            expiresAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private void RemoveExpiredPlans(DateTimeOffset now)
    {
        foreach (var pair in _issuedPlans)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                _issuedPlans.TryRemove(pair.Key, out _);
            }
        }
    }

    private static ChangeRisk ToChangeRisk(OperationRiskLevel risk) => risk switch
    {
        OperationRiskLevel.Safe => ChangeRisk.LowReversible,
        OperationRiskLevel.Balanced => ChangeRisk.MediumRebootOrExperience,
        OperationRiskLevel.Extreme => ChangeRisk.HighSystem,
        _ => ChangeRisk.Experimental
    };

    private static OptimizationRevertStatus Status(
        string operationId,
        OptimizationRevertAvailability availability,
        string reasonCode,
        string message,
        DateTimeOffset observedAtUtc) =>
        new(operationId, availability, reasonCode, message, observedAtUtc);

    private ResolvedBinding Error(string operationId, string code, string message) =>
        new(null, null, Status(operationId, OptimizationRevertAvailability.Unrecoverable, code, message, _timeProvider.GetUtcNow()));

    private static string GetIconForCategory(string category) => category.ToLowerInvariant() switch
    {
        "network" or "网络" => "🌐",
        "service" or "服务" => "⚙️",
        "privacy" or "隐私" => "🔒",
        "security" or "安全" => "🛡️",
        "system" or "系统" => "💻",
        "power" or "电源" => "⚡",
        "visual" or "视觉" => "🎨",
        "gaming" or "游戏" => "🎮",
        "memory" or "内存" => "🧠",
        "storage" or "存储" => "💾",
        "input" or "输入" => "🖱️",
        _ => "↩️"
    };

    private sealed record ResolvedBinding(
        IOptimizationOperation? Operation,
        RecoveryMaterial? Material,
        OptimizationRevertStatus? Error);

    private sealed record DurableStatusRead(
        OptimizationRevertDurableStatus? Status,
        string? ReasonCode,
        string? Error);
}
