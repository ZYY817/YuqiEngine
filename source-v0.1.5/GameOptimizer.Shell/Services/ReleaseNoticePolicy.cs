namespace GameOptimizer.Shell.Services;

/// <summary>
/// Identifies the acknowledgement that must be shown for the current preview release.
/// This is intentionally independent from logs, backups and first-protection state so
/// an upgrade from an older build cannot silently skip the release boundary notice.
/// </summary>
public static class ReleaseNoticePolicy
{
    public const string CurrentNoticeId = "v0.1.4-preview-scope-brief-2026-09";
}
