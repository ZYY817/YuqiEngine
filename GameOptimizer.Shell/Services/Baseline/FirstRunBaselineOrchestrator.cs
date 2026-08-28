using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Services;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class FirstRunBaselineOrchestrator : IFirstRunBaselineOrchestrator
{
	private sealed class BaselinePreflightLockHandle : IAsyncDisposable
	{
		private readonly FileStream _lockStream;

		private int _disposed;

		public BaselinePreflightLockHandle(FileStream lockStream)
		{
			_lockStream = lockStream;
		}

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0)
			{
				return ValueTask.CompletedTask;
			}
			try
			{
				_lockStream.Dispose();
			}
			finally
			{
				BaselineFlowLock.Release();
			}
			return ValueTask.CompletedTask;
		}
	}

	private const long RequiredDiskSpaceBytes = 268435456L;

	private const int DomainCollectMaxAttempts = 3;

	private static readonly string BaselineRoot = BaselineMirrorDirectoryPolicy.PrimaryBaselineRoot;

	private static readonly string BaselinePackagesRoot = BaselineMirrorDirectoryPolicy.PrimaryPackagesRoot;

	private static readonly string BaselineFlowLockFilePath = Path.Combine(BaselineRoot, ".first-run-baseline.lock");

	private static readonly TimeSpan DomainCollectRetryDelay = TimeSpan.FromSeconds(2.0);

	private static readonly SemaphoreSlim BaselineFlowLock = new SemaphoreSlim(1, 1);

	private readonly IBaselineGateService _baselineGateService;

	private readonly IBaselineDomainCollector _domainCollector;

	private readonly IBaselineIntegrityService _integrityService;

	private readonly IBaselineManualGuideService _manualGuideService;

	private readonly IBaselineCatalogService _catalogService;

	private readonly IBaselineRestoreOrchestrator _baselineRestoreOrchestrator;

	private readonly ISystemRestoreService _systemRestoreService;

	private readonly BaselineRuntimeStateStore _runtimeStateStore;

	private readonly ILogger<FirstRunBaselineOrchestrator> _logger;

	public FirstRunBaselineOrchestrator(IBaselineGateService baselineGateService, IBaselineDomainCollector domainCollector, IBaselineIntegrityService integrityService, IBaselineManualGuideService manualGuideService, IBaselineCatalogService catalogService, IBaselineRestoreOrchestrator baselineRestoreOrchestrator, ISystemRestoreService systemRestoreService, BaselineRuntimeStateStore runtimeStateStore, ILogger<FirstRunBaselineOrchestrator> logger)
	{
		_baselineGateService = baselineGateService ?? throw new ArgumentNullException("baselineGateService");
		_domainCollector = domainCollector ?? throw new ArgumentNullException("domainCollector");
		_integrityService = integrityService ?? throw new ArgumentNullException("integrityService");
		_manualGuideService = manualGuideService ?? throw new ArgumentNullException("manualGuideService");
		_catalogService = catalogService ?? throw new ArgumentNullException("catalogService");
		_baselineRestoreOrchestrator = baselineRestoreOrchestrator ?? throw new ArgumentNullException("baselineRestoreOrchestrator");
		_systemRestoreService = systemRestoreService ?? throw new ArgumentNullException("systemRestoreService");
		_runtimeStateStore = runtimeStateStore ?? throw new ArgumentNullException("runtimeStateStore");
		_logger = logger ?? throw new ArgumentNullException("logger");
	}

	public IReadOnlyList<BaselineDomainDefinition> GetDefinitions()
	{
		return _domainCollector.GetDefinitions();
	}

	public async Task<BaselineRuntimeState> GetOrCreateRuntimeStateAsync(bool forceReset, string? creationSource = null, CancellationToken ct = default(CancellationToken))
	{
		BaselineRuntimeState? current = await _runtimeStateStore.LoadAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!forceReset && current != null && !string.IsNullOrWhiteSpace(current.PackageId))
		{
			if (string.IsNullOrWhiteSpace(current.CreationSource))
			{
				current.CreationSource = NormalizeCreationSource(creationSource);
			}
			return EnsureDomainList(current);
		}
		BaselineRuntimeState created = new BaselineRuntimeState
		{
			PackageId = BuildPackageId(),
			CreationSource = NormalizeCreationSource(creationSource),
			CurrentStep = BaselineFlowStep.Overview,
			StartedUtc = DateTime.UtcNow,
			LastUpdatedUtc = DateTime.UtcNow,
			ManualGuideCompleted = false,
			HasBlockingFailure = false,
			Domains = (from definition in _domainCollector.GetDefinitions()
				select new BaselineDomainProgress
				{
					DomainId = definition.DomainId,
					DomainName = definition.DomainName,
					Description = definition.Description,
					Required = definition.Required,
					Status = BaselineDomainStatus.Pending
				}).ToList()
		};
		await _runtimeStateStore.SaveAsync(created, ct).ConfigureAwait(continueOnCapturedContext: false);
		return created;
	}

	public async Task<BaselineRunResult> RunAsync(IProgress<BaselineProgressEvent>? progress = null, CancellationToken ct = default(CancellationToken))
	{
		BaselineRuntimeState runtime = EnsureDomainList(await GetOrCreateRuntimeStateAsync(forceReset: false, null, ct).ConfigureAwait(continueOnCapturedContext: false));
		BaselinePreflightLockHandle? preflightLockHandle = null;
		BaselineRunResult result;
		try
		{
			(bool Success, string ErrorMessage, BaselinePreflightLockHandle? LockHandle) preflight = await RunPreflightAsync(runtime, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
			preflightLockHandle = preflight.LockHandle;
			if (!preflight.Success)
			{
				runtime.CurrentStep = BaselineFlowStep.Preflight;
				runtime.HasBlockingFailure = true;
				await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
				Report(progress, runtime, BaselineFlowStep.Preflight, preflight.ErrorMessage, null, BaselineDomainStatus.Failed);
				await _baselineGateService.MarkBaselineFailureAsync("baseline_orchestrator_preflight", FirstRunBaselineState.PreflightFailed, preflight.ErrorMessage, ct).ConfigureAwait(continueOnCapturedContext: false);
				result = new BaselineRunResult
				{
					Success = false,
					RequiresManualGuide = false,
					Message = preflight.ErrorMessage,
					RuntimeState = runtime
				};
			}
			else
			{
				await _baselineGateService.MarkBaselineFailureAsync("baseline_orchestrator_collecting", FirstRunBaselineState.Running, null, ct).ConfigureAwait(continueOnCapturedContext: false);
				runtime.CurrentStep = BaselineFlowStep.Collecting;
				await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
				Report(progress, runtime, BaselineFlowStep.Collecting, "开始执行全量采集。");
				string packageDirectory = Path.Combine(BaselinePackagesRoot, runtime.PackageId);
				Directory.CreateDirectory(packageDirectory);
				IReadOnlyList<BaselineDomainDefinition> definitions = _domainCollector.GetDefinitions();
				foreach (BaselineDomainDefinition definition in definitions)
				{
					ct.ThrowIfCancellationRequested();
					BaselineDomainProgress domainProgress = runtime.Domains.First((BaselineDomainProgress domain) => domain.DomainId == definition.DomainId);
					if (domainProgress.Status == BaselineDomainStatus.Succeeded)
					{
						continue;
					}
					domainProgress.Status = BaselineDomainStatus.Running;
					domainProgress.ErrorCode = null;
					domainProgress.ErrorMessage = null;
					domainProgress.StartedUtc = DateTime.UtcNow;
					await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
					Report(progress, runtime, BaselineFlowStep.Collecting, "正在处理 " + definition.DomainId + "。", definition.DomainId, BaselineDomainStatus.Running);
					if (!definition.DomainId.Equals("D13", StringComparison.OrdinalIgnoreCase))
					{
						BaselineDomainArtifact artifact = await CollectDomainWithRetryAsync(runtime, definition, packageDirectory, progress, ct).ConfigureAwait(continueOnCapturedContext: false);
						domainProgress.Status = artifact.Status;
						domainProgress.ErrorCode = artifact.ErrorCode;
						domainProgress.ErrorMessage = artifact.ErrorMessage;
						domainProgress.FinishedUtc = DateTime.UtcNow;
						runtime.HasBlockingFailure |= artifact.Status == BaselineDomainStatus.Failed && definition.Required;
						await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
						string message = ((artifact.Status == BaselineDomainStatus.Succeeded) ? (definition.DomainId + " 完成。") : (definition.DomainId + " 失败：" + artifact.ErrorMessage));
						Report(progress, runtime, BaselineFlowStep.Collecting, message, definition.DomainId, artifact.Status);
						continue;
					}
					(bool Success, string Message) manualResult = await HandleRestorePointDomainAsync(runtime, packageDirectory, definition, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (manualResult.Success)
					{
						continue;
					}
					runtime.CurrentStep = BaselineFlowStep.ManualGuide;
					runtime.HasBlockingFailure = true;
					await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
					await _baselineGateService.MarkBaselineFailureAsync("baseline_orchestrator_manual_required", FirstRunBaselineState.ManualInterventionRequired, manualResult.Message, ct).ConfigureAwait(continueOnCapturedContext: false);
					Report(progress, runtime, BaselineFlowStep.ManualGuide, manualResult.Message, "D13", BaselineDomainStatus.ManualRequired);
					result = new BaselineRunResult
					{
						Success = false,
						RequiresManualGuide = true,
						Message = manualResult.Message,
						RuntimeState = runtime
					};
					goto end_IL_0237;
				}
				if (runtime.HasBlockingFailure)
				{
					runtime.CurrentStep = BaselineFlowStep.Collecting;
					await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
					await _baselineGateService.MarkBaselineFailureAsync("baseline_orchestrator_collect_failed", FirstRunBaselineState.PartialFailed, "One or more required domains failed.", ct).ConfigureAwait(continueOnCapturedContext: false);
					result = new BaselineRunResult
					{
						Success = false,
						RequiresManualGuide = false,
						Message = "关键域采集失败，请先修复后重试。",
						RuntimeState = runtime
					};
				}
				else
				{
					runtime.CurrentStep = BaselineFlowStep.Verifying;
					await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
					Report(progress, runtime, BaselineFlowStep.Verifying, "开始封包与完整性校验。");
					BaselinePackageManifest manifest = await BuildManifestAsync(runtime, packageDirectory, ct).ConfigureAwait(continueOnCapturedContext: false);
					await _integrityService.WriteManifestAsync(packageDirectory, manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
					(bool Success, string ErrorMessage) verifyResult = await _integrityService.VerifyPackageAsync(packageDirectory, manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (!verifyResult.Success)
					{
						manifest.BaselineState = FirstRunBaselineState.Corrupted;
						manifest.IntegrityState = "Corrupted";
						manifest.LifecycleState = "Corrupted";
						await _catalogService.UpsertAsync(manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
						await _baselineGateService.MarkBaselineFailureAsync("baseline_orchestrator_verify_failed", FirstRunBaselineState.Corrupted, verifyResult.ErrorMessage, ct).ConfigureAwait(continueOnCapturedContext: false);
						runtime.CurrentStep = BaselineFlowStep.Verifying;
						runtime.HasBlockingFailure = true;
						await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
						result = new BaselineRunResult
						{
							Success = false,
							RequiresManualGuide = false,
							Message = "校验失败：" + verifyResult.ErrorMessage,
							RuntimeState = runtime,
							Manifest = manifest
						};
					}
					else
					{
						manifest.BaselineState = FirstRunBaselineState.CompletedVerified;
						manifest.IntegrityState = "Verified";
						manifest.LifecycleState = "Active";
						BaselineMirrorSyncResult mirrorSync = await BaselineMirrorDirectoryPolicy.SyncPackageToMirrorAsync(packageDirectory, manifest, _integrityService, ct).ConfigureAwait(continueOnCapturedContext: false);
						ApplyMirrorSyncResult(manifest, mirrorSync);
						if (string.Equals(mirrorSync.MirrorSyncState, "Failed", StringComparison.OrdinalIgnoreCase))
						{
							Report(progress, runtime, BaselineFlowStep.Verifying, "镜像目录同步失败（不影响主路径）：" + mirrorSync.MirrorSyncMessage, null, BaselineDomainStatus.Warning);
						}
						await _catalogService.UpsertAsync(manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
						BaselineRecoverabilityCheckResult selfCheck = await _baselineRestoreOrchestrator.RunRecoverabilitySelfCheckAsync(manifest.PackageId, "auto_after_baseline_creation", ct).ConfigureAwait(continueOnCapturedContext: false);
						manifest.LastRecoverabilityCheckUtc = selfCheck.CheckedUtc;
						manifest.RecoverabilityCheckState = (selfCheck.Success ? "Passed" : "Failed");
						manifest.RecoverabilityCheckMessage = selfCheck.Message;
						await _catalogService.UpsertAsync(manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
						if (!selfCheck.Success)
						{
							Report(progress, runtime, BaselineFlowStep.Verifying, "恢复可用性自检提示：" + selfCheck.Message, null, BaselineDomainStatus.Warning);
						}
						if (!selfCheck.Success)
						{
							await _baselineGateService.MarkBaselineFailureAsync("baseline_orchestrator_recoverability_failed", FirstRunBaselineState.PartialFailed, selfCheck.Message, ct).ConfigureAwait(continueOnCapturedContext: false);
							runtime.CurrentStep = BaselineFlowStep.Verifying;
							runtime.HasBlockingFailure = true;
							await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
							result = new BaselineRunResult
							{
								Success = false,
								RequiresManualGuide = false,
								Message = "Baseline recoverability self-check failed: " + selfCheck.Message,
								RuntimeState = runtime,
								Manifest = manifest
							};
							goto end_IL_0237;
						}
						await _baselineGateService.MarkBaselineCompletedVerifiedAsync("baseline_orchestrator_completed", manifest.PackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
						runtime.CurrentStep = BaselineFlowStep.Completed;
						runtime.HasBlockingFailure = false;
						await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
						Report(progress, runtime, BaselineFlowStep.Completed, "首次基线备份已完成，优化入口已解锁。");
						result = new BaselineRunResult
						{
							Success = true,
							RequiresManualGuide = false,
							Message = "首次基线备份完成。",
							RuntimeState = runtime,
							Manifest = manifest
						};
					}
				}
			}
			end_IL_0237:;
		}
		catch (OperationCanceledException)
		{
			await RecordInterruptedRunFailureAsync(runtime, "首次安全基线备份已取消，请在准备好后重新开始。", null).ConfigureAwait(continueOnCapturedContext: false);
			throw;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "[FirstRunBaselineOrchestrator][RunAsync] Baseline creation stopped unexpectedly. package={PackageId}", runtime.PackageId);
			await RecordInterruptedRunFailureAsync(runtime, "首次安全基线备份异常中断，请检查前置条件后重试。", exception).ConfigureAwait(continueOnCapturedContext: false);
			throw;
		}
		finally
		{
			if (preflightLockHandle != null)
			{
				await preflightLockHandle.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return result;
	}

	private async Task RecordInterruptedRunFailureAsync(BaselineRuntimeState runtime, string message, Exception? exception)
	{
		if (runtime.CurrentStep == BaselineFlowStep.Completed && !runtime.HasBlockingFailure)
		{
			return;
		}

		runtime.HasBlockingFailure = true;
		try
		{
			await _runtimeStateStore.SaveAsync(runtime, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception saveException)
		{
			_logger.LogError(saveException, "[FirstRunBaselineOrchestrator][RecordInterruptedRunFailureAsync] Failed to persist interrupted runtime state. package={PackageId}", runtime.PackageId);
		}

		try
		{
			await _baselineGateService.MarkBaselineFailureAsync("baseline_orchestrator_interrupted", FirstRunBaselineState.PartialFailed, message, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception gateException)
		{
			_logger.LogError(gateException, "[FirstRunBaselineOrchestrator][RecordInterruptedRunFailureAsync] Failed to close the baseline safety gate. package={PackageId}, originalError={OriginalError}", runtime.PackageId, exception?.GetType().Name ?? "Canceled");
		}
	}

	private async Task<BaselineDomainArtifact> CollectDomainWithRetryAsync(BaselineRuntimeState runtime, BaselineDomainDefinition definition, string packageDirectory, IProgress<BaselineProgressEvent>? progress, CancellationToken ct)
	{
		BaselineDomainArtifact? lastArtifact = null;
		List<string> attemptErrors = new List<string>();
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			ct.ThrowIfCancellationRequested();
			lastArtifact = await _domainCollector.CollectDomainAsync(definition, packageDirectory, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (lastArtifact.Status != BaselineDomainStatus.Failed)
			{
				if (attemptErrors.Count > 0 && !string.IsNullOrWhiteSpace(lastArtifact.ErrorMessage))
				{
					attemptErrors.Add($"第{attempt}次：{lastArtifact.ErrorMessage}");
					lastArtifact.ErrorMessage = string.Join(" | ", attemptErrors);
				}
				return lastArtifact;
			}
			string errorMessage = (string.IsNullOrWhiteSpace(lastArtifact.ErrorMessage) ? "未知错误" : lastArtifact.ErrorMessage);
			attemptErrors.Add($"第{attempt}次：{errorMessage}");
			if (attempt >= 3)
			{
				break;
			}
			Report(progress, runtime, BaselineFlowStep.Collecting, $"{definition.DomainId} 采集失败，{DomainCollectRetryDelay.TotalSeconds:0} 秒后自动重试（{attempt}/{3}）。", definition.DomainId, BaselineDomainStatus.Warning);
			await Task.Delay(DomainCollectRetryDelay, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (lastArtifact == null)
		{
			lastArtifact = new BaselineDomainArtifact
			{
				DomainId = definition.DomainId,
				DomainName = definition.DomainName,
				Required = definition.Required,
				Status = BaselineDomainStatus.Failed
			};
		}
		string baseErrorCode = (string.IsNullOrWhiteSpace(lastArtifact.ErrorCode) ? "BASELINE.DOMAIN.COLLECT_FAILED" : lastArtifact.ErrorCode);
		lastArtifact.ErrorCode = baseErrorCode + ".RETRY_EXHAUSTED";
		lastArtifact.ErrorMessage = ((attemptErrors.Count > 0) ? string.Join(" | ", attemptErrors) : "域采集失败，未获得详细错误。");
		return lastArtifact;
	}

	public async Task<BaselineManualGuideCheckResult> RecheckManualGuideAsync(CancellationToken ct = default(CancellationToken))
	{
		BaselineRuntimeState runtime = await GetOrCreateRuntimeStateAsync(forceReset: false, null, ct).ConfigureAwait(continueOnCapturedContext: false);
		BaselineManualGuideCheckResult result = await _manualGuideService.RecheckRestorePointAsync(runtime.PackageId, runtime.StartedUtc, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result;
		}
		string packageDirectory = Path.Combine(BaselinePackagesRoot, runtime.PackageId);
		Directory.CreateDirectory(packageDirectory);
		BaselineDomainProgress? domain = runtime.Domains.FirstOrDefault((BaselineDomainProgress item) => item.DomainId == "D13");
		if (domain == null)
		{
			return result;
		}
		domain.Status = BaselineDomainStatus.Succeeded;
		domain.ErrorCode = null;
		domain.ErrorMessage = null;
		domain.FinishedUtc = DateTime.UtcNow;
		runtime.ManualGuideCompleted = true;
		runtime.CurrentStep = BaselineFlowStep.Collecting;
		runtime.HasBlockingFailure = false;
		await WriteManualDomainArtifactAsync(packageDirectory, domain, ct).ConfigureAwait(continueOnCapturedContext: false);
		await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
		await _baselineGateService.MarkBaselineFailureAsync("baseline_orchestrator_manual_recheck_passed", FirstRunBaselineState.Running, null, ct).ConfigureAwait(continueOnCapturedContext: false);
		return result;
	}

	public async Task<BaselinePackageManifest?> GetCurrentManifestAsync(CancellationToken ct = default(CancellationToken))
	{
		BaselineRuntimeState runtime = await GetOrCreateRuntimeStateAsync(forceReset: false, null, ct).ConfigureAwait(continueOnCapturedContext: false);
		return await _catalogService.GetManifestAsync(runtime.PackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<(bool Success, string ErrorMessage, BaselinePreflightLockHandle? LockHandle)> RunPreflightAsync(BaselineRuntimeState runtime, IProgress<BaselineProgressEvent>? progress, CancellationToken ct)
	{
		runtime.CurrentStep = BaselineFlowStep.Preflight;
		await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
		Report(progress, runtime, BaselineFlowStep.Preflight, "正在检查管理员权限。");
		if (!PermissionChecker.IsRunningAsAdministrator())
		{
			return (Success: false, ErrorMessage: "当前未使用管理员权限启动，无法执行首次安全基线备份。", LockHandle: null);
		}
		Report(progress, runtime, BaselineFlowStep.Preflight, "正在检查主备份目录可写性。");
		BaselineDirectoryProbeResult primaryProbe = BaselineMirrorDirectoryPolicy.ProbePrimaryDirectory(268435456L);
		if (!primaryProbe.Success)
		{
			return (Success: false, ErrorMessage: "首次基线主目录不可用：" + primaryProbe.Message, LockHandle: null);
		}
		Directory.CreateDirectory(BaselinePackagesRoot);
		Report(progress, runtime, BaselineFlowStep.Preflight, "正在检查镜像目录状态（可选）。");
		BaselineDirectoryProbeResult mirrorProbe = BaselineMirrorDirectoryPolicy.ProbeMirrorDirectory(268435456L);
		if (mirrorProbe.IsConfigured && !mirrorProbe.Success)
		{
			_logger.LogWarning("[FirstRunBaselineOrchestrator][RunPreflightAsync] Mirror path probe failed but is non-blocking. path={MirrorPath}, reason={Reason}", mirrorProbe.NormalizedPath, mirrorProbe.Message);
			Report(progress, runtime, BaselineFlowStep.Preflight, "镜像目录不可用，已自动降级为仅主路径（" + mirrorProbe.Message + "）。");
		}
		Report(progress, runtime, BaselineFlowStep.Preflight, "正在检查磁盘空间。");
		ResourceCheckResult diskCheck = SystemResourceChecker.CheckDiskSpace(primaryProbe.NormalizedPath, 268435456L);
		if (!diskCheck.Success)
		{
			return (Success: false, ErrorMessage: diskCheck.ErrorMessage ?? "磁盘空间不足。", LockHandle: null);
		}
		Report(progress, runtime, BaselineFlowStep.Preflight, "正在检查系统还原能力。");
		var (canCreateRestorePoint, restoreReason) = _systemRestoreService.CanCreateRestorePoint();
		if (!canCreateRestorePoint)
		{
			return (Success: false, ErrorMessage: "系统还原能力检查未通过：" + restoreReason, LockHandle: null);
		}
		Report(progress, runtime, BaselineFlowStep.Preflight, "正在检查系统命令能力。");
		(bool Success, string Message) commandCheck = BaselinePreflightCapabilityChecker.CheckCommandCapability();
		if (!commandCheck.Success)
		{
			return (Success: false, ErrorMessage: "系统命令能力检查未通过：" + commandCheck.Message, LockHandle: null);
		}
		Report(progress, runtime, BaselineFlowStep.Preflight, "正在检查并发锁。");
		BaselinePreflightLockHandle? preflightLockHandle = await TryAcquirePreflightLockAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		if (preflightLockHandle == null)
		{
			return (Success: false, ErrorMessage: "检测到已有首次基线备份流程正在运行，请等待当前流程结束后重试。", LockHandle: null);
		}
		return (Success: true, ErrorMessage: string.Empty, LockHandle: preflightLockHandle);
	}

	private async Task<BaselinePreflightLockHandle?> TryAcquirePreflightLockAsync(CancellationToken ct)
	{
		if (!(await BaselineFlowLock.WaitAsync(0, ct).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return null;
		}
		FileStream? lockStream = null;
		try
		{
			Directory.CreateDirectory(BaselineRoot);
			lockStream = new FileStream(BaselineFlowLockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
			byte[] marker = Encoding.UTF8.GetBytes($"pid={Environment.ProcessId};utc={DateTime.UtcNow:O}");
			lockStream.SetLength(0L);
			await lockStream.WriteAsync(marker.AsMemory(0, marker.Length), ct).ConfigureAwait(continueOnCapturedContext: false);
			await lockStream.FlushAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			return new BaselinePreflightLockHandle(lockStream);
		}
		catch (IOException exception)
		{
			_logger.LogWarning(exception, "[FirstRunBaselineOrchestrator][TryAcquirePreflightLockAsync] Lock file is busy. file={LockFile}", BaselineFlowLockFilePath);
			lockStream?.Dispose();
			BaselineFlowLock.Release();
			return null;
		}
		catch
		{
			lockStream?.Dispose();
			BaselineFlowLock.Release();
			throw;
		}
	}

	private async Task<(bool Success, string Message)> HandleRestorePointDomainAsync(BaselineRuntimeState runtime, string packageDirectory, BaselineDomainDefinition definition, CancellationToken ct)
	{
		BaselineDomainProgress domainProgress = runtime.Domains.First((BaselineDomainProgress domain) => domain.DomainId == definition.DomainId);
		BaselineManualGuideCheckResult manualResult = await _manualGuideService.RecheckRestorePointAsync(runtime.PackageId, runtime.StartedUtc, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!manualResult.Success)
		{
			domainProgress.Status = BaselineDomainStatus.ManualRequired;
			domainProgress.ErrorCode = "BASELINE.D13.MANUAL_REQUIRED";
			domainProgress.ErrorMessage = manualResult.Message;
			domainProgress.FinishedUtc = DateTime.UtcNow;
			await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
			return (Success: false, Message: manualResult.Message);
		}
		domainProgress.Status = BaselineDomainStatus.Succeeded;
		domainProgress.ErrorCode = null;
		domainProgress.ErrorMessage = null;
		domainProgress.FinishedUtc = DateTime.UtcNow;
		runtime.ManualGuideCompleted = true;
		await WriteManualDomainArtifactAsync(packageDirectory, domainProgress, ct).ConfigureAwait(continueOnCapturedContext: false);
		await _runtimeStateStore.SaveAsync(runtime, ct).ConfigureAwait(continueOnCapturedContext: false);
		return (Success: true, Message: string.Empty);
	}

	private static async Task WriteManualDomainArtifactAsync(string packageDirectory, BaselineDomainProgress domain, CancellationToken ct)
	{
		string artifactPath = Path.Combine(packageDirectory, domain.DomainId + ".json");
		var payload = new
		{
			domainId = domain.DomainId,
			domainName = domain.DomainName,
			status = domain.Status.ToString(),
			finishedUtc = (domain.FinishedUtc ?? DateTime.UtcNow),
			message = "System restore point verified."
		};
		string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
		{
			WriteIndented = true
		});
		await File.WriteAllTextAsync(artifactPath, json, Encoding.UTF8, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static BaselineRuntimeState EnsureDomainList(BaselineRuntimeState runtime)
	{
		if (runtime.Domains == null)
		{
			List<BaselineDomainProgress> list = (runtime.Domains = new List<BaselineDomainProgress>());
		}
		return runtime;
	}

	private async Task<BaselinePackageManifest> BuildManifestAsync(BaselineRuntimeState runtime, string packageDirectory, CancellationToken ct)
	{
		List<BaselineDomainArtifact> domains = new List<BaselineDomainArtifact>();
		foreach (BaselineDomainDefinition definition in _domainCollector.GetDefinitions())
		{
			BaselineDomainProgress? state = runtime.Domains.FirstOrDefault((BaselineDomainProgress domain) => domain.DomainId == definition.DomainId);
			string bundleFileName = definition.DomainId + ".bundle.zip";
			string legacyFileName = definition.DomainId + ".json";
			string bundlePath = Path.Combine(packageDirectory, bundleFileName);
			string legacyPath = Path.Combine(packageDirectory, legacyFileName);
			string artifactFileName = (File.Exists(bundlePath) ? bundleFileName : legacyFileName);
			string artifactPath = Path.Combine(packageDirectory, artifactFileName);
			if (!File.Exists(artifactPath))
			{
				domains.Add(new BaselineDomainArtifact
				{
					DomainId = definition.DomainId,
					DomainName = definition.DomainName,
					Required = definition.Required,
					Status = (state?.Status ?? BaselineDomainStatus.Failed),
					RelativePath = ((File.Exists(bundlePath) || File.Exists(legacyPath)) ? artifactFileName : bundleFileName),
					ErrorCode = (state?.ErrorCode ?? "BASELINE.ARTIFACT.MISSING"),
					ErrorMessage = (state?.ErrorMessage ?? "Artifact file is missing.")
				});
			}
			else
			{
				FileInfo fileInfo = new FileInfo(artifactPath);
				domains.Add(new BaselineDomainArtifact
				{
					DomainId = definition.DomainId,
					DomainName = definition.DomainName,
					Required = definition.Required,
					Status = (state?.Status ?? BaselineDomainStatus.Succeeded),
					RelativePath = artifactFileName,
					SizeBytes = fileInfo.Length,
					Sha256 = BaselineHashHelper.ComputeSha256(artifactPath),
					ErrorCode = state?.ErrorCode,
					ErrorMessage = state?.ErrorMessage
				});
			}
		}
		await Task.CompletedTask;
		string mirrorDirectory = BaselineMirrorDirectoryPolicy.GetConfiguredMirrorDirectory();
		BaselinePackageManifest baselinePackageManifest = new BaselinePackageManifest();
		baselinePackageManifest.PackageId = runtime.PackageId;
		baselinePackageManifest.PackageType = "BaselineFirstRun";
		baselinePackageManifest.CreationSource = NormalizeCreationSource(runtime.CreationSource);
		baselinePackageManifest.LifecycleState = "Creating";
		baselinePackageManifest.AppVersion = typeof(FirstRunBaselineOrchestrator).Assembly.GetName().Version?.ToString() ?? "0.0.0";
		baselinePackageManifest.OsVersion = Environment.OSVersion.VersionString;
		baselinePackageManifest.EnvironmentBinding = BaselineEnvironmentBindingPolicy.CaptureCurrent();
		baselinePackageManifest.CreatedUtc = runtime.StartedUtc;
		baselinePackageManifest.UpdatedUtc = DateTime.UtcNow;
		baselinePackageManifest.BaselineState = FirstRunBaselineState.Running;
		baselinePackageManifest.IntegrityState = "Verifying";
		baselinePackageManifest.MirrorDirectory = mirrorDirectory;
		baselinePackageManifest.MirrorSyncState = (string.IsNullOrWhiteSpace(mirrorDirectory) ? "NotConfigured" : "Pending");
		baselinePackageManifest.MirrorSyncMessage = (string.IsNullOrWhiteSpace(mirrorDirectory) ? "未配置镜像目录，后续仅写入主路径。" : "等待镜像同步。");
		baselinePackageManifest.Domains = domains;
		return baselinePackageManifest;
	}

	private static void ApplyMirrorSyncResult(BaselinePackageManifest manifest, BaselineMirrorSyncResult syncResult)
	{
		manifest.MirrorDirectory = syncResult.MirrorDirectory;
		manifest.MirrorSyncState = syncResult.MirrorSyncState;
		manifest.MirrorSyncMessage = syncResult.MirrorSyncMessage;
		manifest.MirrorSyncedUtc = syncResult.MirrorSyncedUtc;
	}

	private static string BuildPackageId()
	{
		return $"BL-{DateTime.Now:yyyyMMdd-HHmmss}";
	}

	private static string NormalizeCreationSource(string? creationSource)
	{
		if (string.IsNullOrWhiteSpace(creationSource))
		{
			return "FirstRunMandatory";
		}
		return creationSource.Trim();
	}

	private static void Report(IProgress<BaselineProgressEvent>? progress, BaselineRuntimeState runtime, BaselineFlowStep step, string message, string? domainId = null, BaselineDomainStatus? status = null)
	{
		if (progress != null)
		{
			int completedDomains = runtime.Domains.Count((BaselineDomainProgress domain) => domain.Status == BaselineDomainStatus.Succeeded);
			progress.Report(new BaselineProgressEvent
			{
				Step = step,
				Message = message,
				DomainId = domainId,
				DomainStatus = status,
				CompletedDomains = completedDomains,
				TotalDomains = runtime.Domains.Count
			});
		}
	}
}
