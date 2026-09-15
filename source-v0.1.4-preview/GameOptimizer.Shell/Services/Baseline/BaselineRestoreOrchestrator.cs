using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Snapshot;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineRestoreOrchestrator : IBaselineRestoreOrchestrator
{
	private sealed class RestoreExecutionScope : IDisposable
	{
		private readonly IGameAutoOptimizationService? _gameAutoOptimizationService;

		private readonly GameCoreGuardService? _gameCoreGuardService;

		private readonly bool _resumeAutoOptimization;

		private readonly bool _resumeCoreGuard;

		private readonly ILogger _logger;

		private bool _disposed;

		public RestoreExecutionScope(IGameAutoOptimizationService? gameAutoOptimizationService, GameCoreGuardService? gameCoreGuardService, bool resumeAutoOptimization, bool resumeCoreGuard, ILogger logger)
		{
			_gameAutoOptimizationService = gameAutoOptimizationService;
			_gameCoreGuardService = gameCoreGuardService;
			_resumeAutoOptimization = resumeAutoOptimization;
			_resumeCoreGuard = resumeCoreGuard;
			_logger = logger;
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}
			_disposed = true;
			if (_resumeAutoOptimization && _gameAutoOptimizationService != null)
			{
				try
				{
					_gameAutoOptimizationService.Start();
					_logger.LogInformation("[BaselineRestoreOrchestrator] Auto optimization service resumed.");
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "[BaselineRestoreOrchestrator] Failed to resume auto optimization service.");
				}
			}
			if (!_resumeCoreGuard || _gameCoreGuardService == null)
			{
				return;
			}
			try
			{
				_gameCoreGuardService.StartGuard();
				_logger.LogInformation("[BaselineRestoreOrchestrator] Core guard service resumed.");
			}
			catch (Exception exception2)
			{
				_logger.LogWarning(exception2, "[BaselineRestoreOrchestrator] Failed to resume core guard service.");
			}
		}
	}

	private static readonly string BaselineReportsDir = Path.Combine(AppPaths.BackupsDir, "Baseline", "restore-reports");

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private readonly IBaselineCatalogService _catalogService;

	private readonly ISnapshotService _snapshotService;

	private readonly IBaselineRestoreScoringService _restoreScoringService;

	private readonly IGameAutoOptimizationService? _gameAutoOptimizationService;

	private readonly GameCoreGuardService? _gameCoreGuardService;

	private readonly BaselineOwnedFileDomainRestoreOwner _ownedFileDomainRestoreOwner;

	private readonly ILogger<BaselineRestoreOrchestrator> _logger;

	public BaselineRestoreOrchestrator(IBaselineCatalogService catalogService, ISnapshotService snapshotService, IBaselineRestoreScoringService restoreScoringService, ILogger<BaselineRestoreOrchestrator> logger, IGameAutoOptimizationService? gameAutoOptimizationService = null, GameCoreGuardService? gameCoreGuardService = null)
	{
		_catalogService = catalogService ?? throw new ArgumentNullException("catalogService");
		_snapshotService = snapshotService ?? throw new ArgumentNullException("snapshotService");
		_restoreScoringService = restoreScoringService ?? throw new ArgumentNullException("restoreScoringService");
		_gameAutoOptimizationService = gameAutoOptimizationService;
		_gameCoreGuardService = gameCoreGuardService;
		_ownedFileDomainRestoreOwner = new BaselineOwnedFileDomainRestoreOwner();
		_logger = logger ?? throw new ArgumentNullException("logger");
	}

	public async Task<BaselineRestoreResult> RestoreAsync(string packageId, IProgress<BaselineRestoreProgressEvent>? progress = null, CancellationToken ct = default(CancellationToken), BaselineRestoreMode mode = BaselineRestoreMode.Full)
	{
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return new BaselineRestoreResult
			{
				Success = false,
				Message = "基线包ID为空。",
				Report = CreateReport(packageId)
			};
		}
		BaselineRestoreExecutionReport report = CreateReport(packageId.Trim());
		RestoreExecutionScope? restoreExecutionScope = null;
		try
		{
			string modeText = ResolveModeText(mode);
			Report(progress, BaselineRestoreStep.Precheck, "正在执行" + modeText + "预检。", null, null, 0, 0);
			(bool Success, string ErrorMessage) verify = await _catalogService.VerifyPackageAsync(report.SourcePackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!verify.Success)
			{
				report.PrecheckStatus = "Failed";
				report.PostVerificationSummary = "预检失败：" + verify.ErrorMessage;
				report.FinishedUtc = DateTime.UtcNow;
				BaselineRestoreExecutionReport baselineRestoreExecutionReport = report;
				baselineRestoreExecutionReport.ReportFilePath = await WriteReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				Report(progress, BaselineRestoreStep.Failed, report.PostVerificationSummary, null, BaselineDomainStatus.Failed, 0, 0);
				return new BaselineRestoreResult
				{
					Success = false,
					Message = report.PostVerificationSummary,
					Report = report
				};
			}
			BaselinePackageManifest? manifest = await _catalogService.GetManifestAsync(report.SourcePackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (manifest == null)
			{
				report.PrecheckStatus = "Failed";
				report.PostVerificationSummary = "预检失败：未找到基线清单。";
				report.FinishedUtc = DateTime.UtcNow;
				BaselineRestoreExecutionReport baselineRestoreExecutionReport2 = report;
				baselineRestoreExecutionReport2.ReportFilePath = await WriteReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				Report(progress, BaselineRestoreStep.Failed, report.PostVerificationSummary, null, BaselineDomainStatus.Failed, 0, 0);
				return new BaselineRestoreResult
				{
					Success = false,
					Message = report.PostVerificationSummary,
					Report = report
				};
			}
			BaselineEnvironmentCompatibilityResult environmentCheck = BaselineEnvironmentBindingPolicy.Evaluate(
				manifest.EnvironmentBinding,
				BaselineEnvironmentBindingPolicy.CaptureCurrent());
			if (!environmentCheck.IsCompatible)
			{
				report.PrecheckStatus = "Failed";
				report.PostVerificationSummary = "预检失败：" + environmentCheck.Message;
				report.FinishedUtc = DateTime.UtcNow;
				report.ReportFilePath = await WriteReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				Report(progress, BaselineRestoreStep.Failed, report.PostVerificationSummary, null, BaselineDomainStatus.Failed, 0, 0);
				return new BaselineRestoreResult
				{
					Success = false,
					Message = report.PostVerificationSummary,
					Report = report
				};
			}
			report.PrecheckStatus = "Passed";
			Report(progress, BaselineRestoreStep.Precheck, "预检通过，准备创建应急备份。", null, null, 0, manifest.Domains.Count);
			if (mode == BaselineRestoreMode.DryRun)
			{
				Report(progress, BaselineRestoreStep.PostVerify, "正在执行恢复演练校验（只读，不落盘）。", null, null, 0, manifest.Domains.Count);
				BaselineRecoverabilityCheckResult dryRunCheck = await RunRecoverabilitySelfCheckAsync(report.SourcePackageId, "restore_flow_dry_run", ct).ConfigureAwait(continueOnCapturedContext: false);
				report.SuccessCount = Math.Max(0, dryRunCheck.DomainCheckedCount - dryRunCheck.DomainFailedCount);
				report.FailedCount = dryRunCheck.DomainFailedCount;
				report.SkippedCount = 0;
				report.PostVerificationSummary = dryRunCheck.Message;
				report.RestoreConfidenceScore = (dryRunCheck.Success ? 100 : Math.Clamp(100 - dryRunCheck.DomainFailedCount * 12, 0, 94));
				report.NextActions = (dryRunCheck.Success ? new List<string> { "恢复演练通过，可按需执行正式恢复。", "建议继续保留至少两份可用基线包。" } : new List<string> { "恢复演练未通过，请先修复失败域后再执行正式恢复。", "可在备份中心查看自检报告并按域处理。" });
				report.ReportFilePath = dryRunCheck.ReportFilePath;
				report.FinishedUtc = DateTime.UtcNow;
				Report(progress, dryRunCheck.Success ? BaselineRestoreStep.Completed : BaselineRestoreStep.Failed, dryRunCheck.Message, null, dryRunCheck.Success ? BaselineDomainStatus.Succeeded : BaselineDomainStatus.Failed, dryRunCheck.DomainCheckedCount - dryRunCheck.DomainFailedCount, dryRunCheck.DomainCheckedCount);
				return new BaselineRestoreResult
				{
					Success = dryRunCheck.Success,
					Message = dryRunCheck.Message,
					Report = report
				};
			}
			Report(progress, BaselineRestoreStep.EmergencyBackup, "正在创建恢复前应急快照。", null, null, 0, manifest.Domains.Count);
			(bool Success, string SnapshotId, string Message) emergencySnapshot = await CreateEmergencySnapshotAsync(report.SourcePackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!emergencySnapshot.Success)
			{
				report.PrecheckStatus = "Passed";
				report.PostVerificationSummary = emergencySnapshot.Message;
				report.FinishedUtc = DateTime.UtcNow;
				BaselineRestoreExecutionReport baselineRestoreExecutionReport3 = report;
				baselineRestoreExecutionReport3.ReportFilePath = await WriteReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				Report(progress, BaselineRestoreStep.Failed, emergencySnapshot.Message, null, BaselineDomainStatus.Failed, 0, manifest.Domains.Count);
				return new BaselineRestoreResult
				{
					Success = false,
					Message = emergencySnapshot.Message,
					Report = report
				};
			}
			report.EmergencySnapshotCreated = true;
			report.EmergencySnapshotId = emergencySnapshot.SnapshotId;
			Report(progress, BaselineRestoreStep.EmergencyBackup, "应急快照创建成功：" + emergencySnapshot.SnapshotId, null, null, 0, manifest.Domains.Count);
			Report(progress, BaselineRestoreStep.EmergencyBackup, "正在暂停后台自动优化与守护能力。", null, null, 0, manifest.Domains.Count);
			restoreExecutionScope = PauseBackgroundExecutionServices();
			string? packageDirectory = await _catalogService.GetPackageDirectoryAsync(report.SourcePackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(packageDirectory))
			{
				report.PostVerificationSummary = "恢复失败：未找到基线包目录。";
				report.FinishedUtc = DateTime.UtcNow;
				BaselineRestoreExecutionReport baselineRestoreExecutionReport4 = report;
				baselineRestoreExecutionReport4.ReportFilePath = await WriteReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				Report(progress, BaselineRestoreStep.Failed, report.PostVerificationSummary, null, BaselineDomainStatus.Failed, 0, manifest.Domains.Count);
				return new BaselineRestoreResult
				{
					Success = false,
					Message = report.PostVerificationSummary,
					Report = report
				};
			}
			List<BaselineDomainArtifact> selectedDomains = SelectDomainsByMode(manifest.Domains, mode);
			List<BaselineDomainArtifact> orderedDomains = OrderDomainsByRestorePlan(selectedDomains);
			int totalDomains = orderedDomains.Count;
			if (totalDomains == 0)
			{
				report.PostVerificationSummary = "恢复失败：" + modeText + "未匹配到可恢复域。";
				report.FinishedUtc = DateTime.UtcNow;
				BaselineRestoreExecutionReport baselineRestoreExecutionReport5 = report;
				baselineRestoreExecutionReport5.ReportFilePath = await WriteReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				Report(progress, BaselineRestoreStep.Failed, report.PostVerificationSummary, null, BaselineDomainStatus.Failed, 0, 0);
				return new BaselineRestoreResult
				{
					Success = false,
					Message = report.PostVerificationSummary,
					Report = report
				};
			}
			int completedDomains = 0;
			foreach (BaselineDomainArtifact domain in orderedDomains)
			{
				ct.ThrowIfCancellationRequested();
				Report(progress, BaselineRestoreStep.DomainRestore, "正在恢复 " + domain.DomainId + "。", domain.DomainId, BaselineDomainStatus.Running, completedDomains, totalDomains);
				BaselineRestoreDomainResult domainResult = await RestoreDomainAsync(domain, packageDirectory, ct).ConfigureAwait(continueOnCapturedContext: false);
				report.DomainResults.Add(domainResult);
				completedDomains++;
				if (domainResult.Status == BaselineDomainStatus.Succeeded)
				{
					report.SuccessCount++;
				}
				else if (domainResult.Status == BaselineDomainStatus.Skipped || domainResult.Status == BaselineDomainStatus.Warning || domainResult.Status == BaselineDomainStatus.ManualRequired)
				{
					report.SkippedCount++;
				}
				else
				{
					report.FailedCount++;
				}
				if (DomainMayRequireReboot(domain.DomainId))
				{
					report.RequiresReboot = true;
				}
				Report(progress, BaselineRestoreStep.DomainRestore, $"{domain.DomainId} {ToResultText(domainResult.Status)}：{domainResult.Message}", domain.DomainId, domainResult.Status, completedDomains, totalDomains);
			}
			Report(progress, BaselineRestoreStep.PostVerify, "正在生成恢复后校验报告。", null, null, completedDomains, totalDomains);
			report.RestoreConfidenceScore = _restoreScoringService.CalculateScore(report);
			report.NextActions = BuildNextActions(report);
			report.PostVerificationSummary = BuildPostVerifySummary(report);
			report.FinishedUtc = DateTime.UtcNow;
			BaselineRestoreExecutionReport baselineRestoreExecutionReport6 = report;
			baselineRestoreExecutionReport6.ReportFilePath = await WriteReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
			bool success = HasCompleteRestoreEvidence(report, totalDomains);
			string finalMessage = BuildFinalMessage(modeText, report, success);
			Report(progress, success ? BaselineRestoreStep.Completed : BaselineRestoreStep.Failed, finalMessage, null, success ? BaselineDomainStatus.Succeeded : BaselineDomainStatus.Failed, completedDomains, totalDomains);
			return new BaselineRestoreResult
			{
				Success = success,
				Message = finalMessage,
				Report = report
			};
		}
		catch (OperationCanceledException)
		{
			report.PostVerificationSummary = "恢复流程已取消。";
			report.FinishedUtc = DateTime.UtcNow;
			BaselineRestoreExecutionReport baselineRestoreExecutionReport7 = report;
			baselineRestoreExecutionReport7.ReportFilePath = await WriteReportAsync(report, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			Report(progress, BaselineRestoreStep.Failed, report.PostVerificationSummary, null, BaselineDomainStatus.Failed, 0, 0);
			return new BaselineRestoreResult
			{
				Success = false,
				Message = report.PostVerificationSummary,
				Report = report
			};
		}
		catch (Exception ex2)
		{
			_logger.LogWarning(ex2, "[BaselineRestoreOrchestrator][RestoreAsync] Restore failed. package={PackageId}", report.SourcePackageId);
			report.PostVerificationSummary = "恢复异常：" + ex2.Message;
			report.FinishedUtc = DateTime.UtcNow;
			BaselineRestoreExecutionReport baselineRestoreExecutionReport8 = report;
			baselineRestoreExecutionReport8.ReportFilePath = await WriteReportAsync(report, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			Report(progress, BaselineRestoreStep.Failed, report.PostVerificationSummary, null, BaselineDomainStatus.Failed, 0, 0);
			return new BaselineRestoreResult
			{
				Success = false,
				Message = report.PostVerificationSummary,
				Report = report
			};
		}
		finally
		{
			restoreExecutionScope?.Dispose();
		}
	}

	public async Task<BaselineRecoverabilityCheckResult> RunRecoverabilitySelfCheckAsync(string packageId, string trigger, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return new BaselineRecoverabilityCheckResult
			{
				Success = false,
				PackageId = string.Empty,
				Message = "包ID为空，无法执行恢复可用性自检。"
			};
		}
		string normalizedPackageId = packageId.Trim();
		string normalizedTrigger = (string.IsNullOrWhiteSpace(trigger) ? "manual" : trigger.Trim());
		BaselineRecoverabilityCheckReport report = new BaselineRecoverabilityCheckReport
		{
			CheckId = $"DR-{DateTime.Now:yyyyMMddHHmmss}",
			PackageId = normalizedPackageId,
			Trigger = normalizedTrigger,
			StartedUtc = DateTime.UtcNow
		};
		try
		{
			(bool Success, string ErrorMessage) verify = await _catalogService.VerifyPackageAsync(normalizedPackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
			(report.VerifyPassed, _) = verify;
			if (!verify.Success)
			{
				report.DomainFailedCount = 1;
				report.Summary = "自检失败：包完整性校验未通过（" + verify.ErrorMessage + "）";
				report.FinishedUtc = DateTime.UtcNow;
				string reportFilePath = await WriteRecoverabilityReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				await UpdateManifestRecoverabilityStateAsync(normalizedPackageId, success: false, report.Summary, report.FinishedUtc, ct).ConfigureAwait(continueOnCapturedContext: false);
				return new BaselineRecoverabilityCheckResult
				{
					Success = false,
					PackageId = normalizedPackageId,
					CheckedUtc = report.FinishedUtc,
					DomainCheckedCount = report.DomainCheckedCount,
					DomainFailedCount = report.DomainFailedCount,
					Message = report.Summary,
					ReportFilePath = reportFilePath
				};
			}
			BaselinePackageManifest? manifest = await _catalogService.GetManifestAsync(normalizedPackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (manifest == null)
			{
				report.DomainFailedCount = 1;
				report.Summary = "自检失败：未找到基线清单。";
				report.FinishedUtc = DateTime.UtcNow;
				string reportPath = await WriteRecoverabilityReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				await UpdateManifestRecoverabilityStateAsync(normalizedPackageId, success: false, report.Summary, report.FinishedUtc, ct).ConfigureAwait(continueOnCapturedContext: false);
				return new BaselineRecoverabilityCheckResult
				{
					Success = false,
					PackageId = normalizedPackageId,
					CheckedUtc = report.FinishedUtc,
					DomainCheckedCount = report.DomainCheckedCount,
					DomainFailedCount = report.DomainFailedCount,
					Message = report.Summary,
					ReportFilePath = reportPath
				};
			}
			string? packageDirectory = await _catalogService.GetPackageDirectoryAsync(normalizedPackageId, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (string.IsNullOrWhiteSpace(packageDirectory))
			{
				report.DomainFailedCount = 1;
				report.Summary = "自检失败：未找到基线目录。";
				report.FinishedUtc = DateTime.UtcNow;
				string reportPath2 = await WriteRecoverabilityReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
				await UpdateManifestRecoverabilityStateAsync(normalizedPackageId, success: false, report.Summary, report.FinishedUtc, ct).ConfigureAwait(continueOnCapturedContext: false);
				return new BaselineRecoverabilityCheckResult
				{
					Success = false,
					PackageId = normalizedPackageId,
					CheckedUtc = report.FinishedUtc,
					DomainCheckedCount = report.DomainCheckedCount,
					DomainFailedCount = report.DomainFailedCount,
					Message = report.Summary,
					ReportFilePath = reportPath2
				};
			}
			foreach (BaselineDomainArtifact domain in manifest.Domains.OrderBy<BaselineDomainArtifact, string>((BaselineDomainArtifact item) => item.DomainId, StringComparer.OrdinalIgnoreCase))
			{
				ct.ThrowIfCancellationRequested();
				BaselineRecoverabilityDomainCheckItem checkItem = await ValidateRecoverabilityArtifactAsync(domain, packageDirectory, ct).ConfigureAwait(continueOnCapturedContext: false);
				report.DomainChecks.Add(checkItem);
			}
			foreach (string requiredDomainId in BaselineRecoveryCoverageClassifier.GetBaselineAdmissionDomainIds())
			{
				if (report.DomainChecks.Any(item => string.Equals(item.DomainId, requiredDomainId, StringComparison.OrdinalIgnoreCase)))
				{
					continue;
				}

				report.DomainChecks.Add(new BaselineRecoverabilityDomainCheckItem
				{
					DomainId = requiredDomainId,
					DomainName = "首次基线核心恢复域",
					Status = BaselineDomainStatus.Failed,
					Message = "首次基线缺少核心恢复域，不能建立已验证恢复保障。"
				});
			}
			report.DomainCheckedCount = report.DomainChecks.Count;
			report.DomainFailedCount = report.DomainChecks.Count((BaselineRecoverabilityDomainCheckItem item) => item.Status == BaselineDomainStatus.Failed);
			report.DomainManualRequiredCount = report.DomainChecks.Count((BaselineRecoverabilityDomainCheckItem item) => item.Status is BaselineDomainStatus.ManualRequired or BaselineDomainStatus.Warning or BaselineDomainStatus.Skipped);
			report.FinishedUtc = DateTime.UtcNow;
			bool admissionReady = report.DomainChecks
				.Where(item => BaselineRecoveryCoverageClassifier.IsRequiredForBaselineAdmission(item.DomainId))
				.All(item => item.Status == BaselineDomainStatus.Succeeded);
			bool success = report.VerifyPassed && admissionReady;
			report.Summary = success
				? $"恢复可用性自检通过：核心恢复域均具备受控恢复链；另有 {report.DomainManualRequiredCount} 个系统/审计域保留人工或默认恢复说明。"
				: BuildRecoverabilitySummary(report);
			string finalReportPath = await WriteRecoverabilityReportAsync(report, ct).ConfigureAwait(continueOnCapturedContext: false);
			await UpdateManifestRecoverabilityStateAsync(normalizedPackageId, success, report.Summary, report.FinishedUtc, ct).ConfigureAwait(continueOnCapturedContext: false);
			return new BaselineRecoverabilityCheckResult
			{
				Success = success,
				PackageId = normalizedPackageId,
				CheckedUtc = report.FinishedUtc,
				DomainCheckedCount = report.DomainCheckedCount,
				DomainFailedCount = report.DomainFailedCount,
				DomainManualRequiredCount = report.DomainManualRequiredCount,
				Message = report.Summary,
				ReportFilePath = finalReportPath
			};
		}
		catch (OperationCanceledException)
		{
			report.FinishedUtc = DateTime.UtcNow;
			report.Summary = "恢复可用性自检已取消。";
			string reportPath3 = await WriteRecoverabilityReportAsync(report, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			return new BaselineRecoverabilityCheckResult
			{
				Success = false,
				PackageId = normalizedPackageId,
				CheckedUtc = report.FinishedUtc,
				DomainCheckedCount = report.DomainCheckedCount,
				DomainFailedCount = report.DomainFailedCount,
				Message = report.Summary,
				ReportFilePath = reportPath3
			};
		}
		catch (Exception ex2)
		{
			_logger.LogWarning(ex2, "[BaselineRestoreOrchestrator][RunRecoverabilitySelfCheckAsync] Self-check failed. package={PackageId}", normalizedPackageId);
			report.FinishedUtc = DateTime.UtcNow;
			report.Summary = "恢复可用性自检异常：" + ex2.Message;
			string reportPath4 = await WriteRecoverabilityReportAsync(report, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			await UpdateManifestRecoverabilityStateAsync(normalizedPackageId, success: false, report.Summary, report.FinishedUtc, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			return new BaselineRecoverabilityCheckResult
			{
				Success = false,
				PackageId = normalizedPackageId,
				CheckedUtc = report.FinishedUtc,
				DomainCheckedCount = report.DomainCheckedCount,
				DomainFailedCount = report.DomainFailedCount,
				Message = report.Summary,
				ReportFilePath = reportPath4
			};
		}
	}

	private RestoreExecutionScope PauseBackgroundExecutionServices()
	{
		bool resumeAutoOptimization = false;
		bool resumeCoreGuard = false;
		if (_gameAutoOptimizationService != null && _gameAutoOptimizationService.IsRunning)
		{
			try
			{
				_gameAutoOptimizationService.Stop();
				resumeAutoOptimization = true;
				_logger.LogInformation("[BaselineRestoreOrchestrator] Auto optimization service paused for restore.");
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "[BaselineRestoreOrchestrator] Failed to pause auto optimization service.");
			}
		}
		if (_gameCoreGuardService != null && _gameCoreGuardService.IsGuardRunning)
		{
			try
			{
				_gameCoreGuardService.StopGuard();
				resumeCoreGuard = true;
				_logger.LogInformation("[BaselineRestoreOrchestrator] Core guard service paused for restore.");
			}
			catch (Exception exception2)
			{
				_logger.LogWarning(exception2, "[BaselineRestoreOrchestrator] Failed to pause core guard service.");
			}
		}
		return new RestoreExecutionScope(_gameAutoOptimizationService, _gameCoreGuardService, resumeAutoOptimization, resumeCoreGuard, _logger);
	}

	private async Task<(bool Success, string SnapshotId, string Message)> CreateEmergencySnapshotAsync(string packageId, CancellationToken ct)
	{
		try
		{
			string snapshotName = $"EmergencyBeforeBaselineRestore_{DateTime.Now:yyyyMMdd_HHmmss}_{packageId}";
			return (Success: true, SnapshotId: (await _snapshotService.CreateSnapshotAsync(snapshotName, "Emergency snapshot before baseline restore " + packageId, ct).ConfigureAwait(continueOnCapturedContext: false)).Id, Message: "应急快照创建成功。");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.LogWarning(ex2, "[BaselineRestoreOrchestrator][CreateEmergencySnapshotAsync] Failed.");
			return (Success: false, SnapshotId: string.Empty, Message: "创建应急快照失败：" + ex2.Message);
		}
	}

	private async Task<BaselineRestoreDomainResult> RestoreDomainAsync(BaselineDomainArtifact domain, string packageDirectory, CancellationToken ct)
	{
		BaselineRestoreDomainResult result = new BaselineRestoreDomainResult
		{
			DomainId = domain.DomainId,
			DomainName = domain.DomainName,
			StartedUtc = DateTime.UtcNow
		};
		string relativePath = (string.IsNullOrWhiteSpace(domain.RelativePath) ? (domain.DomainId + ".bundle.zip") : domain.RelativePath);
		string artifactPath = Path.Combine(packageDirectory, relativePath);
		if (!File.Exists(artifactPath))
		{
			result.Status = (domain.Required ? BaselineDomainStatus.Failed : BaselineDomainStatus.Skipped);
			result.ErrorCode = (domain.Required ? "BASELINE.RESTORE.ARTIFACT_MISSING" : null);
			result.Message = (domain.Required ? "恢复资产缺失。" : "可选资产缺失，已跳过。");
			result.FinishedUtc = DateTime.UtcNow;
			return result;
		}
		if (relativePath.EndsWith(".bundle.zip", StringComparison.OrdinalIgnoreCase))
		{
			return await RestoreBundleDomainAsync(domain, artifactPath, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await RestoreLegacyJsonDomainAsync(domain, artifactPath, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async Task<BaselineRestoreDomainResult> RestoreLegacyJsonDomainAsync(BaselineDomainArtifact domain, string artifactPath, CancellationToken ct)
	{
		BaselineRestoreDomainResult result = new BaselineRestoreDomainResult
		{
			DomainId = domain.DomainId,
			DomainName = domain.DomainName,
			StartedUtc = DateTime.UtcNow
		};
		try
		{
			await using FileStream stream = File.OpenRead(artifactPath);
			await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), ct).ConfigureAwait(continueOnCapturedContext: false);
			// Parsing an old JSON asset proves only that it can be read. It does not
			// execute a typed restore or a readback verification, so it must never be
			// projected as an automatically restored domain.
			result.Status = BaselineDomainStatus.ManualRequired;
			result.ErrorCode = "BASELINE.RESTORE.LEGACY_MANUAL_REQUIRED";
			result.Message = "旧版域资产仅完成可读性校验，需通过受控 Owner 或人工流程恢复。";
		}
		catch (JsonException)
		{
			result.Status = BaselineDomainStatus.Failed;
			result.ErrorCode = "BASELINE.RESTORE.LEGACY_JSON_INVALID";
			result.Message = "恢复资产格式损坏。";
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			result.Status = BaselineDomainStatus.Failed;
			result.ErrorCode = "BASELINE.RESTORE.LEGACY_EXCEPTION";
			result.Message = ex3.Message;
		}
		result.FinishedUtc = DateTime.UtcNow;
		return result;
	}

	private async Task<BaselineRestoreDomainResult> RestoreBundleDomainAsync(BaselineDomainArtifact domain, string artifactPath, CancellationToken ct)
	{
		BaselineRestoreDomainResult result = new BaselineRestoreDomainResult
		{
			DomainId = domain.DomainId,
			DomainName = domain.DomainName,
			StartedUtc = DateTime.UtcNow
		};
		string extractDirectory = Path.Combine(BaselineReportsDir, $".bundle_extract_{domain.DomainId}_{Guid.NewGuid():N}");
		Directory.CreateDirectory(extractDirectory);
		try
		{
			await Task.Run(delegate
			{
				ZipFile.ExtractToDirectory(artifactPath, extractDirectory, overwriteFiles: true);
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			string manifestPath = Path.Combine(extractDirectory, "domain-manifest.json");
			if (!File.Exists(manifestPath))
			{
				result.Status = BaselineDomainStatus.Failed;
				result.ErrorCode = "BASELINE.RESTORE.BUNDLE_MANIFEST_MISSING";
				result.Message = "域包缺少 domain-manifest.json。";
				result.FinishedUtc = DateTime.UtcNow;
				return result;
			}
			BaselineDomainBundleManifest? manifest = JsonSerializer.Deserialize<BaselineDomainBundleManifest>(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(continueOnCapturedContext: false), new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});
			if (manifest == null)
			{
				result.Status = BaselineDomainStatus.Failed;
				result.ErrorCode = "BASELINE.RESTORE.BUNDLE_MANIFEST_INVALID";
				result.Message = "域包清单解析失败。";
				result.FinishedUtc = DateTime.UtcNow;
				return result;
			}
			if (!string.Equals(manifest.DomainId, domain.DomainId, StringComparison.OrdinalIgnoreCase))
			{
				result.Status = BaselineDomainStatus.Failed;
				result.ErrorCode = "BASELINE.RESTORE.BUNDLE_DOMAIN_MISMATCH";
				result.Message = "域包ID不匹配：清单=" + manifest.DomainId + ", 期望=" + domain.DomainId;
				result.FinishedUtc = DateTime.UtcNow;
				return result;
			}
			if (manifest.RestoreSteps.Count == 0)
			{
				result.Status = BaselineDomainStatus.ManualRequired;
				result.ErrorCode = "BASELINE.RESTORE.BUNDLE_NO_EXECUTABLE_STEPS";
				result.Message = "域包没有可执行的受控恢复步骤，已保留资产供人工处理。";
				return result;
			}
			if (manifest.RestoreSteps.Count == 1
				&& string.Equals(manifest.RestoreSteps[0].StepType, "OwnedFileDomain", StringComparison.OrdinalIgnoreCase))
			{
				BaselineOwnedFileDomainRestoreResult ownedResult = await _ownedFileDomainRestoreOwner.RestoreAsync(domain.DomainId, extractDirectory, ct).ConfigureAwait(continueOnCapturedContext: false);
				result.Status = ownedResult.Success ? BaselineDomainStatus.Succeeded : BaselineDomainStatus.Failed;
				result.ErrorCode = ownedResult.Success ? null : ownedResult.Cancelled ? "BASELINE.RESTORE.OWNED_FILE_CANCELLED" : "BASELINE.RESTORE.OWNED_FILE_FAILED";
				result.Message = ownedResult.Message;
				result.FinishedUtc = DateTime.UtcNow;
				return result;
			}
			if (manifest.RestoreSteps.Count == 1
				&& string.Equals(manifest.RestoreSteps[0].StepType, "TypedSnapshot", StringComparison.OrdinalIgnoreCase))
			{
				return await RestoreTypedSnapshotDomainAsync(domain, manifest.RestoreSteps[0], extractDirectory, ct).ConfigureAwait(false);
			}
			List<string> hardFailures = new List<string>();
			List<string> warnings = new List<string>();
			List<string> manualRequirements = new List<string>();
			foreach (BaselineDomainRestoreStep step in manifest.RestoreSteps)
			{
				ct.ThrowIfCancellationRequested();
				(bool Success, bool Warning, string Message) stepOutcome = await ExecuteRestoreStepAsync(step, extractDirectory, ct).ConfigureAwait(continueOnCapturedContext: false);
				bool requiresManualAction = RequiresManualCompletion(step);
				if (stepOutcome.Success)
				{
					if (stepOutcome.Warning)
					{
						if (requiresManualAction)
						{
							manualRequirements.Add(stepOutcome.Message);
						}
						else
						{
							warnings.Add(stepOutcome.Message);
						}
					}
				}
				else if (requiresManualAction && step.Optional)
				{
					manualRequirements.Add(stepOutcome.Message);
				}
				else if (step.Optional)
				{
					warnings.Add(stepOutcome.Message);
				}
				else
				{
					hardFailures.Add(stepOutcome.Message);
				}
			}
			if (hardFailures.Count > 0)
			{
				result.Status = BaselineDomainStatus.Failed;
				result.ErrorCode = "BASELINE.RESTORE.BUNDLE_STEP_FAILED";
				result.Message = string.Join(" | ", hardFailures);
			}
			else if (manualRequirements.Count > 0)
			{
				result.Status = BaselineDomainStatus.ManualRequired;
				result.ErrorCode = "BASELINE.RESTORE.BUNDLE_MANUAL_REQUIRED";
				result.Message = $"自动恢复未完成，存在 {manualRequirements.Count} 个需人工处理步骤。";
			}
			else if (warnings.Count > 0)
			{
				result.Status = BaselineDomainStatus.Warning;
				result.ErrorCode = "BASELINE.RESTORE.BUNDLE_STEP_WARNING";
				result.Message = $"自动恢复未完整完成，存在 {warnings.Count} 条提示。";
			}
			else
			{
				result.Status = BaselineDomainStatus.Succeeded;
				result.ErrorCode = null;
				result.Message = "域包恢复步骤已执行完成。";
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			result.Status = BaselineDomainStatus.Failed;
			result.ErrorCode = "BASELINE.RESTORE.BUNDLE_EXCEPTION";
			result.Message = ex2.Message;
		}
		finally
		{
			result.FinishedUtc = DateTime.UtcNow;
			TryDeleteDirectorySafe(extractDirectory);
		}
		return result;
	}

	private async Task<BaselineRestoreDomainResult> RestoreTypedSnapshotDomainAsync(
		BaselineDomainArtifact domain,
		BaselineDomainRestoreStep step,
		string extractDirectory,
		CancellationToken ct)
	{
		BaselineRestoreDomainResult result = new()
		{
			DomainId = domain.DomainId,
			DomainName = domain.DomainName,
			StartedUtc = DateTime.UtcNow
		};
		string? importedSnapshotId = null;
		try
		{
			string snapshotPath = ResolveBundleStepPath(extractDirectory, step.SourceRelativePath);
			if (!File.Exists(snapshotPath))
			{
				return CompleteTypedSnapshotFailure(result, "BASELINE.RESTORE.TYPED_SNAPSHOT_MISSING", "类型化设置快照文件缺失。 ");
			}

			OptimizationSnapshot? importedSnapshot = await _snapshotService.ImportSnapshotAsync(snapshotPath).ConfigureAwait(false);
			if (importedSnapshot is null || string.IsNullOrWhiteSpace(importedSnapshot.Id))
			{
				return CompleteTypedSnapshotFailure(result, "BASELINE.RESTORE.TYPED_SNAPSHOT_IMPORT_FAILED", "类型化设置快照未通过导入校验。 ");
			}

			importedSnapshotId = importedSnapshot.Id;
			SnapshotRestorePlan plan = await _snapshotService.PrepareRestoreAsync(importedSnapshotId, SnapshotRestoreKind.Normal, ct).ConfigureAwait(false);
			SnapshotRestoreResult restore = await _snapshotService.ExecuteRestoreAsync(plan, ct).ConfigureAwait(false);
			if (!restore.Success)
			{
				string message = string.IsNullOrWhiteSpace(restore.Message) ? "类型化设置恢复未通过执行或回读验证。" : restore.Message;
				return CompleteTypedSnapshotFailure(result, "BASELINE.RESTORE.TYPED_SNAPSHOT_NOT_VERIFIED", message);
			}

			result.Status = BaselineDomainStatus.Succeeded;
			result.Message = "类型化设置恢复已执行并通过现有恢复链回读验证。";
			result.FinishedUtc = DateTime.UtcNow;
			return result;
		}
		catch (OperationCanceledException)
		{
			return CompleteTypedSnapshotFailure(result, "BASELINE.RESTORE.TYPED_SNAPSHOT_CANCELLED", "类型化设置恢复已取消，未确认完成。 ");
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "[BaselineRestoreOrchestrator][RestoreTypedSnapshotDomainAsync] Typed snapshot restore failed. domain={DomainId}", domain.DomainId);
			return CompleteTypedSnapshotFailure(result, "BASELINE.RESTORE.TYPED_SNAPSHOT_EXCEPTION", ex.Message);
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(importedSnapshotId) && result.Status != BaselineDomainStatus.Succeeded)
			{
				try
				{
					await _snapshotService.DeleteSnapshotAsync(importedSnapshotId).ConfigureAwait(false);
				}
				catch (Exception cleanupFailure)
				{
					_logger.LogWarning(cleanupFailure, "[BaselineRestoreOrchestrator][RestoreTypedSnapshotDomainAsync] Failed to remove unsuccessful imported snapshot.");
				}
			}
		}
	}

	private static BaselineRestoreDomainResult CompleteTypedSnapshotFailure(BaselineRestoreDomainResult result, string errorCode, string message)
	{
		result.Status = BaselineDomainStatus.Failed;
		result.ErrorCode = errorCode;
		result.Message = message.Trim();
		result.FinishedUtc = DateTime.UtcNow;
		return result;
	}

	private static async Task<(bool Success, bool Warning, string Message)> ExecuteRestoreStepAsync(BaselineDomainRestoreStep step, string extractDirectory, CancellationToken ct)
	{
		switch ((step.StepType ?? string.Empty).Trim())
		{
		case "CopyTree":
		{
			string sourcePath = ResolveBundleStepPath(extractDirectory, step.SourceRelativePath);
			if (!Directory.Exists(sourcePath))
			{
				return (Success: false, Warning: step.Optional, Message: "步骤 CopyTree 源目录不存在：" + step.SourceRelativePath);
			}
			if (string.IsNullOrWhiteSpace(step.TargetPath))
			{
				return (Success: false, Warning: false, Message: "步骤 CopyTree 缺少目标目录。");
			}
			await Task.Run(delegate
			{
				CopyDirectory(sourcePath, step.TargetPath, ct);
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			return (Success: true, Warning: false, Message: "目录已恢复：" + step.TargetPath);
		}
		case "CopyFile":
		{
			string sourcePath3 = ResolveBundleStepPath(extractDirectory, step.SourceRelativePath);
			if (!File.Exists(sourcePath3))
			{
				return (Success: false, Warning: step.Optional, Message: "步骤 CopyFile 源文件不存在：" + step.SourceRelativePath);
			}
			if (string.IsNullOrWhiteSpace(step.TargetPath))
			{
				return (Success: false, Warning: false, Message: "步骤 CopyFile 缺少目标文件。");
			}
			string? targetDirectory = Path.GetDirectoryName(step.TargetPath);
			if (!string.IsNullOrWhiteSpace(targetDirectory))
			{
				Directory.CreateDirectory(targetDirectory);
			}
			File.Copy(sourcePath3, step.TargetPath, step.Overwrite);
			return (Success: true, Warning: false, Message: "文件已恢复：" + step.TargetPath);
		}
		case "ImportRegistry":
		{
			string sourcePath2 = ResolveBundleStepPath(extractDirectory, step.SourceRelativePath);
			if (!File.Exists(sourcePath2))
			{
				return (Success: false, Warning: step.Optional, Message: "步骤 ImportRegistry 源文件不存在：" + step.SourceRelativePath);
			}
			// Legacy registry imports have no typed snapshot/readback/restore contract; fail closed.
			return (Success: false, Warning: step.Optional, Message: "步骤 ImportRegistry 已阻断：旧式注册表写入没有受支持的 typed recovery 证据，请通过专用 Owner/人工流程处理。");
		}
		case "RunCommand":
		{
			if (string.IsNullOrWhiteSpace(step.Command))
			{
				return (Success: false, Warning: step.Optional, Message: "步骤 RunCommand 缺少命令。");
			}
			// Arbitrary legacy commands cannot be admitted without a typed owner and recovery evidence.
			return (Success: false, Warning: step.Optional, Message: "步骤 RunCommand 已阻断：旧式任意进程执行没有受支持的 typed recovery 证据，请通过专用 Owner/人工流程处理。");
		}
		case "ManualAction":
			return (Success: true, Warning: true, Message: string.IsNullOrWhiteSpace(step.Description) ? "存在人工处理步骤，请按提示完成。" : step.Description);
		default:
			return (Success: false, Warning: step.Optional, Message: "未知步骤类型：" + step.StepType);
		}
	}

	private static string ResolveBundleStepPath(string extractDirectory, string? sourceRelativePath)
	{
		if (string.IsNullOrWhiteSpace(sourceRelativePath))
		{
			return extractDirectory;
		}
		return Path.Combine(extractDirectory, sourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	private static string QuotePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "\"\"";
		}
		return "\"" + path.Replace("\"", "\\\"") + "\"";
	}

	private static void CopyDirectory(string sourceDirectory, string targetDirectory, CancellationToken ct)
	{
		Directory.CreateDirectory(targetDirectory);
		foreach (string item in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			ct.ThrowIfCancellationRequested();
			string relativePath = Path.GetRelativePath(sourceDirectory, item);
			Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
		}
		foreach (string item2 in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
		{
			ct.ThrowIfCancellationRequested();
			string relativePath2 = Path.GetRelativePath(sourceDirectory, item2);
			string text = Path.Combine(targetDirectory, relativePath2);
			string? directoryName = Path.GetDirectoryName(text);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.Copy(item2, text, overwrite: true);
		}
	}

	private static void TryDeleteDirectorySafe(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}
		try
		{
			if (Directory.Exists(directoryPath))
			{
				Directory.Delete(directoryPath, recursive: true);
			}
		}
		catch
		{
		}
	}

	private static List<BaselineDomainArtifact> SelectDomainsByMode(IEnumerable<BaselineDomainArtifact> domains, BaselineRestoreMode mode)
	{
		List<BaselineDomainArtifact> list = domains?.ToList() ?? new List<BaselineDomainArtifact>();
		List<BaselineDomainArtifact> result = mode switch
		{
			BaselineRestoreMode.DomainOnly => list.Where((BaselineDomainArtifact domain) => domain.Required).ToList(), 
			BaselineRestoreMode.DeepRegistryOnly => list.Where((BaselineDomainArtifact domain) => domain.DomainId.Equals("D03", StringComparison.OrdinalIgnoreCase) || domain.DomainId.Equals("D04", StringComparison.OrdinalIgnoreCase)).ToList(), 
			BaselineRestoreMode.DryRun => list, 
			_ => list, 
		};
		return result;
	}

	private static List<BaselineDomainArtifact> OrderDomainsByRestorePlan(IEnumerable<BaselineDomainArtifact> domains)
	{
		Dictionary<string, int> orderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["D04"] = 10,
			["D05"] = 11,
			["D06"] = 12,
			["D07"] = 13,
			["D01"] = 20,
			["D02"] = 21,
			["D03"] = 30,
			["D11"] = 40,
			["D12"] = 41,
			["D15"] = 42,
			["D16"] = 43,
			["D17"] = 44,
			["D18"] = 45,
			["D19"] = 46,
			["D20"] = 47,
			["D21"] = 48,
			["D22"] = 49,
			["D08"] = 50,
			["D09"] = 51,
			["D10"] = 60,
			["D13"] = 70
		};
		int value;
		return (domains ?? Enumerable.Empty<BaselineDomainArtifact>()).OrderBy((BaselineDomainArtifact domain) => orderMap.TryGetValue(domain.DomainId, out value) ? value : 999).ThenBy<BaselineDomainArtifact, string>((BaselineDomainArtifact domain) => domain.DomainId, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string ResolveModeText(BaselineRestoreMode mode)
	{
		string result = mode switch
		{
			BaselineRestoreMode.DomainOnly => "域级恢复", 
			BaselineRestoreMode.DeepRegistryOnly => "深度注册表恢复", 
			BaselineRestoreMode.DryRun => "恢复演练", 
			BaselineRestoreMode.SystemRestoreFallback => "系统还原点兜底", 
			_ => "全量恢复", 
		};
		return result;
	}

	private static BaselineRestoreExecutionReport CreateReport(string packageId)
	{
		return new BaselineRestoreExecutionReport
		{
			RestoreId = $"BR-{DateTime.Now:yyyyMMddHHmmss}",
			SourcePackageId = packageId,
			StartedUtc = DateTime.UtcNow,
			FinishedUtc = DateTime.UtcNow
		};
	}

	private static bool DomainMayRequireReboot(string domainId)
	{
		return domainId.Equals("D09", StringComparison.OrdinalIgnoreCase) || domainId.Equals("D10", StringComparison.OrdinalIgnoreCase) || domainId.Equals("D21", StringComparison.OrdinalIgnoreCase) || domainId.Equals("D22", StringComparison.OrdinalIgnoreCase);
	}

	private static string ToResultText(BaselineDomainStatus status)
	{
		string result = status switch
		{
			BaselineDomainStatus.Succeeded => "恢复成功", 
			BaselineDomainStatus.Skipped => "已跳过", 
			BaselineDomainStatus.Warning => "告警", 
			BaselineDomainStatus.ManualRequired => "需人工处理", 
			_ => "恢复失败", 
		};
		return result;
	}

	private static List<string> BuildNextActions(BaselineRestoreExecutionReport report)
	{
		List<string> list = new List<string>();
		if (report.RestoreConfidenceScore >= 95)
		{
			list.Add("恢复可信度高，建议执行一次优化项自检。");
		}
		else if (report.RestoreConfidenceScore >= 80)
		{
			list.Add("存在部分风险，建议在备份中心复核失败域后再执行优化。");
		}
		else
		{
			list.Add("恢复可信度低，建议优先使用系统还原点兜底。");
		}
		if (report.RequiresReboot)
		{
			list.Add("建议重启系统后再继续使用优化功能。");
		}
		if (report.FailedCount > 0)
		{
			list.Add("可先创建新的基线包，再次执行恢复验证。");
		}
		if (report.SkippedCount > 0)
		{
			list.Add("存在未自动完成的域，请在域报告中完成人工步骤后再确认恢复结果。");
		}
		return list;
	}

	private static string BuildPostVerifySummary(BaselineRestoreExecutionReport report)
	{
		return $"恢复结果：成功 {report.SuccessCount}，失败 {report.FailedCount}，跳过 {report.SkippedCount}，可信度 {report.RestoreConfidenceScore}。";
	}

	private static bool RequiresManualCompletion(BaselineDomainRestoreStep step)
	{
		return ((step.StepType ?? string.Empty).Trim()) switch
		{
			"CopyTree" or "CopyFile" => false,
			_ => true
		};
	}

	private static bool HasCompleteRestoreEvidence(BaselineRestoreExecutionReport report, int selectedDomainCount)
	{
		return selectedDomainCount > 0
			&& report.DomainResults.Count == selectedDomainCount
			&& report.SuccessCount == selectedDomainCount
			&& report.FailedCount == 0
			&& report.SkippedCount == 0
			&& report.DomainResults.All((BaselineRestoreDomainResult domain) => domain.Status == BaselineDomainStatus.Succeeded);
	}

	private static string BuildFinalMessage(string modeText, BaselineRestoreExecutionReport report, bool success)
	{
		if (success)
		{
			return modeText + "完成，全部选中域已执行并通过恢复结果校验。";
		}
		if (report.FailedCount > 0)
		{
			return modeText + "未完整完成：存在失败域，不能宣称恢复成功，请按建议继续处理。";
		}
		return modeText + "未完整完成：存在未自动完成或需人工处理的域，不能宣称全部域恢复成功。";
	}

	private async Task<BaselineRecoverabilityDomainCheckItem> ValidateRecoverabilityArtifactAsync(BaselineDomainArtifact domain, string packageDirectory, CancellationToken ct)
	{
		BaselineRecoverabilityDomainCheckItem checkItem = new BaselineRecoverabilityDomainCheckItem
		{
			DomainId = domain.DomainId,
			DomainName = domain.DomainName,
			Status = BaselineDomainStatus.Pending
		};
		string relativePath = (string.IsNullOrWhiteSpace(domain.RelativePath) ? (domain.DomainId + ".bundle.zip") : domain.RelativePath);
		string artifactPath = Path.Combine(packageDirectory, relativePath);
		if (!File.Exists(artifactPath))
		{
			checkItem.Status = (domain.Required ? BaselineDomainStatus.Failed : BaselineDomainStatus.Skipped);
			checkItem.Message = (domain.Required ? "缺少恢复资产文件。" : "可选资产缺失，已跳过。");
			return checkItem;
		}
		try
		{
			FileInfo fileInfo = new FileInfo(artifactPath);
			if (domain.SizeBytes > 0 && fileInfo.Length != domain.SizeBytes)
			{
				checkItem.Status = BaselineDomainStatus.Failed;
				checkItem.Message = "文件大小与清单不一致。";
				return checkItem;
			}
			if (!string.IsNullOrWhiteSpace(domain.Sha256))
			{
				string hash = BaselineHashHelper.ComputeSha256(artifactPath);
				if (!hash.Equals(domain.Sha256, StringComparison.OrdinalIgnoreCase))
				{
					checkItem.Status = BaselineDomainStatus.Failed;
					checkItem.Message = "文件校验和与清单不一致。";
					return checkItem;
				}
			}
			if (relativePath.EndsWith(".bundle.zip", StringComparison.OrdinalIgnoreCase))
			{
				using (ZipArchive archive = ZipFile.OpenRead(artifactPath))
				{
					ZipArchiveEntry? manifestEntry = archive.Entries.FirstOrDefault((ZipArchiveEntry entry) => entry.FullName.Equals("domain-manifest.json", StringComparison.OrdinalIgnoreCase));
					if (manifestEntry == null)
					{
						checkItem.Status = BaselineDomainStatus.Failed;
						checkItem.Message = "域包缺少 domain-manifest.json。";
						return checkItem;
					}
					BaselineRecoverabilityDomainCheckItem result;
					await using (Stream manifestStream = manifestEntry.Open())
					{
						BaselineDomainBundleManifest? bundleManifest = await JsonSerializer.DeserializeAsync<BaselineDomainBundleManifest>(manifestStream, new JsonSerializerOptions
						{
							PropertyNameCaseInsensitive = true
						}, ct).ConfigureAwait(continueOnCapturedContext: false);
						if (bundleManifest == null)
						{
							checkItem.Status = BaselineDomainStatus.Failed;
							checkItem.Message = "域包清单解析失败。";
							result = checkItem;
						}
						else if (!bundleManifest.DomainId.Equals(domain.DomainId, StringComparison.OrdinalIgnoreCase))
						{
							checkItem.Status = BaselineDomainStatus.Failed;
							checkItem.Message = "域包 domainId 与清单不一致。";
							result = checkItem;
						}
						else if (bundleManifest.Items.Count == 0 && bundleManifest.RestoreSteps.Count == 0)
						{
							checkItem.Status = BaselineDomainStatus.Failed;
							checkItem.Message = "域包缺少资产与恢复步骤。";
							result = checkItem;
						}
						else
						{
							bool hasOwnedFilePayload = !string.Equals(domain.DomainId, "D04", StringComparison.OrdinalIgnoreCase)
								&& !string.Equals(domain.DomainId, "D05", StringComparison.OrdinalIgnoreCase)
								&& !string.Equals(domain.DomainId, "D06", StringComparison.OrdinalIgnoreCase)
								|| new BaselineOwnedFileDomainRestoreOwner().HasUsableCapturedPayload(domain.DomainId, bundleManifest, archive.Entries.Select(static entry => entry.FullName));
							(bool typedSnapshotPayloadValid, string typedSnapshotPayloadMessage) = await ValidateTypedSnapshotPayloadAsync(
								domain,
								bundleManifest,
								archive,
								ct).ConfigureAwait(false);
							BaselineRecoveryCoverageAssessment assessment = BaselineRecoveryCoverageClassifier.Evaluate(
								domain,
								packageVerified: true,
								restoreSteps: bundleManifest.RestoreSteps,
								readFailure: null);
							if (assessment.IsAutomaticRecovery && hasOwnedFilePayload && typedSnapshotPayloadValid)
							{
								checkItem.Status = BaselineDomainStatus.Succeeded;
								checkItem.Message = "域包结构、校验和受控恢复能力均通过。";
							}
							else
							{
								checkItem.Status = BaselineDomainStatus.ManualRequired;
								checkItem.Message = assessment.IsAutomaticRecovery
									? string.IsNullOrWhiteSpace(typedSnapshotPayloadMessage)
										? "域包结构和校验通过，但专属文件恢复器未找到可用的受控数据载荷。"
										: typedSnapshotPayloadMessage
									: $"域包结构和校验通过，但未达到自动恢复准入：{assessment.Detail}";
							}
							result = checkItem;
						}
					}
					return result;
				}
			}
			BaselineRecoverabilityDomainCheckItem result2;
			await using (FileStream stream = File.OpenRead(artifactPath))
			{
				using JsonDocument document = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), ct).ConfigureAwait(continueOnCapturedContext: false);
				if (!document.RootElement.TryGetProperty("domainId", out var domainIdElement))
				{
					goto IL_0728;
				}
				string payloadDomainId = domainIdElement.GetString() ?? string.Empty;
				if (string.IsNullOrWhiteSpace(payloadDomainId) || payloadDomainId.Equals(domain.DomainId, StringComparison.OrdinalIgnoreCase))
				{
					goto IL_0728;
				}
				checkItem.Status = BaselineDomainStatus.Failed;
				checkItem.Message = "资产 domainId 与清单不一致。";
				result2 = checkItem;
				goto end_IL_0680;
				IL_0728:
				checkItem.Status = BaselineDomainStatus.Succeeded;
				checkItem.Message = "资产结构和校验均通过。";
				result2 = checkItem;
				end_IL_0680:;
			}
			return result2;
		}
		catch (JsonException)
		{
			checkItem.Status = BaselineDomainStatus.Failed;
			checkItem.Message = "资产 JSON 损坏。";
			return checkItem;
		}
		catch (Exception ex2)
		{
			checkItem.Status = BaselineDomainStatus.Failed;
			checkItem.Message = ex2.Message;
			return checkItem;
		}
	}

	private async Task<(bool IsValid, string Message)> ValidateTypedSnapshotPayloadAsync(
		BaselineDomainArtifact domain,
		BaselineDomainBundleManifest manifest,
		ZipArchive archive,
		CancellationToken ct)
	{
		if (!string.Equals(domain.DomainId, "D03", StringComparison.OrdinalIgnoreCase))
		{
			return (true, string.Empty);
		}

		if (manifest.RestoreSteps.Count != 1
			|| !string.Equals(manifest.RestoreSteps[0].StepType, "TypedSnapshot", StringComparison.OrdinalIgnoreCase))
		{
			return (false, "类型化设置域缺少唯一、受控的恢复步骤。");
		}

		string relativePath = manifest.RestoreSteps[0].SourceRelativePath?.Trim().Replace('\\', '/') ?? string.Empty;
		if (string.IsNullOrWhiteSpace(relativePath)
			|| relativePath.Contains("..", StringComparison.Ordinal)
			|| relativePath.StartsWith("/", StringComparison.Ordinal))
		{
			return (false, "类型化设置快照路径无效，已阻断恢复。 ");
		}

		ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(item => string.Equals(item.FullName, relativePath, StringComparison.OrdinalIgnoreCase));
		if (entry is null || entry.Length == 0)
		{
			return (false, "类型化设置快照文件缺失或为空。 ");
		}

		string validationRoot = Path.Combine(BaselineReportsDir, ".typed-snapshot-validation", Guid.NewGuid().ToString("N"));
		string validationPath = Path.Combine(validationRoot, "snapshot.json");
		string? importedSnapshotId = null;
		try
		{
			Directory.CreateDirectory(validationRoot);
			await using (Stream source = entry.Open())
			await using (FileStream target = File.Create(validationPath))
			{
				await source.CopyToAsync(target, ct).ConfigureAwait(false);
			}

			OptimizationSnapshot? imported = await _snapshotService.ImportSnapshotAsync(validationPath).ConfigureAwait(false);
			if (imported is null || string.IsNullOrWhiteSpace(imported.Id))
			{
				return (false, "类型化设置快照未通过导入校验。 ");
			}

			importedSnapshotId = imported.Id;
			return (true, string.Empty);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "[BaselineRestoreOrchestrator][ValidateTypedSnapshotPayloadAsync] Typed snapshot validation failed.");
			return (false, "类型化设置快照校验失败：" + ex.Message);
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(importedSnapshotId))
			{
				try
				{
					await _snapshotService.DeleteSnapshotAsync(importedSnapshotId).ConfigureAwait(false);
				}
				catch (Exception cleanupFailure)
				{
					_logger.LogWarning(cleanupFailure, "[BaselineRestoreOrchestrator][ValidateTypedSnapshotPayloadAsync] Failed to remove validation snapshot.");
				}
			}
			TryDeleteDirectorySafe(validationRoot);
		}
	}

	private static string BuildRecoverabilitySummary(BaselineRecoverabilityCheckReport report)
	{
		if (report.DomainFailedCount > 0)
		{
			return $"恢复可用性自检失败：{report.DomainFailedCount}/{report.DomainCheckedCount} 个域的资产损坏、缺失或不一致；已阻断优化入口。";
		}

		return $"恢复可用性自检未通过：{report.DomainManualRequiredCount}/{report.DomainCheckedCount} 个域尚未具备专属恢复器、环境兼容性预检和回读验证；已阻断优化入口。";
	}

	private async Task UpdateManifestRecoverabilityStateAsync(string packageId, bool success, string message, DateTime checkedUtc, CancellationToken ct)
	{
		BaselinePackageManifest? manifest = await _catalogService.GetManifestAsync(packageId, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (manifest != null)
		{
			manifest.LastRecoverabilityCheckUtc = checkedUtc;
			manifest.RecoverabilityCheckState = (success ? "Passed" : "Failed");
			manifest.RecoverabilityCheckMessage = message;
			await _catalogService.UpsertAsync(manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private static async Task<string> WriteRecoverabilityReportAsync(BaselineRecoverabilityCheckReport report, CancellationToken ct)
	{
		Directory.CreateDirectory(BaselineReportsDir);
		string path = Path.Combine(BaselineReportsDir, "dryrun_" + report.CheckId + ".json");
		string json = JsonSerializer.Serialize(report, JsonOptions);
		await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(continueOnCapturedContext: false);
		return path;
	}

	private static async Task<string> WriteReportAsync(BaselineRestoreExecutionReport report, CancellationToken ct)
	{
		Directory.CreateDirectory(BaselineReportsDir);
		string path = Path.Combine(BaselineReportsDir, report.RestoreId + ".json");
		string json = JsonSerializer.Serialize(report, JsonOptions);
		await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(continueOnCapturedContext: false);
		return path;
	}

	private static void Report(IProgress<BaselineRestoreProgressEvent>? progress, BaselineRestoreStep step, string message, string? domainId, BaselineDomainStatus? status, int completedDomains, int totalDomains)
	{
		progress?.Report(new BaselineRestoreProgressEvent
		{
			Step = step,
			Message = message,
			DomainId = domainId,
			DomainStatus = status,
			CompletedDomains = completedDomains,
			TotalDomains = totalDomains
		});
	}
}
