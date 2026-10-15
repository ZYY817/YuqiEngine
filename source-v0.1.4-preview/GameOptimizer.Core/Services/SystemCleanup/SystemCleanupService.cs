using System.Diagnostics;
using System.Collections.Concurrent;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.SystemCleanup;

public sealed class SystemCleanupService : ISystemCleanupService
{
    private readonly List<CleanupItem> _items;
    private readonly ICleanupAuthorizationGate _authorizationGate;
    private readonly ICleanupQuarantine _quarantine;
    private readonly IReadOnlyDictionary<string, CleanupItem> _definitions;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, string> _issuedPreviews = new();

    public SystemCleanupService() : this(new DenyCleanupAuthorizationGate(), new FileSystemCleanupQuarantine())
    {
    }

    // DI must select this production composition. The test factory below accepts
    // an explicit catalog, but exposing that IEnumerable constructor would let
    // DI resolve it as an empty collection and silently publish an empty
    // immutable allowlist.
    public SystemCleanupService(
        ICleanupAuthorizationGate authorizationGate,
        ICleanupQuarantine quarantine)
        : this(authorizationGate, quarantine, null, null)
    {
    }

    internal static SystemCleanupService CreateForTesting(
        ICleanupAuthorizationGate authorizationGate,
        ICleanupQuarantine quarantine,
        IEnumerable<CleanupItem> definitions,
        TimeProvider? timeProvider = null) =>
        new(authorizationGate, quarantine, definitions, timeProvider);

    private SystemCleanupService(
        ICleanupAuthorizationGate authorizationGate,
        ICleanupQuarantine quarantine,
        IEnumerable<CleanupItem>? definitions = null,
        TimeProvider? timeProvider = null)
    {
        _authorizationGate = authorizationGate ?? throw new ArgumentNullException(nameof(authorizationGate));
        _quarantine = quarantine ?? throw new ArgumentNullException(nameof(quarantine));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _items = (definitions ?? BuildDefinitions()).ToList();
        _definitions = _items
            .Select(SnapshotDefinition)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<CleanupItem> GetCleanupItems() => _items.AsReadOnly();

    public Task<CleanupPreview> CreatePreviewAsync(CleanupItem item, CancellationToken ct = default) =>
        CreatePreviewAsync(item, progress: null, ct: ct);

    public Task<CleanupPreview> CreatePreviewAsync(
        CleanupItem item,
        IProgress<CleanupScanProgress>? progress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_definitions.TryGetValue(item.Id, out CleanupItem? definition))
        {
            throw new InvalidOperationException("Cleanup item is not part of the immutable allowlist catalog.");
        }
        return Task.Run(async () =>
        {
            CleanupPreview preview = await CleanupPreviewBuilder.BuildAsync(definition, progress, ct).ConfigureAwait(false);
            if (!_issuedPreviews.TryAdd(preview.PreviewId, preview.Fingerprint))
            {
                throw new InvalidOperationException("Cleanup preview identity collision.");
            }
            return preview;
        }, ct);
    }

    public async Task<long> ScanItemAsync(CleanupItem item, CancellationToken ct = default)
    {
        CleanupPreview preview = await CreatePreviewAsync(item, ct).ConfigureAwait(false);
        item.Size = preview.TotalBytes;
        item.FileCount = preview.Entries.Count;
        InvalidatePreview(preview);
        return preview.TotalBytes;
    }

    public void InvalidatePreview(CleanupPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (_issuedPreviews.TryGetValue(preview.PreviewId, out string? fingerprint) &&
            string.Equals(fingerprint, preview.Fingerprint, StringComparison.Ordinal))
        {
            _issuedPreviews.TryRemove(preview.PreviewId, out _);
        }
    }

    public async Task<CleanupResult> CleanItemAsync(CleanupItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        var result = new CleanupResult();
        result.Errors.Add("cleanup.confirmation-required: Cleanup requires the exact immutable preview confirmed by the user.");
        return await Task.FromResult(result).ConfigureAwait(false);
    }

    public async Task<CleanupResult> CleanPreviewAsync(
        CleanupItem item,
        CleanupPreview preview,
        CleanupPreviewConfirmation confirmation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(confirmation);
        var result = new CleanupResult();
        if (!_definitions.TryGetValue(item.Id, out CleanupItem? definition))
        {
            result.Errors.Add("cleanup.item-not-allowlisted: Cleanup item is not part of the immutable allowlist catalog.");
            return result;
        }

        if (!string.Equals(preview.ItemId, item.Id, StringComparison.Ordinal))
        {
            result.Errors.Add("cleanup.preview-item-mismatch: Preview does not belong to the requested cleanup item.");
            return result;
        }

        if (!confirmation.Matches(preview, _timeProvider.GetUtcNow()))
        {
            result.Errors.Add("cleanup.confirmation-invalid-or-expired: Confirmation is not bound to this preview or has expired.");
            return result;
        }

        if (!_issuedPreviews.TryRemove(preview.PreviewId, out string? issuedFingerprint) ||
            !string.Equals(issuedFingerprint, preview.Fingerprint, StringComparison.Ordinal))
        {
            result.Errors.Add("cleanup.preview-not-issued-or-replayed: Preview was not issued by this service or was already consumed.");
            return result;
        }

        CleanupAuthorizationDecision authorization;
        try
        {
            authorization = await _authorizationGate.AuthorizeAsync(preview, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result.FileResults.Add(new("", CleanupFileOutcome.Cancelled, 0, "cleanup.cancelled-authorization", "Cleanup authorization was cancelled."));
            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add("cleanup.authorization-failed: " + ex.Message);
            return result;
        }

        if (!authorization.Allowed)
        {
            result.Errors.Add(authorization.ReasonCode + ": " + authorization.Message);
            return result;
        }

        if (definition.UseShellApi)
        {
            result.Errors.Add("cleanup.shell-api-not-recoverable: Recycle-bin emptying is blocked until a recoverable owner is implemented.");
            return result;
        }

        if (definition.BlockingProcesses.Length > 0)
        {
            List<string> running = GetRunningBlockingProcesses(definition.BlockingProcesses);
            if (running.Count > 0)
            {
                result.Errors.Add(LocalizationService.Instance.Get("Cleanup.CloseProcessFirst", string.Join(", ", running)));
                return result;
            }
        }

        foreach (CleanupPreviewEntry entry in preview.Entries)
        {
            if (ct.IsCancellationRequested)
            {
                result.FileResults.Add(new(entry.CanonicalPath, CleanupFileOutcome.Cancelled, 0, "cleanup.cancelled", "Cleanup cancelled before this item."));
                break;
            }

            string rootReason = string.Empty;
            string pathReason = string.Empty;
            string currentRoot = string.Empty;
            string currentPath = string.Empty;
            bool pathAllowed = CleanupPathPolicy.TryValidateRoot(entry.AllowlistedRoot, out currentRoot, out rootReason) &&
                CleanupPathPolicy.TryValidateCandidate(currentRoot, entry.CanonicalPath, out currentPath, out pathReason);
            if (!pathAllowed)
            {
                AddFailure(result, entry, CleanupFileOutcome.BlockedPath, rootReason.Length > 0 ? rootReason : pathReason, "Path failed containment or reparse validation.");
                continue;
            }

            try
            {
                await CleanupQuarantineManifestStore.EnsureEntryStillMatchesAsync(
                    entry with { CanonicalPath = currentPath },
                    ct,
                    requireContentHash: true).ConfigureAwait(false);

                string operationId = preview.PreviewId.ToString("N") + "-" + entry.ResourceKey["cleanup.resource.".Length..][..16];
                CleanupQuarantineMove move = await _quarantine.MoveWithReceiptAsync(
                    entry with { CanonicalPath = currentPath },
                    item.Id,
                    operationId,
                    ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(move.ManifestPath))
                {
                    AddFailure(result, entry, CleanupFileOutcome.Failed, "cleanup.receipt-not-durable", "Quarantine did not return a durable manifest receipt.");
                    continue;
                }

                result.TotalCleaned += entry.SizeBytes;
                result.FilesDeleted++;
                result.FileResults.Add(new(
                    currentPath,
                    CleanupFileOutcome.Quarantined,
                    entry.SizeBytes,
                    "cleanup.quarantined-verified",
                    "Moved to recoverable quarantine and recorded a durable manifest receipt.",
                    move.QuarantinePath,
                    move.OperationId,
                    move.ManifestPath));
            }
            catch (OperationCanceledException)
            {
                result.FileResults.Add(new(entry.CanonicalPath, CleanupFileOutcome.Cancelled, 0, "cleanup.cancelled", "Cleanup cancelled before mutation completed."));
                break;
            }
            catch (UnauthorizedAccessException ex)
            {
                AddFailure(result, entry, CleanupFileOutcome.PermissionDenied, "cleanup.permission-denied", ex.Message);
            }
            catch (CleanupQuarantinePersistenceException ex)
            {
                AddFailure(
                    result,
                    entry,
                    ex.ReasonCode.Contains("cancelled-after", StringComparison.Ordinal)
                        ? CleanupFileOutcome.CancelledAfterPartialQuarantine
                        : CleanupFileOutcome.Failed,
                    ex.ReasonCode,
                    ex.Message);
            }
            catch (CleanupPreviewDriftException ex)
            {
                AddFailure(result, entry, CleanupFileOutcome.ChangedSincePreview, ex.ReasonCode, ex.Message);
            }
            catch (IOException ex)
            {
                string code = ex.Message.Contains("cleanup.cross-volume-quarantine-blocked", StringComparison.Ordinal)
                    ? "cleanup.cross-volume-quarantine-blocked"
                    : ex.Message.Contains("cleanup.resource-lease-unavailable", StringComparison.Ordinal)
                        ? "cleanup.resource-lease-unavailable"
                        : "cleanup.file-locked-or-io";
                AddFailure(result, entry, CleanupFileOutcome.Locked, code, ex.Message);
            }
            catch (Exception ex)
            {
                AddFailure(result, entry, CleanupFileOutcome.Failed, "cleanup.quarantine-failed", ex.Message);
            }
        }

        return result;
    }

    private static void AddFailure(CleanupResult result, CleanupPreviewEntry entry, CleanupFileOutcome outcome, string reasonCode, string message)
    {
        result.FilesFailed++;
        result.Errors.Add($"{entry.CanonicalPath}: {reasonCode}: {message}");
        result.FileResults.Add(new(entry.CanonicalPath, outcome, 0, reasonCode, message));
    }

    private static List<string> GetRunningBlockingProcesses(IEnumerable<string> processNames)
    {
        var running = new List<string>();
        foreach (string name in processNames)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(name);
                if (processes.Length > 0)
                {
                    running.Add(name);
                }
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemCleanup] Process check failed for {name}: {ex.Message}");
            }
        }
        return running;
    }

    private static List<CleanupItem> BuildDefinitions()
    {
        LocalizationService loc = LocalizationService.Instance;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Item("system_temp", CleanupCategory.System, RiskLevel.Safe, [Path.Combine(windows, "Temp")]),
            Item("user_temp", CleanupCategory.User, RiskLevel.Safe, [Path.GetTempPath()]),
            Item("prefetch", CleanupCategory.System, RiskLevel.Low, [Path.Combine(windows, "Prefetch")]),
            Item("thumbnail_cache", CleanupCategory.System, RiskLevel.Safe, [Path.Combine(local, "Microsoft", "Windows", "Explorer")], ["thumbcache*.db", "iconcache*.db"], false),
            Item("windows_logs", CleanupCategory.System, RiskLevel.Safe, [Path.Combine(windows, "Logs")]),
            Item("update_cache", CleanupCategory.System, RiskLevel.Medium, [Path.Combine(windows, "SoftwareDistribution", "Download")]),
            new CleanupItem { Id = "recycle_bin", Name = loc.Get("Cleanup.RecycleBin.Name"), Description = loc.Get("Cleanup.RecycleBin.Desc"), Category = CleanupCategory.System, Risk = RiskLevel.Safe, UseShellApi = true },
            Item("gpu_cache", CleanupCategory.Game, RiskLevel.Safe, [Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "NVIDIA", "GLCache"), Path.Combine(local, "AMD", "DxCache"), Path.Combine(local, "D3DSCache")]),
            Item("steam_cache", CleanupCategory.Game, RiskLevel.Safe, GetSteamCachePaths()),
            Item("epic_cache", CleanupCategory.Game, RiskLevel.Safe, [Path.Combine(local, "EpicGamesLauncher", "Saved", "webcache")]),
            Item("chrome_cache", CleanupCategory.Browser, RiskLevel.Safe, [Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Code Cache")], blocking: ["chrome"]),
            Item("edge_cache", CleanupCategory.Browser, RiskLevel.Safe, [Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Code Cache")], blocking: ["msedge"]),
            Item("firefox_cache", CleanupCategory.Browser, RiskLevel.Safe, [Path.Combine(local, "Mozilla", "Firefox", "Profiles")], ["cache2"], true, ["firefox"])
        ];

        CleanupItem Item(string id, CleanupCategory category, RiskLevel risk, string[] paths, string[]? patterns = null, bool recursive = true, string[]? blocking = null) => new()
        {
            Id = id,
            Name = loc.Get($"Cleanup.{ToPascal(id)}.Name"),
            Description = loc.Get($"Cleanup.{ToPascal(id)}.Desc"),
            IconGlyph = "\uE74D",
            Category = category,
            Risk = risk,
            TargetPaths = paths,
            Patterns = patterns ?? [],
            IncludeSubdirectories = recursive,
            BlockingProcesses = blocking ?? []
        };
    }

    private static CleanupItem SnapshotDefinition(CleanupItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Description = item.Description,
        Icon = item.Icon,
        IconGlyph = item.IconGlyph,
        Risk = item.Risk,
        Category = item.Category,
        TargetPaths = item.TargetPaths.ToArray(),
        Patterns = item.Patterns.ToArray(),
        IncludeSubdirectories = item.IncludeSubdirectories,
        BlockingProcesses = item.BlockingProcesses.ToArray(),
        UseShellApi = item.UseShellApi
    };

    private static string ToPascal(string id) => string.Concat(id.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string[] GetSteamCachePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string steamPath && !string.IsNullOrWhiteSpace(steamPath))
            {
                string appCache = Path.GetFullPath(Path.Combine(steamPath, "appcache"));
                if (Directory.Exists(appCache)) paths.Add(appCache);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemCleanup] Steam registry probe failed: {ex.Message}");
        }

        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed))
            {
                foreach (string relative in new[] { @"Program Files (x86)\Steam\appcache", @"Program Files\Steam\appcache", @"Steam\appcache", @"Games\Steam\appcache" })
                {
                    string candidate = Path.GetFullPath(Path.Combine(drive.RootDirectory.FullName, relative));
                    if (Directory.Exists(candidate)) paths.Add(candidate);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemCleanup] Steam drive probe failed: {ex.Message}");
        }

        return paths.ToArray();
    }
}
