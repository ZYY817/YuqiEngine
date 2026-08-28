using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Core.Services.SystemCleanup;

public enum CleanupState
{
    Idle,
    Scanning,
    Cleaning,
    Done,
    Error
}

public enum RiskLevel
{
    Safe,
    Low,
    Medium
}

public enum CleanupCategory
{
    System,
    User,
    Game,
    Browser
}

public class CleanupItem : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "🗑️";
    public string IconGlyph { get; set; } = "\uE74D";
    public RiskLevel Risk { get; set; } = RiskLevel.Safe;
    public CleanupCategory Category { get; set; } = CleanupCategory.System;
    public string[] TargetPaths { get; set; } = Array.Empty<string>();
    public string[] Patterns { get; set; } = Array.Empty<string>();
    public bool IncludeSubdirectories { get; set; } = true;
    public string[] BlockingProcesses { get; set; } = Array.Empty<string>();
    public bool UseShellApi { get; set; } = false;
    
    public string LocalizedName => LocalizationService.Instance.GetOrDefault($"CleanupItem.{Id}.Name", Name);
    public string LocalizedDescription => LocalizationService.Instance.GetOrDefault($"CleanupItem.{Id}.Description", Description);
    
    public string RiskText => LocalizationService.Instance.Get($"Cleanup.Risk.{Risk}");
    
    public string CategoryText => LocalizationService.Instance.Get($"Cleanup.Category.{Category}");

    private long _size;
    public long Size
    {
        get => _size;
        set { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); }
    }

    private int _fileCount;
    public int FileCount
    {
        get => _fileCount;
        set { _fileCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileCountText)); }
    }

    public string FileCountText => LocalizationService.Instance.Get("CleanupConfirm.FileCountFmt", FileCount);

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    private CleanupState _state = CleanupState.Idle;
    public CleanupState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(StateText)); }
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public string SizeText => FormatSize(Size);

    public string StateText => LocalizationService.Instance.Get($"Cleanup.State.{State}");

    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return LocalizationService.Instance.Get("Common.Unknown");
        if (bytes == 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double size = bytes;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }
        return $"{size:F2} {units[i]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CleanupResult
{
    public long TotalCleaned { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesFailed { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<CleanupFileResult> FileResults { get; set; } = new();
}

public enum CleanupFileOutcome
{
    Quarantined,
    SkippedDuplicate,
    BlockedPath,
    ChangedSincePreview,
    Locked,
    PermissionDenied,
    Cancelled,
    CancelledAfterPartialQuarantine,
    Failed
}

public sealed record CleanupPreviewEntry(
    string CanonicalPath,
    string AllowlistedRoot,
    long SizeBytes,
    DateTime LastWriteTimeUtc,
    string ContentSha256 = "",
    Guid PreviewId = default)
{
    public string ResourceKey => CleanupResourceIdentity.CreateResourceKey(AllowlistedRoot, CanonicalPath);

    public CleanupPreviewEntry BindToPreview(Guid previewId) => this with { PreviewId = previewId };
}

public static class CleanupResourceIdentity
{
    public static string CreateResourceKey(string canonicalRoot, string canonicalPath)
    {
        if (string.IsNullOrWhiteSpace(canonicalRoot)) throw new ArgumentException("A canonical allowlisted root is required.", nameof(canonicalRoot));
        if (string.IsNullOrWhiteSpace(canonicalPath)) throw new ArgumentException("A canonical resource path is required.", nameof(canonicalPath));

        string payload = string.Concat(canonicalRoot, '\0', canonicalPath);
        return "cleanup.resource." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character => char.IsAsciiHexDigit(character));
}

public sealed class CleanupPreview
{
    public CleanupPreview(
        string itemId,
        IEnumerable<CleanupPreviewEntry> entries,
        DateTimeOffset createdAtUtc,
        Guid? previewId = null,
        int skippedDirectoryCount = 0,
        int skippedFileCount = 0)
    {
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
        PreviewId = previewId ?? Guid.NewGuid();
        if (PreviewId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(previewId));
        Entries = Array.AsReadOnly((entries ?? throw new ArgumentNullException(nameof(entries)))
            .Select(entry => entry.BindToPreview(PreviewId))
            .ToArray());
        CreatedAtUtc = createdAtUtc;
        TotalBytes = Entries.Sum(entry => entry.SizeBytes);
        Fingerprint = ComputeFingerprint(ItemId, Entries);
        SkippedDirectoryCount = Math.Max(0, skippedDirectoryCount);
        SkippedFileCount = Math.Max(0, skippedFileCount);
    }

    public Guid PreviewId { get; }
    public string ItemId { get; }
    public IReadOnlyList<CleanupPreviewEntry> Entries { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public long TotalBytes { get; }
    public string Fingerprint { get; }
    public int SkippedDirectoryCount { get; }
    public int SkippedFileCount { get; }

    private static string ComputeFingerprint(string itemId, IEnumerable<CleanupPreviewEntry> entries)
    {
        var payload = new StringBuilder(itemId).Append('\n');
        foreach (CleanupPreviewEntry entry in entries)
        {
            payload.Append(entry.CanonicalPath).Append('\0')
                .Append(entry.AllowlistedRoot).Append('\0')
                .Append(entry.SizeBytes).Append('\0')
                .Append(entry.LastWriteTimeUtc.Ticks).Append('\0')
                .Append(entry.ContentSha256).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())));
    }
}

public sealed record CleanupPreviewConfirmation(
    Guid PreviewId,
    string ItemId,
    string PreviewFingerprint,
    DateTimeOffset ConfirmedAtUtc,
    DateTimeOffset ExpiresAtUtc)
{
    public static CleanupPreviewConfirmation Create(CleanupPreview preview, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        return new(preview.PreviewId, preview.ItemId, preview.Fingerprint, now, now.Add(lifetime));
    }

    public bool Matches(CleanupPreview preview, DateTimeOffset now) =>
        PreviewId == preview.PreviewId &&
        string.Equals(ItemId, preview.ItemId, StringComparison.Ordinal) &&
        string.Equals(PreviewFingerprint, preview.Fingerprint, StringComparison.Ordinal) &&
        ConfirmedAtUtc <= now && now <= ExpiresAtUtc;
}

public sealed record CleanupAuthorizationDecision(bool Allowed, string ReasonCode, string Message);

public interface ICleanupAuthorizationGate
{
    Task<CleanupAuthorizationDecision> AuthorizeAsync(CleanupPreview preview, CancellationToken cancellationToken);
}

public sealed record CleanupFileResult(
    string Path,
    CleanupFileOutcome Outcome,
    long Bytes,
    string ReasonCode,
    string Message,
    string? QuarantinePath = null,
    string? OperationId = null,
    string? ManifestPath = null);
