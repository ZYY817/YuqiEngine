namespace GameOptimizer.Core.FileShellSafety;

public sealed class FileShellSafetyOwner : IFileShellSafetyOwner
{
    private readonly IFileShellSafetyInspector _inspector;
    private readonly TimeProvider _clock;
    private readonly FileShellSafetyTimingOptions _timing;

    public FileShellSafetyOwner(
        IFileShellSafetyInspector inspector,
        TimeProvider? clock = null,
        FileShellSafetyTimingOptions? timing = null)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _clock = clock ?? TimeProvider.System;
        _timing = timing ?? FileShellSafetyTimingOptions.Default;
        if (_timing.InspectionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timing), "Inspection timeout must be positive.");
    }

    public async Task<FileShellSafetyDecision> EvaluateAsync(
        FileShellSafetyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OperationId))
            return Denied(request, FileShellSafetyStatus.InvalidPath, "A stable operation ID is required.");
        if (cancellationToken.IsCancellationRequested)
            return Denied(request, FileShellSafetyStatus.Cancelled, "Evaluation was cancelled before inspection.");

        if (request.Kind is FileShellMutationKind.PermanentDelete
            or FileShellMutationKind.ExplorerRestart
            or FileShellMutationKind.ShellStateReset
            or FileShellMutationKind.AccessControlMutation)
        {
            return Denied(request, FileShellSafetyStatus.PolicyDenied,
                "This release forbids permanent deletion, Explorer termination/restart, Shell state reset, and ACL mutation.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetPath))
            return Denied(request, FileShellSafetyStatus.InvalidPath, "A concrete target path is required.");

        FileShellSafetyInspection inspection;
        using var deadline = new CancellationTokenSource(_timing.InspectionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            inspection = await _inspector.InspectAsync(request, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Denied(request, FileShellSafetyStatus.Cancelled, "Path inspection was cancelled; no mutation was attempted.");
        }
        catch (OperationCanceledException)
        {
            return Denied(request, FileShellSafetyStatus.TimedOut, "Path inspection timed out; no mutation was attempted.");
        }
        catch (Exception ex)
        {
            return Denied(request, FileShellSafetyStatus.InspectionFailed,
                $"Path inspection failed closed: {ex.GetType().Name}.");
        }

        if (!inspection.IsPathSyntaxValid || string.IsNullOrWhiteSpace(inspection.CanonicalPath))
            return Denied(request, FileShellSafetyStatus.InvalidPath, "The target path is invalid or cannot be canonicalized.");
        if (inspection.ContainsWildcard)
            return Denied(request, FileShellSafetyStatus.WildcardDenied, "Wildcard targets are forbidden.");
        if (inspection.IsRootPath)
            return Denied(request, FileShellSafetyStatus.RootPathDenied, "Filesystem and user-profile roots are forbidden targets.");
        if (!inspection.IsWithinAllowlistedRoot)
            return Denied(request, FileShellSafetyStatus.OutsideAllowlist, "The canonical target is outside the fixed allowlist.");
        if (inspection.IsReparsePoint)
            return Denied(request, FileShellSafetyStatus.ReparsePointDenied, "Reparse points and traversals through them are forbidden.");
        if (string.IsNullOrWhiteSpace(inspection.PreviewSha256))
            return Denied(request, FileShellSafetyStatus.PreviewHashRequired, "A content-bound preview hash is required.");
        if (string.IsNullOrWhiteSpace(request.ConfirmationToken))
            return Denied(request, FileShellSafetyStatus.ConfirmationRequired, "Explicit confirmation bound to the preview is required.");
        if (string.IsNullOrWhiteSpace(inspection.BaselineId))
            return Denied(request, FileShellSafetyStatus.BaselineRequired, "A validated baseline is required before mutation.");
        if (!inspection.IsStateCurrent)
            return Denied(request, FileShellSafetyStatus.DriftBlocked, "The target changed after preview; mutation is blocked.");
        if (!inspection.HasDurableQuarantineManifest)
            return Denied(request, FileShellSafetyStatus.ManifestRequired, "A durable quarantine manifest is required.");
        if (!inspection.HasRestoreProof)
            return Denied(request, FileShellSafetyStatus.RestoreUnproven, "Restore integrity has not been proven.");

        return Denied(request, FileShellSafetyStatus.QuarantineOwnerUnavailable,
            "All safety checks passed, but no production quarantine executor is registered for this legacy entry.");
    }

    private FileShellSafetyDecision Denied(
        FileShellSafetyRequest request,
        FileShellSafetyStatus status,
        string message) =>
        new(Guid.NewGuid(), _clock.GetUtcNow(), status, request.OperationId, request.Kind, false, message);
}

public sealed class UnavailableFileShellSafetyOwner : IFileShellSafetyOwner
{
    public static UnavailableFileShellSafetyOwner Instance { get; } = new();

    private UnavailableFileShellSafetyOwner() { }

    public Task<FileShellSafetyDecision> EvaluateAsync(
        FileShellSafetyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new FileShellSafetyDecision(
                Guid.NewGuid(), DateTimeOffset.UtcNow, FileShellSafetyStatus.Cancelled,
                request.OperationId, request.Kind, false, "Evaluation was cancelled; no mutation was attempted."));
        }

        var status = request.Kind == FileShellMutationKind.QuarantineFiles
            ? FileShellSafetyStatus.QuarantineOwnerUnavailable
            : FileShellSafetyStatus.PolicyDenied;
        return Task.FromResult(new FileShellSafetyDecision(
            Guid.NewGuid(), DateTimeOffset.UtcNow, status, request.OperationId, request.Kind, false,
            request.Kind == FileShellMutationKind.QuarantineFiles
                ? "The legacy entry has no approved quarantine Owner; no file was changed."
                : "This release forbids the requested Explorer, Shell, ACL, or permanent-delete mutation."));
    }
}
