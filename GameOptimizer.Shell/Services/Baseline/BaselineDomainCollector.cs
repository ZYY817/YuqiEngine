using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Services;
using GameOptimizer.Core.Services.Snapshot;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineDomainCollector : IBaselineDomainCollector
{
	private sealed class DomainCollectContext
	{
		public BaselineDomainDefinition Domain { get; }

		public string WorkingDirectory { get; }

		public List<BaselineDomainBundleItem> Items { get; } = new List<BaselineDomainBundleItem>();

		public List<BaselineDomainRestoreStep> RestoreSteps { get; } = new List<BaselineDomainRestoreStep>();

		public List<string> Warnings { get; } = new List<string>();

		public List<string> BlockingErrors { get; } = new List<string>();

		public DomainCollectContext(BaselineDomainDefinition domain, string workingDirectory)
		{
			Domain = domain;
			WorkingDirectory = workingDirectory;
		}

		public void AddItem(BaselineDomainBundleItem item)
		{
			Items.Add(item);
		}

		public void AddRestoreStep(BaselineDomainRestoreStep step)
		{
			RestoreSteps.Add(step);
		}

		public void AddWarning(string warning)
		{
			if (!string.IsNullOrWhiteSpace(warning))
			{
				Warnings.Add(warning);
			}
		}

		public void AddBlockingError(string error)
		{
			if (!string.IsNullOrWhiteSpace(error))
			{
				BlockingErrors.Add(error);
			}
		}

		public void Normalize()
		{
			if (Items.Count > 0)
			{
				List<BaselineDomainBundleItem> collection = (from item in Items
					group item by (item.ItemType + "::" + item.RelativePath).ToUpperInvariant() into @group
					select @group.Last()).ToList();
				Items.Clear();
				Items.AddRange(collection);
			}
			if (RestoreSteps.Count > 0)
			{
				List<BaselineDomainRestoreStep> collection2 = (from step in RestoreSteps
					group step by $"{step.StepType}::{step.SourceRelativePath}::{step.TargetPath}::{step.Command}::{step.Arguments}".ToUpperInvariant() into @group
					select @group.Last()).ToList();
				RestoreSteps.Clear();
				RestoreSteps.AddRange(collection2);
			}
		}
	}

	private sealed class CommandExecutionResult
	{
		public bool Success { get; init; }

		public int ExitCode { get; init; }

		public string StdOut { get; init; } = string.Empty;

		public string StdErr { get; init; } = string.Empty;

		public string? ErrorMessage { get; init; }
	}

	private const string DomainManifestFileName = "domain-manifest.json";

	private const int CommandOutputMaxLength = 524288;

	private const int CleanupDomainMaxFileCount = 2000;

	private const long CleanupDomainMaxBytes = 536870912L;

	private static readonly Regex GuidPattern = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	private static readonly IReadOnlyList<BaselineDomainDefinition> DomainDefinitions = new List<BaselineDomainDefinition>
	{
		Create("D01", "回滚仓快照", "restore 目录中的历史回滚快照", required: false),
		Create("D02", "快照主数据", "Snapshots JSON 主数据", required: false),
		Create("D03", "受控设置快照", "类型化设置恢复快照"),
		Create("D04", "应用配置", "Config 配置与开关"),
		Create("D05", "业务档案", "游戏档案、优先级与亲和配置"),
		Create("D06", "核心加速配置", "CoreAccelerator 数据"),
		Create("D07", "历史清理备份", "cleanup 产生的临时备份（可选）", required: false),
		Create("D08", "网络配置", "网络与代理配置", required: false),
		Create("D09", "电源计划", "powercfg 导出结果", required: false),
		Create("D10", "启动配置", "bcdedit 导出结果", required: false),
		Create("D11", "系统服务状态", "关键系统服务状态", required: false),
		Create("D12", "计划任务状态", "关键计划任务状态", required: false),
		Create("D13", "系统还原点", "系统还原点校验（必要时需手动处理）", required: true, supportsManualGuide: true),
		Create("D14", "诊断与审计", "事务日志与备份日志", required: false),
		Create("D15", "本地策略", "本地组策略与安全策略", required: false),
		Create("D16", "安全防护", "防火墙与 Defender 关键策略", required: false),
		Create("D17", "启动项", "Run/RunOnce/启动文件夹", required: false),
		Create("D18", "高级网络", "Hosts/WinHTTP/Winsock", required: false),
		Create("D19", "系统功能", "可选功能与游戏相关开关", required: false),
		Create("D20", "渲染与显示", "渲染和显示相关配置", required: false),
		Create("D21", "内存与分页", "虚拟内存与分页配置", required: false),
		Create("D22", "更新维护", "Windows Update 策略", required: false)
	};

	private static readonly string[] ImportantServices = new string[5] { "WSearch", "wuauserv", "DiagTrack", "SysMain", "MpsSvc" };

	private readonly ILogger<BaselineDomainCollector> _logger;

	private readonly ISnapshotService _snapshotService;

	public BaselineDomainCollector(ILogger<BaselineDomainCollector> logger, ISnapshotService snapshotService)
	{
		_logger = logger;
		_snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
	}

	public IReadOnlyList<BaselineDomainDefinition> GetDefinitions()
	{
		return DomainDefinitions;
	}

	public async Task<BaselineDomainArtifact> CollectDomainAsync(BaselineDomainDefinition domain, string packageDirectory, CancellationToken ct = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(domain, "domain");
		ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory, "packageDirectory");
		if (domain.DomainId.Equals("D13", StringComparison.OrdinalIgnoreCase))
		{
			return new BaselineDomainArtifact
			{
				DomainId = domain.DomainId,
				DomainName = domain.DomainName,
				Required = domain.Required,
				Status = BaselineDomainStatus.ManualRequired,
				ErrorCode = "BASELINE.D13.MANUAL_REQUIRED",
				ErrorMessage = "System restore point must be created by manual guide or service path."
			};
		}
		Directory.CreateDirectory(packageDirectory);
		string artifactFileName = domain.DomainId + ".bundle.zip";
		string artifactPath = Path.Combine(packageDirectory, artifactFileName);
		string tempDirectory = Path.Combine(packageDirectory, $".collect_{domain.DomainId}_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDirectory);
		try
		{
			DomainCollectContext collectContext = new DomainCollectContext(domain, tempDirectory);
			await CollectDomainBundleAsync(collectContext, ct).ConfigureAwait(continueOnCapturedContext: false);
			collectContext.Normalize();
			BaselineDomainBundleManifest bundleManifest = new BaselineDomainBundleManifest
			{
				DomainId = domain.DomainId,
				DomainName = domain.DomainName,
				Required = domain.Required,
				Status = ((collectContext.BlockingErrors.Count > 0) ? BaselineDomainStatus.Failed : BaselineDomainStatus.Succeeded),
				Summary = $"采集完成：{collectContext.Items.Count} 项资产，{collectContext.RestoreSteps.Count} 个恢复步骤。",
				CollectedAtUtc = DateTime.UtcNow,
				Items = collectContext.Items,
				RestoreSteps = collectContext.RestoreSteps,
				Warnings = collectContext.Warnings.Concat(collectContext.BlockingErrors).ToList()
			};
			string manifestPath = Path.Combine(tempDirectory, "domain-manifest.json");
			string manifestJson = JsonSerializer.Serialize(bundleManifest, JsonOptions);
			await File.WriteAllTextAsync(manifestPath, manifestJson, Encoding.UTF8, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (File.Exists(artifactPath))
			{
				File.Delete(artifactPath);
			}
			await Task.Run(delegate
			{
				ZipFile.CreateFromDirectory(tempDirectory, artifactPath, CompressionLevel.Optimal, includeBaseDirectory: false);
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			FileInfo fileInfo = new FileInfo(artifactPath);
			BaselineDomainStatus status = ((collectContext.BlockingErrors.Count > 0) ? BaselineDomainStatus.Failed : BaselineDomainStatus.Succeeded);
			return new BaselineDomainArtifact
			{
				DomainId = domain.DomainId,
				DomainName = domain.DomainName,
				Required = domain.Required,
				Status = status,
				RelativePath = artifactFileName,
				SizeBytes = (fileInfo.Exists ? fileInfo.Length : 0),
				Sha256 = (fileInfo.Exists ? BaselineHashHelper.ComputeSha256(artifactPath) : string.Empty),
				ErrorCode = ((collectContext.BlockingErrors.Count > 0) ? "BASELINE.DOMAIN.COLLECT_FAILED" : null),
				ErrorMessage = ((collectContext.BlockingErrors.Count > 0) ? string.Join(" | ", collectContext.BlockingErrors) : null)
			};
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.LogWarning(ex2, "[BaselineDomainCollector][CollectDomainAsync] Domain collect failed. domain={DomainId}", domain.DomainId);
			return new BaselineDomainArtifact
			{
				DomainId = domain.DomainId,
				DomainName = domain.DomainName,
				Required = domain.Required,
				Status = BaselineDomainStatus.Failed,
				RelativePath = artifactFileName,
				ErrorCode = "BASELINE.DOMAIN.COLLECT_FAILED",
				ErrorMessage = ex2.Message
			};
		}
		finally
		{
			TryDeleteDirectorySafe(tempDirectory);
		}
	}

	private async Task CollectDomainBundleAsync(DomainCollectContext context, CancellationToken ct)
	{
		switch (context.Domain.DomainId.ToUpperInvariant())
		{
		case "D01":
			await CapturePathAsync(context, Path.Combine(AppPaths.BackupsDir, "restore"), "payload/restore", "历史回滚快照目录", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D02":
			await CapturePathAsync(context, AppPaths.SnapshotsDir, "payload/snapshots", "快照主数据目录", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D03":
			await CollectRegistryDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D04":
			await CapturePathAsync(context, AppPaths.ConfigDir, "payload/config", "应用配置目录", addRestoreStep: false, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
			context.AddRestoreStep(OwnedFileDomainStep("恢复应用配置"));
			break;
		case "D05":
			await CollectBusinessProfileDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D06":
			await CapturePathAsync(context, AppPaths.CoreAcceleratorDir, "payload/core-accelerator", "核心加速配置目录", addRestoreStep: false, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
			context.AddRestoreStep(OwnedFileDomainStep("恢复核心加速配置"));
			break;
		case "D07":
			await CollectCleanupDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D08":
			await CollectNetworkDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D09":
			await CollectPowerDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D10":
			await CollectBcdDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D11":
			await CollectServiceDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D12":
			await CollectScheduledTaskDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D14":
			await CollectAuditDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D15":
			await CollectPolicyDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D16":
			await CollectSecurityDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D17":
			await CollectStartupDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D18":
			await CollectAdvancedNetworkDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D19":
			await CollectFeaturesDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D20":
			await CollectRenderingDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D21":
			await CollectMemoryDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		case "D22":
			await CollectUpdateDomainAsync(context, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		default:
			await CapturePathAsync(context, AppPaths.BackupsDir, "payload/fallback", "默认备份目录", addRestoreStep: false, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
			break;
		}
	}

	private async Task CollectRegistryDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		const string snapshotRelativePath = "payload/typed-settings-snapshot.json";
		string snapshotPath = ResolveBundlePath(context.WorkingDirectory, snapshotRelativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath) ?? context.WorkingDirectory);

		try
		{
			string packageLabel = Path.GetFileName(context.WorkingDirectory).Replace(".collect_D03_", string.Empty, StringComparison.OrdinalIgnoreCase);
			OptimizationSnapshot snapshot = await _snapshotService
				.CreateSnapshotAsync("FirstBaselineTypedSettings_" + packageLabel, "首次安全基线的类型化设置恢复快照。", ct)
				.ConfigureAwait(continueOnCapturedContext: false);
			await _snapshotService.ExportSnapshotAsync(snapshot.Id, snapshotPath).ConfigureAwait(continueOnCapturedContext: false);
			if (!File.Exists(snapshotPath) || new FileInfo(snapshotPath).Length == 0)
			{
				throw new InvalidDataException("类型化设置快照导出后不存在或为空。");
			}

			context.AddItem(new BaselineDomainBundleItem
			{
				ItemType = "typed-settings-snapshot",
				RelativePath = snapshotRelativePath,
				Notes = "仅恢复快照内明确支持的类型化资源；恢复前仍需生成、确认并执行受控恢复计划。"
			});
			context.AddRestoreStep(new BaselineDomainRestoreStep
			{
				StepType = "TypedSnapshot",
				SourceRelativePath = snapshotRelativePath,
				Optional = false,
				Description = "导入类型化设置快照并通过现有确认、执行和回读链恢复。"
			});
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "[BaselineDomainCollector][CollectRegistryDomainAsync] Typed snapshot capture failed.");
			context.AddBlockingError("类型化设置快照创建失败，已阻断将该域标记为可恢复：" + ex.Message);
		}
	}

	private async Task CollectBusinessProfileDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CapturePathAsync(context, AppPaths.AffinityProfilesDir, "payload/affinity-profiles", "CPU 亲和配置档案", addRestoreStep: false, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CapturePathAsync(context, AppPaths.OptimizationProfilesDir, "payload/optimization-profiles", "优化配置档案", addRestoreStep: false, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CapturePathAsync(context, AppPaths.GameProfilesFilePath, "payload/config/game-profiles.json", "游戏档案配置", addRestoreStep: false, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CapturePathAsync(context, AppPaths.GamePriorityProfilesFilePath, "payload/config/game-priority-profiles.json", "游戏优先级配置", addRestoreStep: false, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		context.AddRestoreStep(OwnedFileDomainStep("恢复业务档案"));
	}

	private static BaselineDomainRestoreStep OwnedFileDomainStep(string description) => new()
	{
		StepType = "OwnedFileDomain",
		Optional = false,
		Overwrite = true,
		Description = description
	};

	private async Task CollectCleanupDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		string cleanupDirectory = AppPaths.CleanupBackupsDir;
		if (!Directory.Exists(cleanupDirectory))
		{
			context.AddWarning("历史清理备份目录不存在，已跳过：" + cleanupDirectory);
			return;
		}
		int fileCount = 0;
		long totalBytes = 0L;
		bool downgradedToSummary = false;
		try
		{
			foreach (string filePath in Directory.EnumerateFiles(cleanupDirectory, "*", SearchOption.AllDirectories))
			{
				ct.ThrowIfCancellationRequested();
				fileCount++;
				try
				{
					totalBytes += new FileInfo(filePath).Length;
				}
				catch
				{
				}
				if (fileCount > 2000 || totalBytes > 536870912)
				{
					downgradedToSummary = true;
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			context.AddWarning("扫描历史清理备份目录失败，将仅记录摘要：" + ex2.Message);
			downgradedToSummary = true;
		}
		if (!downgradedToSummary)
		{
			await CapturePathAsync(context, cleanupDirectory, "payload/cleanup", "历史清理备份目录（可选）", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		string summaryRelativePath = "captures/cleanup-domain-summary.txt";
		string summaryAbsolutePath = ResolveBundlePath(context.WorkingDirectory, summaryRelativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(summaryAbsolutePath) ?? context.WorkingDirectory);
		StringBuilder summary = new StringBuilder();
		summary.AppendLine("Cleanup domain fallback summary");
		StringBuilder stringBuilder = summary;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 1, stringBuilder);
		handler.AppendLiteral("source: ");
		handler.AppendFormatted(cleanupDirectory);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = summary;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder);
		handler.AppendLiteral("estimated_file_count: ");
		handler.AppendFormatted(fileCount);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = summary;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(23, 1, stringBuilder);
		handler.AppendLiteral("estimated_total_bytes: ");
		handler.AppendFormatted(totalBytes);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = summary;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder);
		handler.AppendLiteral("threshold_file_count: ");
		handler.AppendFormatted(2000);
		stringBuilder5.AppendLine(ref handler);
		stringBuilder = summary;
		StringBuilder stringBuilder6 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(23, 1, stringBuilder);
		handler.AppendLiteral("threshold_total_bytes: ");
		handler.AppendFormatted(536870912L);
		stringBuilder6.AppendLine(ref handler);
		summary.AppendLine("mode: summary_only");
		await File.WriteAllTextAsync(summaryAbsolutePath, summary.ToString(), Encoding.UTF8, ct).ConfigureAwait(continueOnCapturedContext: false);
		context.AddItem(new BaselineDomainBundleItem
		{
			ItemType = "CleanupSummary",
			SourcePath = cleanupDirectory,
			RelativePath = NormalizeBundleRelativePath(summaryRelativePath),
			SizeBytes = new FileInfo(summaryAbsolutePath).Length,
			Notes = "历史清理备份目录体积较大，已降级为摘要采集"
		});
		context.AddWarning($"历史清理备份目录较大（估算 {fileCount} 文件 / {totalBytes} 字节），已降级为摘要采集以避免流程卡住。");
	}

	private async Task CollectNetworkDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CapturePathAsync(context, AppPaths.NetworkBackupsDir, "payload/network-backups", "网络配置备份目录", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureHostsFileAsync(context, "payload/network/hosts", ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureCommandOutputAsync(context, "ipconfig /all", "captures/ipconfig-all.txt", "ipconfig", "/all", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureCommandOutputAsync(context, "route print", "captures/route-print.txt", "route", "print", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureCommandOutputAsync(context, "netsh interface ipv4 show config", "captures/netsh-ipv4-config.txt", "netsh", "interface ipv4 show config", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureCommandOutputAsync(context, "netsh winhttp show proxy", "captures/netsh-winhttp-proxy.txt", "netsh", "winhttp show proxy", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task CollectPowerDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		CommandExecutionResult listResult = await CaptureCommandOutputAsync(context, "powercfg /list", "captures/powercfg-list.txt", "powercfg", "/list", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		CommandExecutionResult activeResult = await CaptureCommandOutputAsync(context, "powercfg /getactivescheme", "captures/powercfg-active.txt", "powercfg", "/getactivescheme", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!listResult.Success)
		{
			return;
		}
		int exportedCount = 0;
		List<string> guids = ExtractGuids(listResult.StdOut).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string guid in guids)
		{
			ct.ThrowIfCancellationRequested();
			string relative = $"payload/power/scheme-{exportedCount + 1}.pow";
			string absolute = ResolveBundlePath(context.WorkingDirectory, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(absolute) ?? context.WorkingDirectory);
			CommandExecutionResult exportResult = await ExecuteCommandAsync("powercfg", "-export \"" + absolute + "\" " + guid, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!exportResult.Success || !File.Exists(absolute))
			{
				context.AddWarning($"电源计划导出失败: {guid} ({BuildCommandFailure(exportResult)})");
				continue;
			}
			exportedCount++;
			context.AddItem(new BaselineDomainBundleItem
			{
				ItemType = "PowerScheme",
				SourcePath = "powercfg -export " + guid,
				RelativePath = NormalizeBundleRelativePath(relative),
				SizeBytes = new FileInfo(absolute).Length,
				Notes = "电源计划导出文件"
			});
		}
		string? activeGuid = ExtractGuids(activeResult.StdOut).FirstOrDefault();
		context.AddRestoreStep(new BaselineDomainRestoreStep
		{
			StepType = "ManualAction",
			Optional = true,
			Description = string.IsNullOrWhiteSpace(activeGuid)
				? "已保存可用电源计划证据。若系统电源计划异常，请在 Windows“设置 > 系统 > 电源和电池”选择计划；需要恢复 Windows 默认计划时，请先阅读备份中心的风险说明后使用系统电源恢复入口。"
				: "已保存电源计划与原活动计划 " + activeGuid + "。如需恢复默认，请先在 Windows“设置 > 系统 > 电源和电池”选择计划；恢复默认计划会移除自定义计划，软件不会自动执行该操作。"
		});
	}

	private async Task CollectBcdDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CaptureCommandOutputAsync(context, "bcdedit /enum all", "captures/bcdedit-enum-all.txt", "bcdedit", "/enum all", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string exportRelative = "payload/boot/current-bcd-export.bcd";
		string exportAbsolute = ResolveBundlePath(context.WorkingDirectory, exportRelative);
		Directory.CreateDirectory(Path.GetDirectoryName(exportAbsolute) ?? context.WorkingDirectory);
		CommandExecutionResult exportResult = await ExecuteCommandAsync("bcdedit", "/export \"" + exportAbsolute + "\"", ct).ConfigureAwait(continueOnCapturedContext: false);
		if (exportResult.Success && File.Exists(exportAbsolute))
		{
			context.AddItem(new BaselineDomainBundleItem
			{
				ItemType = "BootConfigExport",
				SourcePath = "bcdedit /export",
				RelativePath = NormalizeBundleRelativePath(exportRelative),
				SizeBytes = new FileInfo(exportAbsolute).Length,
				Notes = "BCD 导出文件"
			});
			context.AddRestoreStep(new BaselineDomainRestoreStep
			{
				StepType = "RunCommand",
				SourceRelativePath = NormalizeBundleRelativePath(exportRelative),
				Command = "bcdedit",
				Arguments = "/import {{source}}",
				Optional = true,
				Description = "回写 BCD 配置（高风险，仅在明确故障时执行）"
			});
		}
		else
		{
			context.AddWarning("BCD 导出失败: " + BuildCommandFailure(exportResult));
		}
		context.AddRestoreStep(new BaselineDomainRestoreStep
		{
			StepType = "ManualAction",
			Optional = true,
			Description = "BCD 变更建议在恢复后重启系统并人工复核。"
		});
	}

	private async Task CollectServiceDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CaptureCommandOutputAsync(context, "sc query type= service state= all", "captures/services-query-all.txt", "sc", "query type= service state= all", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string[] importantServices = ImportantServices;
		foreach (string service in importantServices)
		{
			ct.ThrowIfCancellationRequested();
			await CaptureCommandOutputAsync(context, "sc qc " + service, "captures/service-" + service + "-qc.txt", "sc", "qc \"" + service + "\"", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
			await CaptureCommandOutputAsync(context, "sc query " + service, "captures/service-" + service + "-query.txt", "sc", "query \"" + service + "\"", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		context.AddRestoreStep(new BaselineDomainRestoreStep
		{
			StepType = "ManualAction",
			Optional = true,
			Description = "服务启动类型和运行状态请结合域报告定向恢复。"
		});
	}

	private async Task CollectScheduledTaskDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CaptureCommandOutputAsync(context, "schtasks /query /fo LIST /v", "captures/schtasks-query-list.txt", "schtasks", "/query /fo LIST /v", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		context.AddRestoreStep(new BaselineDomainRestoreStep
		{
			StepType = "ManualAction",
			Optional = true,
			Description = "计划任务建议在恢复中心按域报告逐项启停。"
		});
	}

	private async Task CollectAuditDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CapturePathAsync(context, AppPaths.LogsDir, "payload/logs", "诊断日志目录", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CapturePathAsync(context, Path.Combine(BaselineMirrorDirectoryPolicy.PrimaryBaselineRoot, "baseline_state.json"), "payload/baseline-state.json", "基线状态快照", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task CollectPolicyDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		string securityPolicyRelative = "payload/policy/security-policy.inf";
		string securityPolicyAbsolute = ResolveBundlePath(context.WorkingDirectory, securityPolicyRelative);
		Directory.CreateDirectory(Path.GetDirectoryName(securityPolicyAbsolute) ?? context.WorkingDirectory);
		CommandExecutionResult exportResult = await ExecuteCommandAsync("secedit", "/export /cfg \"" + securityPolicyAbsolute + "\"", ct).ConfigureAwait(continueOnCapturedContext: false);
		if (exportResult.Success && File.Exists(securityPolicyAbsolute))
		{
			context.AddItem(new BaselineDomainBundleItem
			{
				ItemType = "LocalPolicy",
				SourcePath = "secedit /export",
				RelativePath = NormalizeBundleRelativePath(securityPolicyRelative),
				SizeBytes = new FileInfo(securityPolicyAbsolute).Length,
				Notes = "本地安全策略导出"
			});
			context.AddRestoreStep(new BaselineDomainRestoreStep
			{
				StepType = "ManualAction",
				Optional = true,
				Description = "已保存本地安全策略导出。策略异常时请优先使用 Windows 系统还原点；若只需恢复默认策略，应由管理员在隔离窗口确认影响后使用 Windows 官方安全策略恢复流程，软件不会回放整份策略。"
			});
		}
		else
		{
			context.AddWarning("本地策略导出失败: " + BuildCommandFailure(exportResult));
		}
		await CaptureCommandOutputAsync(context, "gpresult /scope computer /r", "captures/gpresult-computer.txt", "gpresult", "/scope computer /r", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task CollectSecurityDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CaptureCommandOutputAsync(context, "netsh advfirewall show allprofiles", "captures/firewall-profiles.txt", "netsh", "advfirewall show allprofiles", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string firewallExportRelative = "payload/security/firewall-policy.wfw";
		string firewallExportAbsolute = ResolveBundlePath(context.WorkingDirectory, firewallExportRelative);
		Directory.CreateDirectory(Path.GetDirectoryName(firewallExportAbsolute) ?? context.WorkingDirectory);
		CommandExecutionResult firewallExportResult = await ExecuteCommandAsync("netsh", "advfirewall export \"" + firewallExportAbsolute + "\"", ct).ConfigureAwait(continueOnCapturedContext: false);
		if (firewallExportResult.Success && File.Exists(firewallExportAbsolute))
		{
			context.AddItem(new BaselineDomainBundleItem
			{
				ItemType = "FirewallPolicy",
				SourcePath = "netsh advfirewall export",
				RelativePath = NormalizeBundleRelativePath(firewallExportRelative),
				SizeBytes = new FileInfo(firewallExportAbsolute).Length,
				Notes = "Windows 防火墙策略导出"
			});
			context.AddRestoreStep(new BaselineDomainRestoreStep
			{
				StepType = "ManualAction",
				Optional = true,
				Description = "已保存防火墙策略导出。网络访问异常时请优先在 Windows 安全中心查看防火墙设置；恢复防火墙默认值会移除自定义规则，软件不会自动导入或重置。"
			});
		}
		else
		{
			context.AddWarning("防火墙策略导出失败: " + BuildCommandFailure(firewallExportResult));
		}
		await CaptureCommandOutputAsync(context, "Get-MpPreference", "captures/defender-preference.json", "powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"Get-MpPreference | ConvertTo-Json -Depth 4\"", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task CollectStartupDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		string? hkcuRun = await ExportRegistryKeyAsync(context, "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", "payload/startup/hkcu-run.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string? hkcuRunOnce = await ExportRegistryKeyAsync(context, "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", "payload/startup/hkcu-runonce.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string? hklmRun = await ExportRegistryKeyAsync(context, "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", "payload/startup/hklm-run.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string? hklmRunOnce = await ExportRegistryKeyAsync(context, "HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", "payload/startup/hklm-runonce.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string?[] array = new string?[4] { hkcuRun, hkcuRunOnce, hklmRun, hklmRunOnce };
		foreach (string? regRelative in array)
		{
			if (!string.IsNullOrWhiteSpace(regRelative))
			{
				context.AddRestoreStep(new BaselineDomainRestoreStep
				{
					StepType = "ImportRegistry",
					SourceRelativePath = regRelative,
					Optional = true,
					Description = "恢复启动项注册表"
				});
			}
		}
		await CapturePathAsync(context, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "payload/startup/user-startup-folder", "当前用户启动文件夹", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CapturePathAsync(context, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "payload/startup/common-startup-folder", "公共启动文件夹", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task CollectAdvancedNetworkDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CaptureHostsFileAsync(context, "payload/network-advanced/hosts", ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureCommandOutputAsync(context, "netsh winhttp show proxy", "captures/winhttp-proxy.txt", "netsh", "winhttp show proxy", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureCommandOutputAsync(context, "netsh winsock show catalog", "captures/winsock-catalog.txt", "netsh", "winsock show catalog", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureCommandOutputAsync(context, "netsh interface ip show config", "captures/netsh-ip-config.txt", "netsh", "interface ip show config", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		// Baseline capture and restore metadata must remain read-only. Mutating the
		// socket catalog belongs exclusively to the confirmed Network Reset Owner.
	}

	private async Task CollectFeaturesDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CaptureCommandOutputAsync(context, "dism /online /Get-Features /Format:Table", "captures/dism-features.txt", "dism", "/online /Get-Features /Format:Table", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string? gameModeExport = await ExportRegistryKeyAsync(context, "HKCU\\Software\\Microsoft\\GameBar", "payload/features/gamebar.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!string.IsNullOrWhiteSpace(gameModeExport))
		{
			context.AddRestoreStep(new BaselineDomainRestoreStep
			{
				StepType = "ImportRegistry",
				SourceRelativePath = gameModeExport,
				Optional = true,
				Description = "恢复 Game Bar / 游戏模式配置"
			});
		}
	}

	private async Task CollectRenderingDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		string? desktopExport = await ExportRegistryKeyAsync(context, "HKCU\\Control Panel\\Desktop", "payload/rendering/desktop.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string? dwmExport = await ExportRegistryKeyAsync(context, "HKCU\\Software\\Microsoft\\Windows\\DWM", "payload/rendering/dwm.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string?[] array = new string?[2] { desktopExport, dwmExport };
		foreach (string? regRelative in array)
		{
			if (!string.IsNullOrWhiteSpace(regRelative))
			{
				context.AddRestoreStep(new BaselineDomainRestoreStep
				{
					StepType = "ImportRegistry",
					SourceRelativePath = regRelative,
					Optional = true,
					Description = "恢复渲染与显示相关注册表配置"
				});
			}
		}
	}

	private async Task CollectMemoryDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		await CaptureCommandOutputAsync(context, "wmic pagefileset list /format:list", "captures/pagefile-config.txt", "wmic", "pagefileset list /format:list", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string? memoryExport = await ExportRegistryKeyAsync(context, "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management", "payload/memory/memory-management.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!string.IsNullOrWhiteSpace(memoryExport))
		{
			context.AddRestoreStep(new BaselineDomainRestoreStep
			{
				StepType = "ImportRegistry",
				SourceRelativePath = memoryExport,
				Optional = true,
				Description = "恢复虚拟内存相关注册表配置"
			});
		}
	}

	private async Task CollectUpdateDomainAsync(DomainCollectContext context, CancellationToken ct)
	{
		string? wuExport = await ExportRegistryKeyAsync(context, "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate", "payload/update/windows-update.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string? wuAuExport = await ExportRegistryKeyAsync(context, "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU", "payload/update/windows-update-au.reg", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		string?[] array = new string?[2] { wuExport, wuAuExport };
		foreach (string? regRelative in array)
		{
			if (!string.IsNullOrWhiteSpace(regRelative))
			{
				context.AddRestoreStep(new BaselineDomainRestoreStep
				{
					StepType = "ImportRegistry",
					SourceRelativePath = regRelative,
					Optional = true,
					Description = "恢复更新策略注册表"
				});
			}
		}
		await CaptureCommandOutputAsync(context, "sc qc wuauserv", "captures/wuauserv-qc.txt", "sc", "qc wuauserv", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
		await CaptureCommandOutputAsync(context, "sc query wuauserv", "captures/wuauserv-query.txt", "sc", "query wuauserv", optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<string?> ExportRegistryKeyAsync(DomainCollectContext context, string registryPath, string outputRelativePath, bool optional, CancellationToken ct)
	{
		string outputPath = ResolveBundlePath(context.WorkingDirectory, outputRelativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? context.WorkingDirectory);
		CommandExecutionResult result = await ExecuteCommandAsync("reg", $"export \"{registryPath}\" \"{outputPath}\" /y", ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success || !File.Exists(outputPath))
		{
			string message = $"注册表导出失败: {registryPath} ({BuildCommandFailure(result)})";
			if (optional)
			{
				context.AddWarning(message);
				return null;
			}
			context.AddBlockingError(message);
			return null;
		}
		context.AddItem(new BaselineDomainBundleItem
		{
			ItemType = "RegistryExport",
			SourcePath = registryPath,
			RelativePath = NormalizeBundleRelativePath(outputRelativePath),
			SizeBytes = new FileInfo(outputPath).Length,
			Notes = "reg export 导出文件"
		});
		return NormalizeBundleRelativePath(outputRelativePath);
	}

	private async Task CapturePathAsync(DomainCollectContext context, string sourcePath, string outputRelativePath, string notes, bool addRestoreStep, bool optional, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			if (optional)
			{
				context.AddWarning(notes + " 路径为空，已跳过。");
			}
			else
			{
				context.AddBlockingError(notes + " 路径为空。");
			}
			return;
		}
		bool hasDirectory = Directory.Exists(sourcePath);
		bool hasFile = File.Exists(sourcePath);
		if (!hasDirectory && !hasFile)
		{
			string missingMessage = notes + " 源路径不存在：" + sourcePath;
			if (optional)
			{
				context.AddWarning(missingMessage);
			}
			else
			{
				context.AddBlockingError(missingMessage);
			}
			return;
		}
		string normalizedRelative = NormalizeBundleRelativePath(outputRelativePath);
		string destinationPath = ResolveBundlePath(context.WorkingDirectory, normalizedRelative);
		if (hasDirectory)
		{
			await Task.Run(delegate
			{
				CopyDirectory(sourcePath, destinationPath, ct);
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			context.AddItem(new BaselineDomainBundleItem
			{
				ItemType = "Directory",
				SourcePath = sourcePath,
				RelativePath = normalizedRelative,
				SizeBytes = ComputeDirectorySize(destinationPath),
				Notes = notes
			});
			if (addRestoreStep)
			{
				context.AddRestoreStep(new BaselineDomainRestoreStep
				{
					StepType = "CopyTree",
					SourceRelativePath = normalizedRelative,
					TargetPath = sourcePath,
					Optional = optional,
					Overwrite = true,
					Description = "恢复目录：" + notes
				});
			}
		}
		else
		{
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? context.WorkingDirectory);
			File.Copy(sourcePath, destinationPath, overwrite: true);
			context.AddItem(new BaselineDomainBundleItem
			{
				ItemType = "File",
				SourcePath = sourcePath,
				RelativePath = normalizedRelative,
				SizeBytes = new FileInfo(destinationPath).Length,
				Notes = notes
			});
			if (addRestoreStep)
			{
				context.AddRestoreStep(new BaselineDomainRestoreStep
				{
					StepType = "CopyFile",
					SourceRelativePath = normalizedRelative,
					TargetPath = sourcePath,
					Optional = optional,
					Overwrite = true,
					Description = "恢复文件：" + notes
				});
			}
		}
	}

	private async Task<CommandExecutionResult> CaptureCommandOutputAsync(DomainCollectContext context, string commandDisplayName, string outputRelativePath, string command, string arguments, bool optional, CancellationToken ct)
	{
		CommandExecutionResult result = await ExecuteCommandAsync(command, arguments, ct).ConfigureAwait(continueOnCapturedContext: false);
		string outputPath = ResolveBundlePath(context.WorkingDirectory, outputRelativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? context.WorkingDirectory);
		StringBuilder outputBuilder = new StringBuilder();
		StringBuilder stringBuilder = outputBuilder;
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(12, 2, stringBuilder);
		handler.AppendLiteral("# command: ");
		handler.AppendFormatted(command);
		handler.AppendLiteral(" ");
		handler.AppendFormatted(arguments);
		stringBuilder2.AppendLine(ref handler);
		stringBuilder = outputBuilder;
		StringBuilder stringBuilder3 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder);
		handler.AppendLiteral("# timestamp_utc: ");
		handler.AppendFormatted(DateTime.UtcNow, "O");
		stringBuilder3.AppendLine(ref handler);
		stringBuilder = outputBuilder;
		StringBuilder stringBuilder4 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder);
		handler.AppendLiteral("# success: ");
		handler.AppendFormatted(result.Success);
		stringBuilder4.AppendLine(ref handler);
		stringBuilder = outputBuilder;
		StringBuilder stringBuilder5 = stringBuilder;
		handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder);
		handler.AppendLiteral("# exit_code: ");
		handler.AppendFormatted(result.ExitCode);
		stringBuilder5.AppendLine(ref handler);
		if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
		{
			stringBuilder = outputBuilder;
			StringBuilder stringBuilder6 = stringBuilder;
			handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder);
			handler.AppendLiteral("# error: ");
			handler.AppendFormatted(result.ErrorMessage);
			stringBuilder6.AppendLine(ref handler);
		}
		outputBuilder.AppendLine();
		outputBuilder.AppendLine("## stdout");
		outputBuilder.AppendLine(TrimCommandOutput(result.StdOut));
		outputBuilder.AppendLine();
		outputBuilder.AppendLine("## stderr");
		outputBuilder.AppendLine(TrimCommandOutput(result.StdErr));
		await File.WriteAllTextAsync(outputPath, outputBuilder.ToString(), Encoding.UTF8, ct).ConfigureAwait(continueOnCapturedContext: false);
		context.AddItem(new BaselineDomainBundleItem
		{
			ItemType = "CommandOutput",
			SourcePath = (command + " " + arguments).Trim(),
			RelativePath = NormalizeBundleRelativePath(outputRelativePath),
			SizeBytes = new FileInfo(outputPath).Length,
			Notes = commandDisplayName
		});
		if (!result.Success)
		{
			string message = $"命令执行失败: {commandDisplayName} ({BuildCommandFailure(result)})";
			if (optional)
			{
				context.AddWarning(message);
			}
			else
			{
				context.AddBlockingError(message);
			}
		}
		return result;
	}

	private async Task CaptureHostsFileAsync(DomainCollectContext context, string outputRelativePath, CancellationToken ct)
	{
		string hostsPath = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
		await CapturePathAsync(context, hostsPath, outputRelativePath, "系统 Hosts 文件", addRestoreStep: true, optional: true, ct).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async Task<CommandExecutionResult> ExecuteCommandAsync(string command, string arguments, CancellationToken ct)
	{
		var (exitCode, stdOut, stdErr, errorMessage) = await SafeProcessHelper.RunProcessAsync(command, arguments, ct, 60000).ConfigureAwait(continueOnCapturedContext: false);
		return new CommandExecutionResult
		{
			Success = (exitCode == 0 && string.IsNullOrWhiteSpace(errorMessage)),
			ExitCode = exitCode,
			StdOut = (stdOut ?? string.Empty),
			StdErr = (stdErr ?? string.Empty),
			ErrorMessage = errorMessage
		};
	}

	private static IEnumerable<string> ExtractGuids(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return Array.Empty<string>();
		}
		return (from match in GuidPattern.Matches(text)
			select match.Value into value
			where !string.IsNullOrWhiteSpace(value)
			select value).ToList();
	}

	private static string TrimCommandOutput(string? output)
	{
		if (string.IsNullOrWhiteSpace(output))
		{
			return string.Empty;
		}
		if (output.Length <= 524288)
		{
			return output;
		}
		return output.Substring(0, 524288) + "\n\n...[truncated]";
	}

	private static string BuildCommandFailure(CommandExecutionResult result)
	{
		if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
		{
			return result.ErrorMessage;
		}
		if (result.ExitCode != 0)
		{
			string text = (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr);
			return string.IsNullOrWhiteSpace(text) ? $"exit={result.ExitCode}" : $"{text.Trim()} (exit={result.ExitCode})";
		}
		return "unknown error";
	}

	private static void CopyDirectory(string sourceDirectory, string targetDirectory, CancellationToken ct)
	{
		if (!Directory.Exists(sourceDirectory))
		{
			return;
		}
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

	private static long ComputeDirectorySize(string directoryPath)
	{
		if (!Directory.Exists(directoryPath))
		{
			return 0L;
		}
		long num = 0L;
		foreach (string item in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
		{
			try
			{
				num += new FileInfo(item).Length;
			}
			catch
			{
			}
		}
		return num;
	}

	private static string NormalizeBundleRelativePath(string relativePath)
	{
		return relativePath.Replace('\\', '/').TrimStart('/');
	}

	private static string ResolveBundlePath(string root, string relativePath)
	{
		return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
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

	private static BaselineDomainDefinition Create(string id, string name, string description, bool required = true, bool supportsManualGuide = false)
	{
		return new BaselineDomainDefinition
		{
			DomainId = id,
			DomainName = name,
			Description = description,
			Required = required,
			SupportsManualGuide = supportsManualGuide
		};
	}
}
