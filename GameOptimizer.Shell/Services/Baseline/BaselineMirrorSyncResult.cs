using System;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineMirrorSyncResult
{
	public string MirrorSyncState { get; init; } = "NotConfigured";

	public string MirrorDirectory { get; init; } = string.Empty;

	public string MirrorSyncMessage { get; init; } = string.Empty;

	public DateTime? MirrorSyncedUtc { get; init; }
}
