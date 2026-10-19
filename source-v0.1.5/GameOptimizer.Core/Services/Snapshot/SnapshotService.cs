using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Contracts.Changes;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Optimization;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Core.Utilities;
using Microsoft.Win32;

namespace GameOptimizer.Core.Services.Snapshot
{
    public class SnapshotService : ISnapshotService
    {
        private readonly string _snapshotDir;
        private readonly SnapshotFilePolicy _filePolicy;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly SnapshotRestoreCoordinator _restoreCoordinator;
        private readonly ISnapshotRestoreBatchStore _batchStore;
        private readonly IOptimizationOperationProvider? _operationProvider;
        private readonly IChangeOrchestrator? _changeOrchestrator;
        private readonly IChangeConfirmationAuthority? _confirmationAuthority;

        public SnapshotService() : this(new DenySnapshotRestoreAuthorizationGate(), null, null, null, null, null)
        {
        }

        public SnapshotService(
            ISnapshotRestoreAuthorizationGate authorizationGate,
            IChangeOrchestrator? changeOrchestrator = null,
            IChangeConfirmationAuthority? confirmationAuthority = null)
            : this(authorizationGate, changeOrchestrator, confirmationAuthority, null, null, null)
        {
        }

        internal SnapshotService(
            ISnapshotRestoreAuthorizationGate authorizationGate,
            IChangeOrchestrator? changeOrchestrator,
            IChangeConfirmationAuthority? confirmationAuthority,
            IOptimizationOperationProvider? operationProvider,
            IRecoveryClaimStore? claimStore,
            TimeProvider? timeProvider,
            string? snapshotDirectory = null)
        {
            _operationProvider = operationProvider;
            _filePolicy = new SnapshotFilePolicy(snapshotDirectory ?? AppPaths.SnapshotsDir);
            _snapshotDir = _filePolicy.Root;
            _batchStore = new FileSnapshotRestoreBatchStore(Path.Combine(_snapshotDir, "restore-batches"), timeProvider);
            _restoreCoordinator = new SnapshotRestoreCoordinator(
                authorizationGate,
                ReadRestorePreparationAsync,
                RequestRestoreConfirmationAsync,
                claimStore,
                _batchStore,
                timeProvider);
            _changeOrchestrator = changeOrchestrator;
            _confirmationAuthority = confirmationAuthority;
            
            ValidateSnapshotDirectory();
        }
        
        private void ValidateSnapshotDirectory()
        {
            try
            {
                if (!Directory.Exists(_snapshotDir))
                {
                    if (!SafeDirectoryOperations.TryCreateDirectory(_snapshotDir, out var error))
                    {
                        Debug.WriteLine($"[SnapshotService] Failed to create snapshot directory: {error}");
                        return;
                    }
                    Debug.WriteLine($"[SnapshotService] Created snapshot directory: {_snapshotDir}");
                }
                
                var testFile = Path.Combine(_snapshotDir, $".write_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                Debug.WriteLine("[SnapshotService] Snapshot directory is writable");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SnapshotService] WARNING: Snapshot directory validation failed: {ex.Message}");
            }
        }

        public async Task<OptimizationSnapshot> CreateSnapshotAsync(string name, string? description = null, CancellationToken ct = default)
        {
            var loc = LocalizationService.Instance;
            
            // P0-002: 检查磁盘空间（估算需要至少10MB空间用于快照）
            // 边界条件：验证快照名称
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(loc.Get("Snapshot.NameRequired"), nameof(name));
            
            // 边界条件：名称长度限制
            if (name.Length > 100)
                throw new ArgumentException(loc.Get("Snapshot.NameTooLong"), nameof(name));
            
            // 边界条件：过滤特殊字符（只允许字母、数字、中文、空格、下划线、连字符）
            var sanitizedName = SanitizeSnapshotName(name);
            if (string.IsNullOrWhiteSpace(sanitizedName))
                throw new ArgumentException(loc.Get("Snapshot.NameInvalid"), nameof(name));
            
            var existingSnapshots = await GetSnapshotsAsync();
            // Why: 重名时自动追加序号，而不是抛异常阻断备份流程
            // 用户快速点击或自动备份可能产生同秒名称
            if (existingSnapshots.Any(s => s.Name.Equals(sanitizedName, StringComparison.OrdinalIgnoreCase)))
            {
                var baseName = sanitizedName;
                var suffix = 2;
                while (existingSnapshots.Any(s => s.Name.Equals(sanitizedName, StringComparison.OrdinalIgnoreCase)))
                {
                    sanitizedName = $"{baseName}_{suffix}";
                    suffix++;
                    if (suffix > 100) break;
                }
            }

            var snapshot = new OptimizationSnapshot
            {
                Name = sanitizedName,
                Description = description ?? loc.Get("Snapshot.DefaultDesc", DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
                Version = 5
            };

            // V5 只记录已有唯一 typed Owner 的资源；未迁移域不再写入新快照。
            var typedStates = await Task.Run(
                () => (
                    Dword: CollectTypedRegistryDwordStates(ct),
                    Capabilities: CollectTypedRegistryCapabilityStates(ct)),
                ct).ConfigureAwait(false);
            snapshot.TypedRegistryDwordStates = typedStates.Dword;
            snapshot.TypedRegistryCapabilityStates = typedStates.Capabilities;

            // 计算校验和（使用新的确定性算法）
            snapshot.Checksum = SnapshotChecksumCalculator.ComputeChecksum(snapshot);

            // Reserve enough space for the exact serialized payload, its temporary copy and modest
            // filesystem/metadata overhead. This avoids a misleading fixed-size estimate.
            var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions).LongLength;
            var requiredBytes = checked(serializedBytes * 2 + 64 * 1024);
            var diskCheck = SystemResourceChecker.CheckDiskSpace(_snapshotDir, requiredBytes);
            if (!diskCheck.Success)
            {
                Debug.WriteLine($"[SnapshotService] Disk space check failed: {diskCheck.ErrorMessage}");
                throw new IOException(diskCheck.ErrorMessage ?? loc.Get("Backup.Error.DiskFull"));
            }

            // 验证快照完整性
            var (isValid, validationError) = SnapshotChecksumCalculator.ValidateSnapshot(snapshot);
            if (!isValid)
            {
                throw new InvalidOperationException($"Snapshot data invalid: {validationError}");
            }

            try
            {
                if (!Directory.Exists(_snapshotDir))
                {
                    if (!SafeDirectoryOperations.TryCreateDirectory(_snapshotDir, out var error))
                    {
                        var userDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var displayPath = _snapshotDir.Replace(userDataPath, "%LocalAppData%");
                        Debug.WriteLine($"[SnapshotService] Failed to create directory: {error}");
                        throw new IOException(
                            loc.Get("Backup.Error.CannotCreateDirectory", displayPath) + $"\n{error}");
                    }
                    Debug.WriteLine($"[SnapshotService] Created snapshot directory: {_snapshotDir}");
                }
            }
            catch (IOException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var userDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var displayPath = _snapshotDir.Replace(userDataPath, "%LocalAppData%");
                Debug.WriteLine($"[SnapshotService] Failed to create directory: {ex.Message}");
                throw new IOException(
                    loc.Get("Backup.Error.CannotCreateDirectory", displayPath), ex);
            }
            
            var filePath = GetSnapshotPath(snapshot.Id);
            
            // 使用新的安全文件写入器（带文件锁和原子操作）
            var (writeSuccess, writeError, writtenSnapshot) = await SnapshotFileWriter.WriteSnapshotSafelyAsync(filePath, snapshot, ct);
            
            if (!writeSuccess || writtenSnapshot == null)
            {
                var userDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var displayPath = _snapshotDir.Replace(userDataPath, "%LocalAppData%");
                
                var errorMsg = loc.Get("Snapshot.WriteVerifyFailed") + 
                    $"\nSnapshot dir: {displayPath}" +
                    $"\nDetails: {writeError ?? "Unknown error"}" +
                    $"\nSuggestion: 1. Check disk space 2. Temporarily disable antivirus 3. Check directory permissions";
                
                throw new IOException(errorMsg);
            }
            
            Debug.WriteLine($"[SnapshotService] Snapshot created successfully: {snapshot.Name} ({snapshot.Id})");
            
            return snapshot;
        }

        public Task<SnapshotCoverageReport> GetCoverageReportAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var manifest = _operationProvider is null
                ? Array.Empty<OptimizationOperationManifestEntry>()
                : OptimizationProviderRuntimeManifest.Create(_operationProvider).ToArray();
            var operationIds = manifest.Select(entry => entry.OperationId).ToArray();
            var typedExecutableIds = manifest
                .Where(entry => entry.Route == OptimizationOperationRoute.TypedExecutable)
                .Select(entry => entry.OperationId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var managedFailClosedIds = manifest
                .Where(entry => entry.Route == OptimizationOperationRoute.ManagedFailClosed)
                .Select(entry => entry.OperationId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(SnapshotCoverageAnalyzer.Analyze(
                operationIds,
                TypedRegistryCapabilityIds,
                typedExecutableIds,
                managedFailClosedIds,
                TypedRegistryDwordDefinitions.Length
                    + TypedRegistryCapabilityDefinitions.Sum(definition => definition.Values.Length)));
        }

        private static List<SnapshotTypedRegistryDwordState> CollectTypedRegistryDwordStates(CancellationToken ct)
        {
            var states = new List<SnapshotTypedRegistryDwordState>(TypedRegistryDwordDefinitions.Length);
            foreach (var definition in TypedRegistryDwordDefinitions)
            {
                ct.ThrowIfCancellationRequested();
                using var key = Registry.CurrentUser.OpenSubKey(definition.SubKeyPath, writable: false);
                var exists = key?.GetValueNames().Contains(definition.ValueName, StringComparer.OrdinalIgnoreCase) == true;
                int? value = null;
                if (exists)
                {
                    if (key!.GetValueKind(definition.ValueName) != RegistryValueKind.DWord
                        || key.GetValue(definition.ValueName) is not int dword)
                    {
                        throw new InvalidDataException($"Typed snapshot source is not a DWORD: {definition.SourceKey}");
                    }
                    value = dword;
                }
                states.Add(new SnapshotTypedRegistryDwordState(
                    definition.CapabilityId,
                    definition.SourceKey,
                    exists,
                    value));
            }
            return states;
        }

        private static List<SnapshotTypedRegistryCapabilityState> CollectTypedRegistryCapabilityStates(CancellationToken ct)
        {
            var capabilities = new List<SnapshotTypedRegistryCapabilityState>(TypedRegistryCapabilityDefinitions.Length);
            foreach (var definition in TypedRegistryCapabilityDefinitions)
            {
                ct.ThrowIfCancellationRequested();
                using var key = Registry.CurrentUser.OpenSubKey(definition.SubKeyPath, writable: false);
                var names = key?.GetValueNames().ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var values = new List<SnapshotTypedRegistryValueState>(definition.Values.Length);
                foreach (var expected in definition.Values)
                {
                    var exists = names.Contains(expected.ValueName);
                    string? stringValue = null;
                    int? dwordValue = null;
                    if (exists)
                    {
                        var expectedKind = expected.Kind == SnapshotRegistryScalarKind.String
                            ? RegistryValueKind.String
                            : RegistryValueKind.DWord;
                        if (key!.GetValueKind(expected.ValueName) != expectedKind)
                            throw new InvalidDataException($"Typed snapshot source kind mismatch: {definition.SubKeyPath}\\{expected.ValueName}");
                        var raw = key.GetValue(expected.ValueName);
                        if (expected.Kind == SnapshotRegistryScalarKind.String && raw is string text) stringValue = text;
                        else if (expected.Kind == SnapshotRegistryScalarKind.DWord && raw is int number) dwordValue = number;
                        else throw new InvalidDataException($"Typed snapshot source value is invalid: {definition.SubKeyPath}\\{expected.ValueName}");
                    }
                    values.Add(new SnapshotTypedRegistryValueState(
                        expected.ValueName, expected.Kind, exists, stringValue, dwordValue));
                }
                capabilities.Add(new SnapshotTypedRegistryCapabilityState(
                    definition.CapabilityId, definition.SubKeyPath, values.AsReadOnly()));
            }
            return capabilities;
        }


        public Task<SnapshotRestorePlan> PrepareRestoreAsync(string snapshotId, SnapshotRestoreKind kind, CancellationToken ct = default) =>
            _restoreCoordinator.PrepareAsync(snapshotId, kind, ct);

        private async Task<bool> RequestRestoreConfirmationAsync(
            SnapshotRestorePlan plan,
            CancellationToken cancellationToken)
        {
            if (_confirmationAuthority is null)
            {
                return false;
            }

            var scope = TargetScope.CurrentUser(
                WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Current user SID is unavailable."));
            var confirmationChange = new PreparedCapabilityChange(
                "snapshot.restore.batch",
                "2.0.0",
                scope,
                plan.SnapshotIdentitySha256,
                plan.SnapshotIdentitySha256,
                plan.PlanSha256,
                plan.PlanSha256,
                $"snapshot.restore.batch:{plan.SnapshotId}");
            var confirmationPlan = new PreparedChangePlan(
                plan.PlanId,
                confirmationChange,
                plan.PlanSha256,
                plan.CreatedAtUtc,
                plan.ExpiresAtUtc,
                RequiresAdministrator: false,
                RequiresExplicitConfirmation: true,
                RequiresRecoveryMaterial: true,
                ChangeRisk.HighSystem);
            var confirmation = await _confirmationAuthority.RequestAsync(
                confirmationPlan,
                "恢复系统快照",
                $"恢复快照 {plan.SnapshotId} 的 {plan.Steps.Count(step => step.Supported)} 个 typed 资源。确认仅绑定当前计划哈希，过期或篡改后无效。",
                cancellationToken).ConfigureAwait(false);
            return confirmation is not null
                && _confirmationAuthority.TryConsume(confirmationPlan, confirmation);
        }

        public Task<SnapshotRestoreResult> ExecuteRestoreAsync(SnapshotRestorePlan plan, CancellationToken ct = default) =>
            _restoreCoordinator.ExecuteAsync(
                plan,
                SnapshotRestoreKind.Normal,
                token => RestoreSnapshotCoreAsync(plan, token),
                message => new SnapshotRestoreResult
                {
                    Success = false,
                    BatchPlanId = plan.PlanId,
                    OutcomeReasonCode = "snapshot.restore-blocked",
                    Message = message,
                    Errors = { message }
                },
                ct);

        public Task<RegistryBackupRestoreResult> ExecuteRegistryRestoreAsync(SnapshotRestorePlan plan, CancellationToken ct = default) =>
            _restoreCoordinator.ExecuteAsync(
                plan,
                SnapshotRestoreKind.DeepRegistry,
                token => Task.FromResult(new RegistryBackupRestoreResult
                {
                    Success = false,
                    BatchPlanId = plan.PlanId,
                    MutationStatus = SnapshotMutationStatus.None,
                    FailedFiles = { "snapshot.deep-registry-typed-owner-unavailable" }
                }),
                message => new RegistryBackupRestoreResult
                {
                    Success = false,
                    BatchPlanId = plan.PlanId,
                    MutationStatus = SnapshotMutationStatus.None,
                    FailedFiles = { message }
                },
                ct);

        public Task<SnapshotRestoreRecoveryReport> ReconcileRestoreAsync(
            Guid planId,
            string planSha256,
            CancellationToken ct = default) =>
            _restoreCoordinator.ReconcileAsync(planId, planSha256, ct);

        public Task<SnapshotRestoreRecoveryReport> ResolveInterruptedRestoreAsync(
            Guid planId,
            string planSha256,
            SnapshotRestoreManualDecision decision,
            CancellationToken ct = default) =>
            _restoreCoordinator.ResolveManualAsync(planId, planSha256, decision, ct);

        public Task<SnapshotRestoreResult> RestoreSnapshotAsync(string snapshotId, CancellationToken ct = default) =>
            Task.FromResult(new SnapshotRestoreResult
            {
                Success = false,
                Message = "必须先准备、确认并领取类型化快照恢复计划。",
                Errors = { "snapshot.restore-plan-required" }
            });

        private async Task<SnapshotRestoreResult> RestoreSnapshotCoreAsync(
            SnapshotRestorePlan plan,
            CancellationToken ct)
        {
            var loc = LocalizationService.Instance;
            var result = new SnapshotRestoreResult
            {
                BatchPlanId = plan.PlanId,
                BatchState = SnapshotRestoreBatchState.Restoring,
                OutcomeReasonCode = "snapshot.restore-started"
            };
            var snapshot = await GetSnapshotAsync(plan.SnapshotId);
            if (snapshot == null)
            {
                result.Message = loc.Get("Snapshot.NotFound");
                result.OutcomeReasonCode = "snapshot.not-found";
                return result;
            }

            // 验证校验和（使用新的确定性算法）
            if (!string.IsNullOrEmpty(snapshot.Checksum))
            {
                var currentChecksum = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
                if (snapshot.Checksum != currentChecksum)
                {
                    result.Message = loc.Get("Snapshot.ChecksumFailed");
                    result.OutcomeReasonCode = "snapshot.checksum-mismatch";
                    result.Errors.Add($"Checksum mismatch: expected {snapshot.Checksum}, got {currentChecksum}");
                    
                    // 记录详细差异用于诊断
                    Debug.WriteLine($"[SnapshotService] 校验和不匹配: 预期={snapshot.Checksum}, 实际={currentChecksum}");
                    return result;
                }
                Debug.WriteLine($"[SnapshotService] 快照校验和验证通过: {currentChecksum}");
            }

            if (!IsTypedRestoreSchemaVersionSupported(snapshot.Version)
                || snapshot.TypedRegistryDwordStates.Count == 0)
            {
                result.Message = "该快照缺少 V4 类型化恢复清单，旧格式不能安全猜测恢复目标。";
                result.OutcomeReasonCode = "snapshot.typed-manifest-required";
                result.Errors.Add("snapshot.typed-manifest-required");
                return result;
            }

            var batchExecution = new SnapshotTypedBatchExecution(
                plan,
                result,
                _changeOrchestrator,
                _batchStore);
            if (await RecordUnsupportedBeforeWriteAsync(
                    plan,
                    result,
                    batchExecution).ConfigureAwait(false))
            {
                return result;
            }

            var currentUserScope = TargetScope.CurrentUser(
                WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Current user SID is unavailable."));
            if (!await RestoreTypedRegistryDwordsAsync(
                    snapshot,
                    result,
                    _changeOrchestrator,
                    _confirmationAuthority,
                    currentUserScope,
                    batchExecution,
                    ct).ConfigureAwait(false))
            {
                await batchExecution.CompensateAsync().ConfigureAwait(false);
                result.Success = false;
                result.OutcomeReasonCode = result.CompensationStatus == SnapshotCompensationStatus.VerifiedBeforeState
                    ? "snapshot.failed-no-change"
                    : "snapshot.typed-dword-batch-failed";
                result.Message = result.CompensationStatus == SnapshotCompensationStatus.VerifiedBeforeState
                    ? "类型化恢复失败，但已验证逆序补偿回到 before 状态；批次未恢复成功。"
                    : "类型化注册表恢复失败或取消，已停止后续恢复；已执行项可按 recovery receipt 恢复。";
                return result;
            }
            if (snapshot.Version >= 5
                && !await RestoreTypedRegistryCapabilitiesAsync(
                    snapshot,
                    result,
                    _changeOrchestrator,
                    _confirmationAuthority,
                    currentUserScope,
                    batchExecution,
                    ct).ConfigureAwait(false))
            {
                await batchExecution.CompensateAsync().ConfigureAwait(false);
                result.Success = false;
                result.OutcomeReasonCode = result.CompensationStatus == SnapshotCompensationStatus.VerifiedBeforeState
                    ? "snapshot.failed-no-change"
                    : "snapshot.v5-typed-batch-failed";
                result.Message = result.CompensationStatus == SnapshotCompensationStatus.VerifiedBeforeState
                    ? "V5 类型化恢复失败，但已验证逆序补偿回到 before 状态；批次未恢复成功。"
                    : "V5 类型化注册表恢复失败或取消，已停止后续恢复；此前已执行项保留 recovery receipt。";
                return result;
            }

            result.Success = result.FailedCount == 0
                && result.Warnings.Count == 0
                && result.UnsupportedCount == 0;
            result.MutationStatus = result.TypedReceipts.Any(receipt => receipt.MutationOccurred)
                ? SnapshotMutationStatus.Known
                : SnapshotMutationStatus.None;
            result.OutcomeReasonCode = result.Success
                ? "snapshot.restored-verified"
                : "snapshot.restore-not-verified";
            result.Message = result.Success
                ? loc.Get("Snapshot.RestoreSuccess", result.RestoredCount)
                : loc.Get("Snapshot.RestorePartial", result.RestoredCount, result.FailedCount);
            
            if (result.RequiresReboot)
                result.Message += loc.Get("Snapshot.NeedReboot");

            return result;
        }

        internal static async Task<bool> RecordUnsupportedBeforeWriteAsync(
            SnapshotRestorePlan plan,
            SnapshotRestoreResult result,
            SnapshotTypedBatchExecution batchExecution)
        {
            var unsupported = plan.Steps.Where(step => !step.Supported).ToArray();
            if (unsupported.Length == 0)
            {
                return false;
            }

            foreach (var step in unsupported)
            {
                await batchExecution.RecordUnsupportedAsync(step).ConfigureAwait(false);
            }
            result.UnsupportedCount = unsupported.Length;
            result.SkippedCount += unsupported.Length;
            result.MutationStatus = SnapshotMutationStatus.None;
            result.CompensationStatus = SnapshotCompensationStatus.NotRequired;
            result.Success = false;
            result.OutcomeReasonCode = "snapshot.unsupported-resources";
            result.Message = $"恢复计划包含 {unsupported.Length} 个没有唯一 typed Owner 的资源；已在任何写入前阻断，本批次未标记为已恢复。";
            result.Warnings.Add("Unsupported 资源已记录为 Skipped；没有执行任何 typed 或 legacy 写入。");
            return true;
        }

        internal static async Task<bool> RestoreTypedRegistryDwordsAsync(
            OptimizationSnapshot snapshot,
            SnapshotRestoreResult result,
            IChangeOrchestrator? changeOrchestrator,
            IChangeConfirmationAuthority? confirmationAuthority,
            TargetScope scope,
            CancellationToken cancellationToken) =>
            await RestoreTypedRegistryDwordsAsync(
                snapshot,
                result,
                changeOrchestrator,
                confirmationAuthority,
                scope,
                null,
                cancellationToken).ConfigureAwait(false);

        internal static async Task<bool> RestoreTypedRegistryDwordsAsync(
            OptimizationSnapshot snapshot,
            SnapshotRestoreResult result,
            IChangeOrchestrator? changeOrchestrator,
            IChangeConfirmationAuthority? confirmationAuthority,
            TargetScope scope,
            SnapshotTypedBatchExecution? batchExecution,
            CancellationToken cancellationToken)
        {
            if (changeOrchestrator is null || confirmationAuthority is null)
            {
                result.Errors.Add("snapshot.typed-orchestrator-missing");
                return false;
            }

            var manifest = snapshot.TypedRegistryDwordStates;
            if (manifest.Count != TypedRegistryDwordDefinitions.Length
                || manifest.Select(item => item.CapabilityId).Distinct(StringComparer.Ordinal).Count() != manifest.Count)
            {
                result.Errors.Add("snapshot.typed-manifest-incomplete");
                return false;
            }

            var requests = new List<SnapshotTypedRestoreRequest>(TypedRegistryDwordDefinitions.Length);
            foreach (var definition in TypedRegistryDwordDefinitions)
            {
                var item = manifest.SingleOrDefault(candidate =>
                    string.Equals(candidate.CapabilityId, definition.CapabilityId, StringComparison.Ordinal));
                if (item is null
                    || !string.Equals(item.SourceKey, definition.SourceKey, StringComparison.Ordinal)
                    || (item.ValueExists && item.Value is null)
                    || (!item.ValueExists && item.Value is not null))
                {
                    result.Errors.Add($"snapshot.typed-entry-invalid:{definition.CapabilityId}");
                    return false;
                }

                requests.Add(new SnapshotTypedRestoreRequest(
                    item.CapabilityId,
                    JsonSerializer.Serialize(new SnapshotRegistryDwordTarget(
                        item.ValueExists ? 0 : 1,
                        item.Value))));
            }

            return await ExecuteTypedRestoreRequestsAsync(
                requests,
                result,
                changeOrchestrator,
                confirmationAuthority,
                scope,
                batchExecution,
                cancellationToken).ConfigureAwait(false);
        }

        internal static async Task<bool> RestoreTypedRegistryCapabilitiesAsync(
            OptimizationSnapshot snapshot,
            SnapshotRestoreResult result,
            IChangeOrchestrator? changeOrchestrator,
            IChangeConfirmationAuthority? confirmationAuthority,
            TargetScope scope,
            CancellationToken cancellationToken) =>
            await RestoreTypedRegistryCapabilitiesAsync(
                snapshot,
                result,
                changeOrchestrator,
                confirmationAuthority,
                scope,
                null,
                cancellationToken).ConfigureAwait(false);

        internal static async Task<bool> RestoreTypedRegistryCapabilitiesAsync(
            OptimizationSnapshot snapshot,
            SnapshotRestoreResult result,
            IChangeOrchestrator? changeOrchestrator,
            IChangeConfirmationAuthority? confirmationAuthority,
            TargetScope scope,
            SnapshotTypedBatchExecution? batchExecution,
            CancellationToken cancellationToken)
        {
            if (changeOrchestrator is null || confirmationAuthority is null)
            {
                result.Errors.Add("snapshot.typed-orchestrator-missing");
                return false;
            }

            var manifest = snapshot.TypedRegistryCapabilityStates;
            if (manifest.Count != TypedRegistryCapabilityDefinitions.Length
                || manifest.Select(item => item.CapabilityId).Distinct(StringComparer.Ordinal).Count() != manifest.Count)
            {
                result.Errors.Add("snapshot.v5-typed-manifest-incomplete");
                return false;
            }

            var requests = new List<SnapshotTypedRestoreRequest>(TypedRegistryCapabilityDefinitions.Length);
            foreach (var definition in TypedRegistryCapabilityDefinitions)
            {
                var item = manifest.SingleOrDefault(candidate =>
                    string.Equals(candidate.CapabilityId, definition.CapabilityId, StringComparison.Ordinal));
                if (!TryBuildTypedCapabilityRequest(definition, item, out var request, out var error))
                {
                    result.Errors.Add(error);
                    return false;
                }
                requests.Add(request!);
            }

            return await ExecuteTypedRestoreRequestsAsync(
                requests,
                result,
                changeOrchestrator,
                confirmationAuthority,
                scope,
                batchExecution,
                cancellationToken).ConfigureAwait(false);
        }

        private static bool TryBuildTypedCapabilityRequest(
            TypedRegistryCapabilityDefinition definition,
            SnapshotTypedRegistryCapabilityState? item,
            out SnapshotTypedRestoreRequest? request,
            out string error)
        {
            request = null;
            error = $"snapshot.v5-typed-entry-invalid:{definition.CapabilityId}";
            if (item is null
                || !string.Equals(item.SubKeyPath, definition.SubKeyPath, StringComparison.Ordinal)
                || item.Values.Count != definition.Values.Length
                || item.Values.Select(value => value.ValueName).Distinct(StringComparer.Ordinal).Count() != item.Values.Count)
                return false;

            var targetValues = new List<SnapshotRegistryTypedTargetValue>(definition.Values.Length);
            foreach (var expected in definition.Values)
            {
                var value = item.Values.SingleOrDefault(candidate =>
                    string.Equals(candidate.ValueName, expected.ValueName, StringComparison.Ordinal));
                if (value is null
                    || value.Kind != expected.Kind
                    || (value.ValueExists && value.Kind == SnapshotRegistryScalarKind.String
                        && (value.StringValue is null || value.DwordValue is not null))
                    || (value.ValueExists && value.Kind == SnapshotRegistryScalarKind.DWord
                        && (value.DwordValue is null || value.StringValue is not null))
                    || (!value.ValueExists && (value.StringValue is not null || value.DwordValue is not null)))
                    return false;

                targetValues.Add(new SnapshotRegistryTypedTargetValue(
                    value.ValueName,
                    (int)value.Kind,
                    value.StringValue,
                    value.DwordValue,
                    value.ValueExists ? 0 : 1));
            }

            var targetPayload = definition.Values.Length == 1
                ? JsonSerializer.Serialize(new SnapshotRegistryStringTarget(
                    targetValues[0].Action,
                    targetValues[0].StringValue,
                    false))
                : JsonSerializer.Serialize(new SnapshotRegistryValueSetTarget(targetValues.AsReadOnly()));
            request = new SnapshotTypedRestoreRequest(definition.CapabilityId, targetPayload);
            error = string.Empty;
            return true;
        }

        private static async Task<bool> ExecuteTypedRestoreRequestsAsync(
            IReadOnlyList<SnapshotTypedRestoreRequest> requests,
            SnapshotRestoreResult result,
            IChangeOrchestrator changeOrchestrator,
            IChangeConfirmationAuthority confirmationAuthority,
            TargetScope scope,
            SnapshotTypedBatchExecution? batchExecution,
            CancellationToken cancellationToken)
        {
            foreach (var item in requests)
            {
                if (batchExecution is not null)
                {
                    await batchExecution.RecordExecutingAsync(item.CapabilityId).ConfigureAwait(false);
                }

                PreparationResult preparation;
                try
                {
                    preparation = await changeOrchestrator.PrepareAsync(
                        new ChangeRequest(item.CapabilityId, scope, item.TargetPayload),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    var receipt = SyntheticTypedReceipt(item.CapabilityId, "CancelledBeforeWrite", "cancelled.prepare", "准备期间取消。");
                    result.TypedReceipts.Add(receipt);
                    if (batchExecution is not null)
                    {
                        await batchExecution.RecordSyntheticAsync(
                            item.CapabilityId,
                            SnapshotRestoreStepState.Cancelled,
                            SnapshotMutationStatus.None,
                            receipt).ConfigureAwait(false);
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    var receipt = SyntheticTypedReceipt(item.CapabilityId, "PrepareFailed", "snapshot.prepare-failed", ex.Message);
                    result.TypedReceipts.Add(receipt);
                    result.Errors.Add($"{item.CapabilityId}: {ex.Message}");
                    if (batchExecution is not null)
                    {
                        await batchExecution.RecordSyntheticAsync(
                            item.CapabilityId,
                            SnapshotRestoreStepState.Failed,
                            SnapshotMutationStatus.None,
                            receipt).ConfigureAwait(false);
                    }
                    return false;
                }

                if (preparation.Status == ChangePreparationStatus.NoChange)
                {
                    var receipt = SyntheticTypedReceipt(item.CapabilityId, "NoChange", preparation.ReasonCode, preparation.Message);
                    result.TypedReceipts.Add(receipt);
                    if (batchExecution is not null)
                    {
                        await batchExecution.RecordSyntheticAsync(
                            item.CapabilityId,
                            SnapshotRestoreStepState.NoChange,
                            SnapshotMutationStatus.None,
                            receipt).ConfigureAwait(false);
                    }
                    result.SkippedCount++;
                    continue;
                }
                if (preparation.Status != ChangePreparationStatus.Ready || preparation.Plan is null)
                {
                    var receipt = SyntheticTypedReceipt(item.CapabilityId, "Blocked", preparation.ReasonCode, preparation.Message);
                    result.TypedReceipts.Add(receipt);
                    result.Errors.Add($"{item.CapabilityId}: {preparation.Message}");
                    if (batchExecution is not null)
                    {
                        await batchExecution.RecordSyntheticAsync(
                            item.CapabilityId,
                            SnapshotRestoreStepState.Failed,
                            SnapshotMutationStatus.None,
                            receipt).ConfigureAwait(false);
                    }
                    return false;
                }

                ConfirmationToken? confirmation;
                try
                {
                    confirmation = preparation.Plan.RequiresExplicitConfirmation
                        ? await confirmationAuthority.RequestAsync(
                            preparation.Plan,
                            "恢复快照注册表设置",
                            $"恢复 {item.CapabilityId} 到快照记录值。此确认仅绑定当前计划。",
                            cancellationToken).ConfigureAwait(false)
                        : ConfirmationToken.ForPreparedPlan(preparation.Plan);
                }
                catch (OperationCanceledException)
                {
                    var receipt = SyntheticTypedReceipt(item.CapabilityId, "CancelledBeforeWrite", "cancelled.confirmation", "确认期间取消。");
                    result.TypedReceipts.Add(receipt);
                    if (batchExecution is not null)
                    {
                        await batchExecution.RecordSyntheticAsync(
                            item.CapabilityId,
                            SnapshotRestoreStepState.Cancelled,
                            SnapshotMutationStatus.None,
                            receipt).ConfigureAwait(false);
                    }
                    return false;
                }

                if (confirmation is null)
                {
                    var receipt = SyntheticTypedReceipt(item.CapabilityId, "Denied", "confirmation.denied", "用户未确认或确认计划已失效。");
                    result.TypedReceipts.Add(receipt);
                    if (batchExecution is not null)
                    {
                        await batchExecution.RecordSyntheticAsync(
                            item.CapabilityId,
                            SnapshotRestoreStepState.Failed,
                            SnapshotMutationStatus.None,
                            receipt).ConfigureAwait(false);
                    }
                    return false;
                }

                BatchReceipt batchReceipt;
                try
                {
                    batchReceipt = await changeOrchestrator.ExecuteAsync(preparation.Plan, confirmation, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    var receipt = SyntheticTypedReceipt(item.CapabilityId, "CancelledAfterUnknownWrite", "cancelled.execute", "执行期间取消，最终写入状态未知，需人工恢复。");
                    result.TypedReceipts.Add(receipt);
                    result.MutationStatus = SnapshotMutationStatus.Unknown;
                    if (batchExecution is not null)
                    {
                        await batchExecution.RecordSyntheticAsync(
                            item.CapabilityId,
                            SnapshotRestoreStepState.Unknown,
                            SnapshotMutationStatus.Unknown,
                            receipt).ConfigureAwait(false);
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    var receipt = SyntheticTypedReceipt(item.CapabilityId, "ExecutionUnknown", "snapshot.execute-exception", ex.Message);
                    result.TypedReceipts.Add(receipt);
                    result.Errors.Add($"{item.CapabilityId}: {ex.Message}");
                    result.MutationStatus = SnapshotMutationStatus.Unknown;
                    if (batchExecution is not null)
                    {
                        await batchExecution.RecordSyntheticAsync(
                            item.CapabilityId,
                            SnapshotRestoreStepState.Unknown,
                            SnapshotMutationStatus.Unknown,
                            receipt).ConfigureAwait(false);
                    }
                    return false;
                }

                var typedReceipt = new SnapshotTypedRestoreReceipt(
                    batchReceipt.Operation.CapabilityId,
                    batchReceipt.Operation.ReceiptId,
                    batchReceipt.Operation.RecoveryId,
                    batchReceipt.Operation.Outcome.ToString(),
                    batchReceipt.Operation.ReasonCode,
                    batchReceipt.Operation.Message,
                    batchReceipt.Operation.MutationOccurred);
                result.TypedReceipts.Add(typedReceipt);
                if (batchExecution is not null)
                {
                    await batchExecution.RecordOperationReceiptAsync(batchReceipt).ConfigureAwait(false);
                }

                if (batchReceipt.Outcome == ChangeOutcome.SucceededVerified
                    && batchReceipt.Operation.MutationOccurred
                    && !batchReceipt.Operation.RecoveryId.HasValue)
                {
                    result.FailedCount++;
                    result.MutationStatus = SnapshotMutationStatus.Unknown;
                    result.Errors.Add($"{item.CapabilityId}: verified mutation has no durable recovery material.");
                    return false;
                }

                if (batchReceipt.Outcome is ChangeOutcome.SucceededVerified or ChangeOutcome.NoChange)
                {
                    if (batchReceipt.Outcome == ChangeOutcome.SucceededVerified) result.RestoredCount++;
                    else result.SkippedCount++;
                    continue;
                }

                result.FailedCount++;
                if (batchReceipt.Operation.MutationOccurred)
                {
                    result.MutationStatus = SnapshotMutationStatus.Known;
                }
                result.Errors.Add($"{item.CapabilityId}: {batchReceipt.Operation.Message}");
                return false;
            }
            return true;
        }

        private static SnapshotTypedRestoreReceipt SyntheticTypedReceipt(
            string capabilityId, string outcome, string reasonCode, string message) =>
            new(capabilityId, Guid.Empty, null, outcome, reasonCode, message, false);

        internal sealed class SnapshotTypedBatchExecution
        {
            private readonly SnapshotRestorePlan _plan;
            private readonly SnapshotRestoreResult _result;
            private readonly IChangeOrchestrator? _orchestrator;
            private readonly ISnapshotRestoreBatchStore _store;
            private readonly List<BatchReceipt> _recoverable = [];

            internal SnapshotTypedBatchExecution(
                SnapshotRestorePlan plan,
                SnapshotRestoreResult result,
                IChangeOrchestrator? orchestrator,
                ISnapshotRestoreBatchStore store)
            {
                _plan = plan;
                _result = result;
                _orchestrator = orchestrator;
                _store = store;
            }

            internal Task RecordExecutingAsync(string capabilityId) =>
                AppendAsync(
                    FindStep(capabilityId).StepId,
                    capabilityId,
                    SnapshotRestoreStepState.Executing,
                    Guid.Empty,
                    null,
                    SnapshotMutationStatus.None,
                    "snapshot.step-executing",
                    "typed Owner step entered execution.");

            internal async Task RecordOperationReceiptAsync(BatchReceipt receipt)
            {
                var operation = receipt.Operation;
                var state = operation.Outcome switch
                {
                    ChangeOutcome.SucceededVerified => SnapshotRestoreStepState.RestoredVerified,
                    ChangeOutcome.NoChange => SnapshotRestoreStepState.NoChange,
                    _ => SnapshotRestoreStepState.Failed
                };
                var mutation = operation.MutationOccurred
                    ? SnapshotMutationStatus.Known
                    : SnapshotMutationStatus.None;
                await AppendAsync(
                    FindStep(operation.CapabilityId).StepId,
                    operation.CapabilityId,
                    state,
                    operation.ReceiptId,
                    operation.RecoveryId,
                    mutation,
                    operation.ReasonCode,
                    operation.Message).ConfigureAwait(false);
                if (operation.MutationOccurred)
                {
                    _result.MutationStatus = SnapshotMutationStatus.Known;
                }
                if (operation.MutationOccurred && operation.RecoveryId.HasValue)
                {
                    _recoverable.Add(receipt);
                }
            }

            internal Task RecordSyntheticAsync(
                string capabilityId,
                SnapshotRestoreStepState state,
                SnapshotMutationStatus mutationStatus,
                SnapshotTypedRestoreReceipt receipt) =>
                AppendAsync(
                    FindStep(capabilityId).StepId,
                    capabilityId,
                    state,
                    receipt.ReceiptId,
                    receipt.RecoveryId,
                    mutationStatus,
                    receipt.ReasonCode,
                    receipt.Message);

            internal Task RecordUnsupportedAsync(SnapshotRestoreStepPlan step) =>
                AppendAsync(
                    step.StepId,
                    null,
                    SnapshotRestoreStepState.Unsupported,
                    Guid.Empty,
                    null,
                    SnapshotMutationStatus.None,
                    step.ReasonCode,
                    "No unique typed Owner exists; the resource was skipped without writing.");

            internal async Task CompensateAsync()
            {
                if (_recoverable.Count == 0)
                {
                    return;
                }
                if (_orchestrator is null)
                {
                    _result.CompensationStatus = SnapshotCompensationStatus.FailedOrUnknown;
                    _result.MutationStatus = SnapshotMutationStatus.Unknown;
                    return;
                }

                var compensationVerified = _result.MutationStatus != SnapshotMutationStatus.Unknown;
                foreach (var applied in _recoverable.AsEnumerable().Reverse())
                {
                    var operation = applied.Operation;
                    var step = FindStep(operation.CapabilityId);
                    await AppendAsync(
                        step.StepId,
                        operation.CapabilityId,
                        SnapshotRestoreStepState.Compensating,
                        operation.ReceiptId,
                        operation.RecoveryId,
                        SnapshotMutationStatus.Known,
                        "snapshot.compensation-started",
                        "Reverse compensation started.").ConfigureAwait(false);

                    try
                    {
                        var compensation = await _orchestrator.RestoreAsync(
                            new RestoreRequest(operation.ReceiptId, operation.RecoveryId),
                            CancellationToken.None).ConfigureAwait(false);
                        var restored = compensation.Outcome is ChangeOutcome.RestoredVerified
                            or ChangeOutcome.SucceededVerified
                            or ChangeOutcome.NoChange;
                        var typed = new SnapshotTypedRestoreReceipt(
                            compensation.Operation.CapabilityId,
                            compensation.Operation.ReceiptId,
                            compensation.Operation.RecoveryId,
                            compensation.Operation.Outcome.ToString(),
                            compensation.Operation.ReasonCode,
                            compensation.Operation.Message,
                            compensation.Operation.MutationOccurred);
                        _result.CompensationReceipts.Add(typed);
                        await AppendAsync(
                            step.StepId,
                            operation.CapabilityId,
                            restored
                                ? SnapshotRestoreStepState.CompensatedVerified
                                : SnapshotRestoreStepState.CompensationFailed,
                            compensation.Operation.ReceiptId,
                            compensation.Operation.RecoveryId,
                            restored ? SnapshotMutationStatus.Known : SnapshotMutationStatus.Unknown,
                            compensation.Operation.ReasonCode,
                            compensation.Operation.Message).ConfigureAwait(false);
                        if (restored)
                        {
                            _result.CompensatedCount++;
                            _result.RestoredCount = Math.Max(0, _result.RestoredCount - 1);
                        }
                        else
                        {
                            compensationVerified = false;
                            _result.FailedCount++;
                            _result.MutationStatus = SnapshotMutationStatus.Unknown;
                            _result.Errors.Add($"{operation.CapabilityId}: reverse compensation was not verified.");
                        }
                    }
                    catch (Exception ex)
                    {
                        compensationVerified = false;
                        _result.FailedCount++;
                        _result.MutationStatus = SnapshotMutationStatus.Unknown;
                        _result.Errors.Add($"{operation.CapabilityId}: reverse compensation failed: {ex.Message}");
                        await AppendAsync(
                            step.StepId,
                            operation.CapabilityId,
                            SnapshotRestoreStepState.CompensationFailed,
                            Guid.Empty,
                            operation.RecoveryId,
                            SnapshotMutationStatus.Unknown,
                            "snapshot.compensation-exception",
                            ex.Message).ConfigureAwait(false);
                    }
                }
                _result.CompensationStatus = compensationVerified
                    ? SnapshotCompensationStatus.VerifiedBeforeState
                    : SnapshotCompensationStatus.FailedOrUnknown;
                if (compensationVerified)
                {
                    _result.OutcomeReasonCode = "snapshot.failed-no-change";
                }
                _recoverable.Clear();
            }

            private SnapshotRestoreStepPlan FindStep(string capabilityId) =>
                _plan.Steps.Single(step =>
                    step.Supported
                    && string.Equals(step.CapabilityId, capabilityId, StringComparison.Ordinal));

            private Task AppendAsync(
                string stepId,
                string? capabilityId,
                SnapshotRestoreStepState state,
                Guid receiptId,
                Guid? recoveryId,
                SnapshotMutationStatus mutationStatus,
                string reasonCode,
                string message) =>
                _store.AppendReceiptAsync(
                    _plan.PlanId,
                    _plan.PlanSha256,
                    new SnapshotRestoreStepReceipt(
                        0,
                        stepId,
                        capabilityId,
                        state,
                        receiptId,
                        recoveryId,
                        mutationStatus,
                        reasonCode,
                        message,
                        DateTimeOffset.UtcNow),
                    CancellationToken.None);
        }

        private sealed record SnapshotRegistryDwordTarget(int Action, int? Value);
        private sealed record SnapshotRegistryStringTarget(int Action, string? Value, bool RequireExistingValue);
        private sealed record SnapshotRegistryTypedTargetValue(
            string ValueName, int Kind, string? StringValue, int? DwordValue, int Action);
        private sealed record SnapshotRegistryValueSetTarget(IReadOnlyList<SnapshotRegistryTypedTargetValue> Values);
        private sealed record SnapshotTypedRestoreRequest(string CapabilityId, string TargetPayload);

        private static bool HasValidTypedDwordManifest(OptimizationSnapshot snapshot)
        {
            var manifest = snapshot.TypedRegistryDwordStates;
            if (manifest.Count != TypedRegistryDwordDefinitions.Length
                || manifest.Select(item => item.CapabilityId).Distinct(StringComparer.Ordinal).Count() != manifest.Count)
                return false;
            return TypedRegistryDwordDefinitions.All(definition =>
            {
                var item = manifest.SingleOrDefault(candidate =>
                    string.Equals(candidate.CapabilityId, definition.CapabilityId, StringComparison.Ordinal));
                return item is not null
                    && string.Equals(item.SourceKey, definition.SourceKey, StringComparison.Ordinal)
                    && (item.ValueExists ? item.Value is not null : item.Value is null);
            });
        }

        private static bool HasValidTypedCapabilityManifest(OptimizationSnapshot snapshot)
        {
            var manifest = snapshot.TypedRegistryCapabilityStates;
            if (manifest.Count != TypedRegistryCapabilityDefinitions.Length
                || manifest.Select(item => item.CapabilityId).Distinct(StringComparer.Ordinal).Count() != manifest.Count)
                return false;
            return TypedRegistryCapabilityDefinitions.All(definition =>
            {
                var item = manifest.SingleOrDefault(candidate =>
                    string.Equals(candidate.CapabilityId, definition.CapabilityId, StringComparison.Ordinal));
                return TryBuildTypedCapabilityRequest(definition, item, out _, out _);
            });
        }

        // P1-024: 允许的注册表根键白名单
        private static readonly HashSet<string> AllowedRegistryRoots = new(StringComparer.OrdinalIgnoreCase)
        {
            "HKEY_CURRENT_USER",
            "HKEY_LOCAL_MACHINE",
            "HKCU",
            "HKLM"
        };

        // P1-024: 禁止修改的注册表路径黑名单
        private static readonly string[] ForbiddenRegistryPaths = new[]
        {
            @"HKEY_LOCAL_MACHINE\SAM",
            @"HKEY_LOCAL_MACHINE\SECURITY",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Lsa",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"
        };

        private static bool IsRegistryPathSafe(string keyPath)
        {
            if (string.IsNullOrWhiteSpace(keyPath)) return false;
            
            // 检查路径遍历攻击
            if (keyPath.Contains("..") || keyPath.Contains("//") || keyPath.Contains("\\\\"))
            {
                Debug.WriteLine($"[Snapshot] 注册表路径包含非法字符: {keyPath}");
                return false;
            }

            // 检查根键是否在白名单中
            var rootEnd = keyPath.IndexOf('\\');
            var root = rootEnd > 0 ? keyPath[..rootEnd] : keyPath;
            if (!AllowedRegistryRoots.Contains(root))
            {
                Debug.WriteLine($"[Snapshot] 注册表根键不在白名单中: {root}");
                return false;
            }

            // 检查是否在黑名单中
            foreach (var forbidden in ForbiddenRegistryPaths)
            {
                if (keyPath.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[Snapshot] 注册表路径在黑名单中: {keyPath}");
                    return false;
                }
            }

            return true;
        }

        public async Task<IReadOnlyList<SnapshotInfo>> GetSnapshotsAsync()
        {
            var catalog = await GetSnapshotCatalogAsync().ConfigureAwait(false);
            return catalog.Snapshots;
        }

        public Task<IReadOnlyList<SnapshotRestoreBatchIdentity>> GetPendingRestoreBatchesAsync(
            CancellationToken ct = default) => _batchStore.EnumeratePendingAsync(ct);

        public async Task<SnapshotCatalogResult> GetSnapshotCatalogAsync(CancellationToken ct = default)
        {
            var snapshots = new List<SnapshotInfo>();
            var diagnostics = new List<SnapshotCatalogDiagnostic>();
            if (!Directory.Exists(_snapshotDir)) return new(snapshots, diagnostics);

            foreach (var file in Directory.GetFiles(_snapshotDir, "*.json", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!_filePolicy.TryGetIdFromSnapshotFile(file, out var fileId, out var pathDiagnostic))
                    {
                        diagnostics.Add(new(Path.GetFileName(file), pathDiagnostic, "Snapshot path or file name is invalid."));
                        continue;
                    }
                    var snapshot = await ReadAndValidateSnapshotFileAsync(file, fileId, ct).ConfigureAwait(false);
                    var fi = new FileInfo(file);
                    var typedValueCount = snapshot.TypedRegistryDwordStates.Count
                        + snapshot.TypedRegistryCapabilityStates.Sum(static item => item.Values.Count);
                    var typedCapabilityCount = snapshot.TypedRegistryDwordStates.Count
                        + snapshot.TypedRegistryCapabilityStates.Count;
                    snapshots.Add(new SnapshotInfo
                    {
                        Id = snapshot.Id,
                        Name = snapshot.Name,
                        CreatedAt = snapshot.CreatedAt,
                        FilePath = file,
                        FileSize = fi.Length,
                        SettingsCount = typedValueCount,
                        OperationsCount = typedCapabilityCount,
                        AppliedCount = typedValueCount,
                        HasRegistryBackup = snapshot.HasRegistryBackup || HasRegistryBackup(snapshot.Id)
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Snapshot] 读取快照文件失败 {file}: {ex.Message}");
                    diagnostics.Add(new(Path.GetFileName(file), "snapshot.catalog.invalid", ex.Message));
                }
            }
            return new(
                snapshots.OrderByDescending(static s => s.CreatedAt).ToList(),
                diagnostics.AsReadOnly());
        }

        public async Task<OptimizationSnapshot?> GetSnapshotAsync(string snapshotId)
        {
            var filePath = GetSnapshotPath(snapshotId);
            if (!File.Exists(filePath)) return null;
            return await ReadAndValidateSnapshotFileAsync(filePath, snapshotId, CancellationToken.None).ConfigureAwait(false);
        }

        public Task DeleteSnapshotAsync(string snapshotId)
        {
            var filePath = GetSnapshotPath(snapshotId);
            if (File.Exists(filePath)) File.Delete(filePath);
            
            // V3: 清理 .reg 备份子目录
            var regDir = GetRegBackupDir(snapshotId);
            try
            {
                if (Directory.Exists(regDir))
                {
                    Directory.Delete(regDir, recursive: true);
                    Debug.WriteLine($"[SnapshotService] 已清理 .reg 备份目录: {regDir}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SnapshotService] 清理 .reg 备份目录失败（不影响删除）: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        public async Task ExportSnapshotAsync(string snapshotId, string filePath)
        {
            var loc = LocalizationService.Instance;
            var sourcePath = GetSnapshotPath(snapshotId);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException(loc.Get("Snapshot.FileNotFound"), snapshotId);

            // P1: 目标目录存在性检查
            var targetDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                throw new DirectoryNotFoundException(
                    loc.Get("Snapshot.ExportDirNotFound", targetDir));
            }

            // P1: 磁盘空间检查
            var sourceInfo = new FileInfo(sourcePath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                var diskCheck = SystemResourceChecker.CheckDiskSpace(targetDir, sourceInfo.Length * 2);
                if (!diskCheck.Success)
                {
                    throw new IOException(diskCheck.ErrorMessage ?? loc.Get("Backup.Error.DiskFull"));
                }
            }

            try
            {
                var content = await File.ReadAllTextAsync(sourcePath);
                await File.WriteAllTextAsync(filePath, content);
                Debug.WriteLine($"[SnapshotService] Exported snapshot {snapshotId} to {filePath}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"[SnapshotService] Export permission denied: {ex.Message}");
                throw new UnauthorizedAccessException(
                    loc.Get("Snapshot.ExportPermissionDenied", filePath), ex);
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070070)) // ERROR_DISK_FULL
            {
                Debug.WriteLine($"[SnapshotService] Export disk full: {ex.Message}");
                throw new IOException(loc.Get("Backup.Error.DiskFull"), ex);
            }
        }

        // Why: 50MB上限防止恶意构造的超大JSON导致OOM
        private const long MaxImportFileSizeBytes = SnapshotFilePolicy.MaximumSnapshotBytes;

        // File: GameOptimizer.Core/Services/Snapshot/SnapshotService.cs
        public async Task<OptimizationSnapshot?> ImportSnapshotAsync(string filePath)
        {
            var loc = LocalizationService.Instance;
            if (!File.Exists(filePath)) return null;

            // 文件大小检查 — 防止OOM攻击
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxImportFileSizeBytes)
            {
                Debug.WriteLine($"[SnapshotService] Import rejected: file too large ({fileInfo.Length} bytes)");
                throw new InvalidOperationException(
                    loc.Get("Snapshot.FileTooLarge", fileInfo.Length / (1024 * 1024)));
            }

            try
            {
                await using var importStream = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var snapshot = await JsonSerializer.DeserializeAsync<OptimizationSnapshot>(
                    importStream, SnapshotFilePolicy.StrictJsonOptions).ConfigureAwait(false);
                if (snapshot == null || string.IsNullOrEmpty(snapshot.Name))
                    throw new InvalidOperationException(loc.Get("Snapshot.InvalidFile"));

                // Imports are accepted only when their original identity, strict V4/V5 typed
                // manifest and mandatory checksum form a complete, self-consistent document.
                ValidateStrictSnapshot(snapshot, snapshot.Id);

                // FIX-R6-#1: Validate imported snapshot data bounds to prevent abuse
                const int MaxSettingsCount = 500;
                const int MaxOperationStatesCount = 500;
                if (snapshot.Settings.Count > MaxSettingsCount)
                    throw new InvalidOperationException(loc.Get("Snapshot.TooManySettings"));
                if (snapshot.OperationStates.Count > MaxOperationStatesCount)
                    throw new InvalidOperationException(loc.Get("Snapshot.TooManyOperations"));
                
                // Pre-validate registry paths before accepting the snapshot
                var unsafePaths = snapshot.Settings.Keys
                    .Where(k => k.Contains('|') && !IsRegistryPathSafe(k.Split('|')[0]))
                    .Take(3)
                    .ToList();
                if (unsafePaths.Count > 0)
                {
                    Debug.WriteLine($"[SnapshotService] Import rejected: {unsafePaths.Count} unsafe registry paths");
                    throw new InvalidOperationException(loc.Get("Snapshot.UnsafeRegistryPaths"));
                }

                snapshot.Id = Guid.NewGuid().ToString("N")[..8];
                snapshot.Name = $"{snapshot.Name}{loc.Get("Snapshot.Imported")}";
                snapshot.CreatedAt = DateTime.Now;
                snapshot.Checksum = SnapshotChecksumCalculator.ComputeChecksum(snapshot);

                string? dirError = null;
                SafeDirectoryOperations.TryCreateDirectory(_snapshotDir, out dirError);
                var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
                var requiredBytes = checked(Encoding.UTF8.GetByteCount(snapshotJson) * 2L + 64 * 1024);
                var diskCheck = SystemResourceChecker.CheckDiskSpace(_snapshotDir, requiredBytes);
                if (!diskCheck.Success)
                    throw new IOException(diskCheck.ErrorMessage ?? loc.Get("Backup.Error.DiskFull"));
                var writeResult = await SnapshotFileWriter.WriteSnapshotSafelyAsync(
                    GetSnapshotPath(snapshot.Id), snapshot).ConfigureAwait(false);
                if (!writeResult.Success)
                {
                    throw new IOException($"Failed to import snapshot: {writeResult.Error}");
                }
                return snapshot;
            }
            catch (JsonException) { throw new InvalidOperationException(loc.Get("Snapshot.FormatError")); }
        }

        public string GetSnapshotDirectory() => _snapshotDir;
        private string GetSnapshotPath(string id) => _filePolicy.GetSnapshotPath(id);

        private async Task<OptimizationSnapshot> ReadAndValidateSnapshotFileAsync(
            string filePath,
            string expectedId,
            CancellationToken cancellationToken)
        {
            var canonical = _filePolicy.EnsureContained(filePath);
            var length = new FileInfo(canonical).Length;
            if (length <= 0 || length > SnapshotFilePolicy.MaximumSnapshotBytes)
                throw new InvalidDataException("Snapshot file is empty or exceeds the size limit.");

            await using var stream = new FileStream(
                canonical, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<OptimizationSnapshot>(
                stream, SnapshotFilePolicy.StrictJsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Snapshot document is missing.");
            ValidateStrictSnapshot(snapshot, expectedId);
            return snapshot;
        }

        private static void ValidateStrictSnapshot(OptimizationSnapshot snapshot, string expectedId)
        {
            SnapshotFilePolicy.ValidateSnapshotId(snapshot.Id);
            if (!string.Equals(snapshot.Id, expectedId, StringComparison.Ordinal)
                || snapshot.Version is not (4 or 5)
                || snapshot.Settings is null
                || snapshot.OperationStates is null
                || snapshot.ServiceStates is null
                || snapshot.BcdSettings is null
                || snapshot.TypedRegistryDwordStates is null
                || snapshot.TypedRegistryCapabilityStates is null
                || string.IsNullOrWhiteSpace(snapshot.Checksum)
                || !HasValidTypedDwordManifest(snapshot)
                || snapshot.Version == 4 && snapshot.TypedRegistryCapabilityStates.Count != 0
                || snapshot.Version == 5 && !HasValidTypedCapabilityManifest(snapshot))
            {
                throw new InvalidDataException("Snapshot identity, schema, or typed manifest is invalid.");
            }
            var validation = SnapshotChecksumCalculator.ValidateSnapshot(snapshot);
            var computed = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
            if (!validation.IsValid || !SnapshotFilePolicy.FixedChecksumEquals(snapshot.Checksum, computed))
                throw new InvalidDataException("Snapshot checksum does not match its content.");
        }

        /// <summary>
        /// 清理快照名称，移除不安全字符
        /// </summary>
        private static string SanitizeSnapshotName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            
            // 移除文件系统不允许的字符: \ / : * ? " < > |
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new System.Text.StringBuilder();
            
            foreach (var c in name.Trim())
            {
                // 只允许：字母、数字、中文、空格、下划线、连字符、点
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-' || c == '.')
                {
                    if (!invalidChars.Contains(c))
                        sanitized.Append(c);
                }
            }
            
            // 移除连续空格
            var result = System.Text.RegularExpressions.Regex.Replace(sanitized.ToString(), @"\s+", " ").Trim();
            
            // 移除开头和结尾的点（Windows文件名限制）
            result = result.Trim('.');
            
            return result;
        }

        // 需要备份的关键注册表路径
        private sealed record TypedRegistryDwordDefinition(
            string CapabilityId,
            string SubKeyPath,
            string ValueName,
            string Category)
        {
            public string SourceKey => $"HKEY_CURRENT_USER\\{SubKeyPath}|{ValueName}|{Category}";
        }

        private static readonly TypedRegistryDwordDefinition[] TypedRegistryDwordDefinitions =
        {
            new(RepairChangeCapabilityIds.GameDvrAppCapture,
                @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", "游戏录制"),
            new(RepairChangeCapabilityIds.GameDvrEnabled,
                @"System\GameConfigStore", "GameDVR_Enabled", "游戏模式"),
            new(RepairChangeCapabilityIds.GameModeAutoEnable,
                @"SOFTWARE\Microsoft\GameBar", "AutoGameModeEnabled", "Game Bar"),
            new("basic.disable_game_bar",
                @"SOFTWARE\Microsoft\GameBar", "ShowStartupPanel", "Game Bar")
        };

        private sealed record TypedRegistryValueDefinition(
            string ValueName,
            SnapshotRegistryScalarKind Kind);

        private sealed record TypedRegistryCapabilityDefinition(
            string CapabilityId,
            string SubKeyPath,
            TypedRegistryValueDefinition[] Values);

        private static readonly TypedRegistryCapabilityDefinition[] TypedRegistryCapabilityDefinitions =
        {
            new("peripheral.mouse_precision", @"Control Panel\Mouse",
            [
                new("MouseSpeed", SnapshotRegistryScalarKind.String),
                new("MouseThreshold1", SnapshotRegistryScalarKind.String),
                new("MouseThreshold2", SnapshotRegistryScalarKind.String)
            ]),
            new("peripheral.keyboard_response", @"Control Panel\Keyboard",
            [
                new("KeyboardDelay", SnapshotRegistryScalarKind.String),
                new("KeyboardSpeed", SnapshotRegistryScalarKind.String)
            ]),
            new("input.disable_pointer_trails", @"Control Panel\Mouse",
            [
                new("MouseTrails", SnapshotRegistryScalarKind.String)
            ]),
            new("input.disable_snap_to", @"Control Panel\Mouse",
            [
                new("SnapToDefaultButton", SnapshotRegistryScalarKind.String)
            ]),
            new("privacy.deny_app_access_to_calendar",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\appointments",
            [
                new("Value", SnapshotRegistryScalarKind.String)
            ]),
            new("privacy.deny_app_access_to_contacts",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\contacts",
            [
                new("Value", SnapshotRegistryScalarKind.String)
            ]),
            new("privacy.deny_app_access_to_tasks",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\userDataTasks",
            [
                new("Value", SnapshotRegistryScalarKind.String)
            ]),
            new("privacy.deny_app_access_to_notifications",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\userNotificationListener",
            [
                new("Value", SnapshotRegistryScalarKind.String)
            ])
        };

        private static readonly HashSet<string> TypedRegistryCapabilityIds =
            TypedRegistryDwordDefinitions.Select(item => item.CapabilityId)
                .Concat(TypedRegistryCapabilityDefinitions.Select(item => item.CapabilityId))
                .ToHashSet(StringComparer.Ordinal);

        private static readonly HashSet<string> TypedRegistrySourceKeys =
            TypedRegistryDwordDefinitions.Select(item => item.SourceKey)
                .Concat(new[]
                {
                    @"HKEY_CURRENT_USER\Control Panel\Mouse|MouseSpeed|鼠标",
                    @"HKEY_CURRENT_USER\Control Panel\Mouse|MouseThreshold1|鼠标",
                    @"HKEY_CURRENT_USER\Control Panel\Mouse|MouseThreshold2|鼠标",
                    @"HKEY_CURRENT_USER\Control Panel\Keyboard|KeyboardDelay|键盘",
                    @"HKEY_CURRENT_USER\Control Panel\Keyboard|KeyboardSpeed|键盘"
                })
                .ToHashSet(StringComparer.Ordinal);

        #region Deep Registry Backup

        /// <summary>
        /// 旧 .reg 深度恢复入口永久 fail-closed；只能通过 typed Owner 恢复。
        /// </summary>
        public Task<RegistryBackupRestoreResult> RestoreRegistryBackupAsync(string snapshotId, CancellationToken ct = default)
        {
            SnapshotFilePolicy.ValidateSnapshotId(snapshotId);
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new RegistryBackupRestoreResult
            {
                Success = false,
                FailedFiles = { "snapshot.restore-plan-required" }
            });
        }

        /// <summary>
        /// 只读检查旧快照是否包含 .reg 证据文件。
        /// </summary>
        public bool HasRegistryBackup(string snapshotId)
        {
            SnapshotFilePolicy.ValidateSnapshotId(snapshotId);
            var regDir = GetRegBackupDir(snapshotId);
            try
            {
                return Directory.Exists(regDir) && Directory.GetFiles(regDir, "*.reg").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取快照的 .reg 备份子目录路径
        /// </summary>
        private string GetRegBackupDir(string snapshotId)
        {
            return _filePolicy.GetLegacyRegistryDirectory(snapshotId);
        }

        private async Task<SnapshotRestorePreparation> ReadRestorePreparationAsync(
            string snapshotId,
            SnapshotRestoreKind kind,
            CancellationToken cancellationToken)
        {
            SnapshotRestoreCoordinator.ValidateSnapshotId(snapshotId);
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await GetSnapshotAsync(snapshotId).ConfigureAwait(false)
                ?? throw new FileNotFoundException("Snapshot does not exist.", snapshotId);
            var validation = SnapshotChecksumCalculator.ValidateSnapshot(snapshot);
            if (!validation.IsValid
                || !IsTypedRestoreSchemaVersionSupported(snapshot.Version)
                || !HasValidTypedDwordManifest(snapshot)
                || (snapshot.Version >= 5 && !HasValidTypedCapabilityManifest(snapshot))
                || string.IsNullOrWhiteSpace(snapshot.Checksum))
                throw new InvalidDataException("Snapshot schema or checksum is missing or incompatible.");
            var computed = SnapshotChecksumCalculator.ComputeChecksum(snapshot);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(snapshot.Checksum), Convert.FromHexString(computed)))
                throw new InvalidDataException("Snapshot checksum does not match its content.");

            var identity = new StringBuilder()
                .Append("snapshot-restore-v2").Append('\u001F')
                .Append(kind).Append('\u001F')
                .Append(snapshot.Id).Append('\u001F')
                .Append(snapshot.Version).Append('\u001F')
                .Append(computed);
            var steps = new List<SnapshotRestoreStepPlan>();

            if (kind == SnapshotRestoreKind.DeepRegistry)
            {
                var regDirectory = GetRegBackupDir(snapshotId);
                if (!Directory.Exists(regDirectory))
                    throw new DirectoryNotFoundException("Deep registry backup directory does not exist.");
                var files = Directory.GetFiles(regDirectory, "*.reg", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (files.Length == 0) throw new InvalidDataException("Deep registry backup contains no .reg files.");
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
                    var fileHash = Convert.ToHexString(SHA256.HashData(bytes));
                    var fileName = Path.GetFileName(file);
                    identity.Append('\u001F').Append(fileName).Append(':').Append(bytes.Length).Append(':').Append(fileHash);
                    steps.Add(UnsupportedStep(
                        SnapshotRestoreResourceKind.UnsupportedRegistryFile,
                        fileName,
                        fileHash,
                        "snapshot.deep-registry-typed-owner-unavailable"));
                }
            }
            else
            {
                steps.AddRange(BuildTypedSteps(snapshot));
                steps.AddRange(BuildUnsupportedLegacySteps(snapshot));
            }

            return new SnapshotRestorePreparation(
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString()))),
                steps.AsReadOnly());
        }

        internal static IReadOnlyList<SnapshotRestoreStepPlan> BuildTypedSteps(OptimizationSnapshot snapshot)
        {
            var steps = new List<SnapshotRestoreStepPlan>();
            foreach (var definition in TypedRegistryDwordDefinitions)
            {
                var item = snapshot.TypedRegistryDwordStates.Single(candidate =>
                    string.Equals(candidate.CapabilityId, definition.CapabilityId, StringComparison.Ordinal));
                var payload = JsonSerializer.Serialize(new SnapshotRegistryDwordTarget(
                    item.ValueExists ? 0 : 1,
                    item.Value));
                steps.Add(TypedStep(definition.CapabilityId, payload));
            }

            if (snapshot.Version >= 5)
            {
                foreach (var definition in TypedRegistryCapabilityDefinitions)
                {
                    var item = snapshot.TypedRegistryCapabilityStates.Single(candidate =>
                        string.Equals(candidate.CapabilityId, definition.CapabilityId, StringComparison.Ordinal));
                    if (!TryBuildTypedCapabilityRequest(definition, item, out var request, out var error))
                    {
                        throw new InvalidDataException(error);
                    }
                    steps.Add(TypedStep(request!.CapabilityId, request.TargetPayload));
                }
            }
            return steps.AsReadOnly();
        }

        internal static bool IsTypedRestoreSchemaVersionSupported(int version) => version is 4 or 5;

        internal static IReadOnlyList<SnapshotRestoreStepPlan> BuildUnsupportedLegacySteps(
            OptimizationSnapshot snapshot)
        {
            var steps = new List<SnapshotRestoreStepPlan>();
            foreach (var operation in snapshot.OperationStates
                         .Where(item => !TypedRegistryCapabilityIds.Contains(item.Key))
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                steps.Add(UnsupportedStep(
                    SnapshotRestoreResourceKind.UnsupportedLegacyOperation,
                    operation.Key,
                    HashTarget(operation.Value),
                    "snapshot.legacy-operation-unsupported"));
            }

            foreach (var setting in snapshot.Settings
                         .Where(item => !TypedRegistrySourceKeys.Contains(item.Key))
                         .OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                steps.Add(UnsupportedStep(
                    SnapshotRestoreResourceKind.UnsupportedRawRegistry,
                    setting.Key,
                    HashTarget(setting.Value),
                    "snapshot.raw-registry-unsupported"));
            }

            if (!string.IsNullOrWhiteSpace(snapshot.ActivePowerPlanGuid))
            {
                steps.Add(UnsupportedStep(
                    SnapshotRestoreResourceKind.UnsupportedPowerPlan,
                    "active-scheme",
                    HashTarget(snapshot.ActivePowerPlanGuid),
                    "snapshot.power-owner-unavailable"));
            }

            foreach (var service in snapshot.ServiceStates.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                steps.Add(UnsupportedStep(
                    SnapshotRestoreResourceKind.UnsupportedService,
                    service.Key,
                    HashTarget(service.Value),
                    "snapshot.service-owner-unavailable"));
            }

            foreach (var bcd in snapshot.BcdSettings.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                steps.Add(UnsupportedStep(
                    SnapshotRestoreResourceKind.UnsupportedBcd,
                    bcd.Key,
                    HashTarget(bcd.Value),
                    "snapshot.bcd-owner-unavailable"));
            }
            return steps.AsReadOnly();
        }

        private static SnapshotRestoreStepPlan TypedStep(string capabilityId, string targetPayload) =>
            new(
                $"typed:{capabilityId}",
                SnapshotRestoreResourceKind.TypedCapability,
                capabilityId,
                HashTarget(targetPayload),
                true,
                "snapshot.typed-owner");

        private static SnapshotRestoreStepPlan UnsupportedStep(
            SnapshotRestoreResourceKind kind,
            string sourceIdentity,
            string targetSha256,
            string reasonCode) =>
            new(
                $"unsupported:{kind}:{HashTarget(sourceIdentity)[..16]}",
                kind,
                null,
                targetSha256,
                false,
                reasonCode);

        private static string HashTarget<T>(T value) =>
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

        #endregion
    }
}
