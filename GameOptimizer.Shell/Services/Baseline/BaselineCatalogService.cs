using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Constants;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineCatalogService : IBaselineCatalogService
{
	private static readonly string BaselineDir = Path.Combine(AppPaths.BackupsDir, "Baseline");

	private static readonly string PackagesDir = Path.Combine(BaselineDir, "packages");

	private static readonly string CatalogPath = Path.Combine(BaselineDir, "catalog.json");

	private const int MinVerifiedRetained = 2;

	private const int MaxDeprecatedRetained = 6;

	private static readonly TimeSpan DeprecatedRetention = TimeSpan.FromDays(30.0);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

	private readonly ILogger<BaselineCatalogService> _logger;

	private readonly IBaselineIntegrityService _integrityService;

	public BaselineCatalogService(ILogger<BaselineCatalogService> logger, IBaselineIntegrityService integrityService)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
		_integrityService = integrityService ?? throw new ArgumentNullException("integrityService");
	}

	public async Task<IReadOnlyList<BaselineCatalogEntry>> GetEntriesAsync(CancellationToken ct = default(CancellationToken))
	{
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			BaselineCatalog catalog = await LoadCatalogCoreAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			bool changed = await ReconcileCatalogEntriesAsync(catalog, ct).ConfigureAwait(continueOnCapturedContext: false);
			foreach (BaselineCatalogEntry entry in catalog.Entries)
			{
				changed |= NormalizeEntryInPlace(entry);
			}
			if (changed | CleanupDeprecatedEntries(catalog, "GetEntries"))
			{
				await SaveCatalogCoreAsync(catalog, ct).ConfigureAwait(continueOnCapturedContext: false);
			}
			return catalog.Entries.OrderByDescending((BaselineCatalogEntry baselineCatalogEntry) => baselineCatalogEntry.CreatedUtc).ToList();
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task UpsertAsync(BaselinePackageManifest manifest, CancellationToken ct = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(manifest, "manifest");
		if (string.IsNullOrWhiteSpace(manifest.PackageId))
		{
			throw new ArgumentException("PackageId cannot be empty.", "manifest");
		}
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			manifest.PackageId = manifest.PackageId.Trim();
			manifest.CreationSource = NormalizeCreationSource(manifest.CreationSource);
			manifest.LifecycleState = NormalizeLifecycleState(manifest.LifecycleState);
			manifest.MirrorSyncState = NormalizeMirrorSyncState(manifest.MirrorSyncState);
			manifest.RecoverabilityCheckState = NormalizeRecoverabilityCheckState(manifest.RecoverabilityCheckState);
			Directory.CreateDirectory(PackagesDir);
			string packageDirectory = Path.Combine(PackagesDir, manifest.PackageId);
			Directory.CreateDirectory(packageDirectory);
			await _integrityService.WriteManifestAsync(packageDirectory, manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
			BaselineCatalog catalog = await LoadCatalogCoreAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			BaselineCatalogEntry? existing = catalog.Entries.FirstOrDefault((BaselineCatalogEntry baselineCatalogEntry) => string.Equals(baselineCatalogEntry.PackageId, manifest.PackageId, StringComparison.OrdinalIgnoreCase));
			if (existing == null)
			{
				existing = new BaselineCatalogEntry();
				catalog.Entries.Add(existing);
			}
			string normalizedLifecycle = manifest.LifecycleState;
			bool incomingIsVerified = string.Equals(manifest.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase) && manifest.BaselineState == FirstRunBaselineState.CompletedVerified;
			bool incomingIsActive = string.Equals(normalizedLifecycle, "Active", StringComparison.OrdinalIgnoreCase);
			if (incomingIsVerified && incomingIsActive)
			{
				foreach (BaselineCatalogEntry entry in catalog.Entries)
				{
					if (!entry.PackageId.Equals(manifest.PackageId, StringComparison.OrdinalIgnoreCase) && string.Equals(entry.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase) && entry.BaselineState == FirstRunBaselineState.CompletedVerified && !string.Equals(entry.LifecycleState, "Deprecated", StringComparison.OrdinalIgnoreCase))
					{
						entry.LifecycleState = "Deprecated";
					}
				}
			}
			ApplyManifestToEntry(existing, manifest);
			CleanupDeprecatedEntries(catalog, "Upsert");
			catalog.UpdatedUtc = DateTime.UtcNow;
			await SaveCatalogCoreAsync(catalog, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<BaselinePackageManifest?> GetManifestAsync(string packageId, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return null;
		}
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			string packageDirectory = Path.Combine(PackagesDir, packageId.Trim());
			string manifestPath = Path.Combine(packageDirectory, "manifest.json");
			if (!File.Exists(manifestPath))
			{
				return null;
			}
			BaselinePackageManifest? manifest = JsonSerializer.Deserialize<BaselinePackageManifest>(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(continueOnCapturedContext: false), JsonOptions);
			if (manifest == null)
			{
				return null;
			}
			manifest.PackageId = (string.IsNullOrWhiteSpace(manifest.PackageId) ? packageId.Trim() : manifest.PackageId.Trim());
			manifest.CreationSource = NormalizeCreationSource(manifest.CreationSource);
			manifest.LifecycleState = NormalizeLifecycleState(manifest.LifecycleState);
			manifest.MirrorSyncState = NormalizeMirrorSyncState(manifest.MirrorSyncState);
			manifest.RecoverabilityCheckState = NormalizeRecoverabilityCheckState(manifest.RecoverabilityCheckState);
			return manifest;
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.LogWarning(ex2, "[BaselineCatalogService][GetManifestAsync] Read manifest failed. package={PackageId}", packageId);
			return null;
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<(bool Success, string ErrorMessage)> VerifyPackageAsync(string packageId, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return (Success: false, ErrorMessage: "PackageId is empty.");
		}
		BaselinePackageManifest? manifest = await GetManifestAsync(packageId, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (manifest == null)
		{
			return (Success: false, ErrorMessage: "Manifest not found.");
		}
		string packageDirectory = Path.Combine(PackagesDir, packageId.Trim());
		(bool Success, string ErrorMessage) verifyResult = await _integrityService.VerifyPackageAsync(packageDirectory, manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
		manifest.IntegrityState = (verifyResult.Success ? "Verified" : "Corrupted");
		manifest.BaselineState = (verifyResult.Success ? FirstRunBaselineState.CompletedVerified : FirstRunBaselineState.Corrupted);
		manifest.LifecycleState = (verifyResult.Success ? ResolveLifecycleAfterVerify(manifest.LifecycleState) : "Corrupted");
		await UpsertAsync(manifest, ct).ConfigureAwait(continueOnCapturedContext: false);
		return verifyResult;
	}

	public async Task<(bool Success, string ErrorMessage, string ExportPath)> ExportPackageAsync(string packageId, string outputDirectory, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return (Success: false, ErrorMessage: "PackageId is empty.", ExportPath: string.Empty);
		}
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			return (Success: false, ErrorMessage: "Output directory is empty.", ExportPath: string.Empty);
		}
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			string packageDirectory = Path.Combine(PackagesDir, packageId.Trim());
			if (!Directory.Exists(packageDirectory))
			{
				return (Success: false, ErrorMessage: "Package directory not found.", ExportPath: string.Empty);
			}
			Directory.CreateDirectory(outputDirectory);
			string exportFilePath = Path.Combine(outputDirectory, $"{packageId.Trim()}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
			if (File.Exists(exportFilePath))
			{
				File.Delete(exportFilePath);
			}
			await Task.Run(delegate
			{
				ZipFile.CreateFromDirectory(packageDirectory, exportFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);
			}, ct).ConfigureAwait(continueOnCapturedContext: false);
			return (Success: true, ErrorMessage: string.Empty, ExportPath: exportFilePath);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.LogWarning(ex2, "[BaselineCatalogService][ExportPackageAsync] Export failed. package={PackageId}", packageId);
			return (Success: false, ErrorMessage: ex2.Message, ExportPath: string.Empty);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<string?> GetPackageDirectoryAsync(string packageId, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return null;
		}
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			string packageDirectory = Path.Combine(PackagesDir, packageId.Trim());
			return Directory.Exists(packageDirectory) ? packageDirectory : null;
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task<(bool Success, string ErrorMessage)> DeletePackageAsync(string packageId, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return (Success: false, ErrorMessage: "PackageId is empty.");
		}
		await _lock.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			string normalized = packageId.Trim();
			BaselineCatalog catalog = await LoadCatalogCoreAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			BaselineCatalogEntry? entry = catalog.Entries.FirstOrDefault((BaselineCatalogEntry item) => string.Equals(item.PackageId, normalized, StringComparison.OrdinalIgnoreCase));
			if (entry == null)
			{
				return (Success: false, ErrorMessage: "Package not found.");
			}
			if (entry.BaselineState == FirstRunBaselineState.Running)
			{
				return (Success: false, ErrorMessage: "Running package cannot be deleted.");
			}
			int verifiedCount = catalog.Entries.Count((BaselineCatalogEntry item) => string.Equals(item.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase));
			if (string.Equals(entry.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase) && verifiedCount <= 2)
			{
				return (Success: false, ErrorMessage: "At least two verified baseline packages must be retained.");
			}
			string packageDirectory = Path.Combine(PackagesDir, normalized);
			if (Directory.Exists(packageDirectory))
			{
				Directory.Delete(packageDirectory, recursive: true);
			}
			catalog.Entries.Remove(entry);
			await SaveCatalogCoreAsync(catalog, ct).ConfigureAwait(continueOnCapturedContext: false);
			return (Success: true, ErrorMessage: string.Empty);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.LogWarning(ex2, "[BaselineCatalogService][DeletePackageAsync] Delete package failed. package={PackageId}", packageId);
			return (Success: false, ErrorMessage: ex2.Message);
		}
		finally
		{
			_lock.Release();
		}
	}

	private static async Task<BaselineCatalog> LoadCatalogCoreAsync(CancellationToken ct)
	{
		Directory.CreateDirectory(BaselineDir);
		Directory.CreateDirectory(PackagesDir);
		if (!File.Exists(CatalogPath))
		{
			return new BaselineCatalog();
		}
		try
		{
			BaselineCatalog? catalog = JsonSerializer.Deserialize<BaselineCatalog>(await File.ReadAllTextAsync(CatalogPath, ct).ConfigureAwait(continueOnCapturedContext: false), JsonOptions);
			return catalog ?? new BaselineCatalog();
		}
		catch
		{
			return new BaselineCatalog();
		}
	}

	private static async Task SaveCatalogCoreAsync(BaselineCatalog catalog, CancellationToken ct)
	{
		catalog.UpdatedUtc = DateTime.UtcNow;
		string tempFile = $"{CatalogPath}.{Guid.NewGuid():N}.tmp";
		string json = JsonSerializer.Serialize(catalog, JsonOptions);
		await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(continueOnCapturedContext: false);
		File.Move(tempFile, CatalogPath, overwrite: true);
	}

	private async Task<bool> ReconcileCatalogEntriesAsync(BaselineCatalog catalog, CancellationToken ct)
	{
		bool changed = false;
		int removedInvalidEntries = catalog.Entries.RemoveAll((BaselineCatalogEntry baselineCatalogEntry) => string.IsNullOrWhiteSpace(baselineCatalogEntry.PackageId));
		if (removedInvalidEntries > 0)
		{
			changed = true;
		}
		Dictionary<string, BaselineCatalogEntry> dedupeMap = new Dictionary<string, BaselineCatalogEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (BaselineCatalogEntry entry in catalog.Entries.ToList())
		{
			string normalizedPackageId = entry.PackageId.Trim();
			if (!string.Equals(entry.PackageId, normalizedPackageId, StringComparison.Ordinal))
			{
				entry.PackageId = normalizedPackageId;
				changed = true;
			}
			if (!dedupeMap.TryGetValue(entry.PackageId, out BaselineCatalogEntry? existing) || existing == null)
			{
				dedupeMap[entry.PackageId] = entry;
				continue;
			}
			bool keepCurrent = entry.CreatedUtc >= existing.CreatedUtc;
			BaselineCatalogEntry duplicate = (keepCurrent ? existing : entry);
			catalog.Entries.Remove(duplicate);
			if (keepCurrent)
			{
				dedupeMap[entry.PackageId] = entry;
			}
			changed = true;
		}
		foreach (BaselineCatalogEntry entry2 in catalog.Entries.ToList())
		{
			ct.ThrowIfCancellationRequested();
			string packageDirectory = Path.Combine(PackagesDir, entry2.PackageId);
			string manifestPath = Path.Combine(packageDirectory, "manifest.json");
			if (!Directory.Exists(packageDirectory) || !File.Exists(manifestPath))
			{
				catalog.Entries.Remove(entry2);
				changed = true;
				_logger.LogWarning("[BaselineCatalogService][Reconcile] Removed stale catalog entry. package={PackageId}, dirExists={DirExists}, manifestExists={ManifestExists}", entry2.PackageId, Directory.Exists(packageDirectory), File.Exists(manifestPath));
			}
		}
		HashSet<string> indexedPackageIds = new HashSet<string>(catalog.Entries.Select((BaselineCatalogEntry baselineCatalogEntry) => baselineCatalogEntry.PackageId), StringComparer.OrdinalIgnoreCase);
		string[] directories = Directory.GetDirectories(PackagesDir);
		foreach (string packageDirectory2 in directories)
		{
			ct.ThrowIfCancellationRequested();
			string packageId = Path.GetFileName(packageDirectory2);
			if (!string.IsNullOrWhiteSpace(packageId) && !indexedPackageIds.Contains(packageId))
			{
				BaselinePackageManifest? manifest = await ReadManifestFromDirectoryAsync(packageDirectory2, ct).ConfigureAwait(continueOnCapturedContext: false);
				BaselineCatalogEntry recoveredEntry;
				if (manifest == null)
				{
					recoveredEntry = new BaselineCatalogEntry
					{
						PackageId = packageId,
						PackageType = "BaselineFirstRun",
						CreationSource = "RecoveredMissingManifest",
						LifecycleState = "Corrupted",
						CreatedUtc = Directory.GetCreationTimeUtc(packageDirectory2),
						AppVersion = string.Empty,
						IntegrityState = "Corrupted",
						BaselineState = FirstRunBaselineState.Corrupted,
						DomainSucceededCount = 0,
						DomainFailedCount = 0,
						RequiresReboot = false,
						MirrorDirectory = string.Empty,
						MirrorSyncState = "NotConfigured",
						MirrorSyncMessage = "manifest 丢失，目录通过自动对账接入。",
						MirrorSyncedUtc = null,
						LastRecoverabilityCheckUtc = null,
						RecoverabilityCheckState = "Failed",
						RecoverabilityCheckMessage = "manifest 丢失，需重新创建基线。"
					};
				}
				else
				{
					manifest.PackageId = packageId;
					manifest.CreationSource = NormalizeCreationSource(manifest.CreationSource);
					manifest.LifecycleState = NormalizeLifecycleState(manifest.LifecycleState);
					manifest.MirrorSyncState = NormalizeMirrorSyncState(manifest.MirrorSyncState);
					manifest.RecoverabilityCheckState = NormalizeRecoverabilityCheckState(manifest.RecoverabilityCheckState);
					recoveredEntry = new BaselineCatalogEntry();
					ApplyManifestToEntry(recoveredEntry, manifest);
				}
				NormalizeEntryInPlace(recoveredEntry);
				catalog.Entries.Add(recoveredEntry);
				indexedPackageIds.Add(recoveredEntry.PackageId);
				changed = true;
				_logger.LogInformation("[BaselineCatalogService][Reconcile] Recovered package into catalog. package={PackageId}, hasManifest={HasManifest}", recoveredEntry.PackageId, manifest != null);
			}
		}
		return changed;
	}

	private async Task<BaselinePackageManifest?> ReadManifestFromDirectoryAsync(string packageDirectory, CancellationToken ct)
	{
		string manifestPath = Path.Combine(packageDirectory, "manifest.json");
		if (!File.Exists(manifestPath))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<BaselinePackageManifest>(await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(continueOnCapturedContext: false), JsonOptions);
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "[BaselineCatalogService][ReadManifestFromDirectoryAsync] Read manifest failed. dir={PackageDirectory}", packageDirectory);
			return null;
		}
	}

	private static void ApplyManifestToEntry(BaselineCatalogEntry entry, BaselinePackageManifest manifest)
	{
		entry.PackageId = manifest.PackageId;
		entry.PackageType = manifest.PackageType;
		entry.CreationSource = manifest.CreationSource;
		entry.LifecycleState = manifest.LifecycleState;
		entry.CreatedUtc = manifest.CreatedUtc;
		entry.AppVersion = manifest.AppVersion;
		entry.IntegrityState = manifest.IntegrityState;
		entry.BaselineState = manifest.BaselineState;
		entry.DomainSucceededCount = manifest.Domains.Count((BaselineDomainArtifact domain) => domain.Status == BaselineDomainStatus.Succeeded);
		entry.DomainFailedCount = manifest.Domains.Count((BaselineDomainArtifact domain) => domain.Status == BaselineDomainStatus.Failed);
		entry.RequiresReboot = manifest.RequiresReboot;
		entry.MirrorDirectory = manifest.MirrorDirectory ?? string.Empty;
		entry.MirrorSyncState = manifest.MirrorSyncState;
		entry.MirrorSyncMessage = manifest.MirrorSyncMessage ?? string.Empty;
		entry.MirrorSyncedUtc = manifest.MirrorSyncedUtc;
		entry.LastRecoverabilityCheckUtc = manifest.LastRecoverabilityCheckUtc;
		entry.RecoverabilityCheckState = manifest.RecoverabilityCheckState;
		entry.RecoverabilityCheckMessage = manifest.RecoverabilityCheckMessage ?? string.Empty;
	}

	private bool CleanupDeprecatedEntries(BaselineCatalog catalog, string reason)
	{
		bool result = false;
		DateTime utcNow = DateTime.UtcNow;
		List<BaselineCatalogEntry> list = (from entry in catalog.Entries
			where string.Equals(entry.LifecycleState, "Deprecated", StringComparison.OrdinalIgnoreCase)
			orderby entry.CreatedUtc descending
			select entry).ToList();
		if (list.Count == 0)
		{
			return false;
		}
		HashSet<string> hashSet = new HashSet<string>(from entry in list.Take(6)
			select entry.PackageId, StringComparer.OrdinalIgnoreCase);
		foreach (BaselineCatalogEntry item in list)
		{
			if (item.BaselineState == FirstRunBaselineState.Running)
			{
				continue;
			}
			TimeSpan timeSpan = utcNow - item.CreatedUtc;
			bool flag = timeSpan > DeprecatedRetention;
			bool flag2 = !hashSet.Contains(item.PackageId);
			if (flag || flag2)
			{
				bool flag3 = string.Equals(item.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase);
				int num = catalog.Entries.Count((BaselineCatalogEntry item) => string.Equals(item.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase));
				if (!flag3 || num > 2)
				{
					catalog.Entries.Remove(item);
					result = true;
					string directoryPath = Path.Combine(PackagesDir, item.PackageId);
					TryDeleteDirectorySafe(directoryPath);
					_logger.LogInformation("[BaselineCatalogService][CleanupDeprecated] Removed deprecated package. package={PackageId}, reason={Reason}, ageDays={AgeDays:F1}, countLimit={CountLimit}, ageLimit={AgeLimit}", item.PackageId, reason, timeSpan.TotalDays, flag2, flag);
				}
			}
		}
		return result;
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

	private static bool NormalizeEntryInPlace(BaselineCatalogEntry entry)
	{
		bool result = false;
		string text = NormalizeCreationSource(entry.CreationSource);
		if (!string.Equals(entry.CreationSource, text, StringComparison.Ordinal))
		{
			entry.CreationSource = text;
			result = true;
		}
		string text2 = NormalizeLifecycleState(entry.LifecycleState);
		if (!string.Equals(entry.LifecycleState, text2, StringComparison.Ordinal))
		{
			entry.LifecycleState = text2;
			result = true;
		}
		string text3 = NormalizeMirrorSyncState(entry.MirrorSyncState);
		if (!string.Equals(entry.MirrorSyncState, text3, StringComparison.Ordinal))
		{
			entry.MirrorSyncState = text3;
			result = true;
		}
		string text4 = NormalizeRecoverabilityCheckState(entry.RecoverabilityCheckState);
		if (!string.Equals(entry.RecoverabilityCheckState, text4, StringComparison.Ordinal))
		{
			entry.RecoverabilityCheckState = text4;
			result = true;
		}
		return result;
	}

	private static string NormalizeCreationSource(string? creationSource)
	{
		return string.IsNullOrWhiteSpace(creationSource) ? "Unknown" : creationSource.Trim();
	}

	private static string NormalizeLifecycleState(string? lifecycleState)
	{
		if (string.IsNullOrWhiteSpace(lifecycleState))
		{
			return "Active";
		}
		return lifecycleState.Trim();
	}

	private static string ResolveLifecycleAfterVerify(string? lifecycleState)
	{
		if (string.Equals(lifecycleState, "Deprecated", StringComparison.OrdinalIgnoreCase))
		{
			return "Deprecated";
		}
		return "Active";
	}

	private static string NormalizeMirrorSyncState(string? mirrorSyncState)
	{
		if (string.IsNullOrWhiteSpace(mirrorSyncState))
		{
			return "NotConfigured";
		}
		string text = mirrorSyncState.Trim();
		string result = text switch
		{
			"Pending" => "Pending", 
			"Synced" => "Synced", 
			"Failed" => "Failed", 
			_ => "NotConfigured", 
		};
		return result;
	}

	private static string NormalizeRecoverabilityCheckState(string? recoverabilityCheckState)
	{
		if (string.IsNullOrWhiteSpace(recoverabilityCheckState))
		{
			return "NotChecked";
		}
		string text = recoverabilityCheckState.Trim();
		string result = ((text == "Passed") ? "Passed" : ((!(text == "Failed")) ? "NotChecked" : "Failed"));
		return result;
	}
}
