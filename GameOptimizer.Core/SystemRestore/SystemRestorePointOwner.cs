using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.SystemRestore;

public sealed partial class SystemRestorePointOwner : ISystemRestorePointOwner
{
    private const int MaximumDescriptionLength = 255;
    private readonly ISystemRestorePointPlatform _platform;
    private readonly ISystemRestorePointWorkflowStore _store;
    private readonly ISystemRestorePointConfirmationAuthority _confirmation;
    private readonly TimeProvider _timeProvider;
    private readonly SystemRestorePointTimingOptions _timing;

    public SystemRestorePointOwner(
        ISystemRestorePointPlatform platform,
        ISystemRestorePointWorkflowStore store,
        ISystemRestorePointConfirmationAuthority confirmation,
        TimeProvider? timeProvider = null,
        SystemRestorePointTimingOptions? timing = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timing = timing ?? SystemRestorePointTimingOptions.Default;
        if (_timing.PlanLifetime <= TimeSpan.Zero
            || _timing.ConfirmationLifetime <= TimeSpan.Zero
            || _timing.ResourceLeaseTimeout <= TimeSpan.Zero
            || _timing.ExecutionDeadline <= TimeSpan.Zero
            || _timing.IndependentReadBackTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timing), "All System Restore workflow timeouts must be positive.");
        }
    }

    public async Task<SystemRestorePointWorkflowResult> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var probe = await _platform.ProbeAsync(cancellationToken).ConfigureAwait(false);
            return FailedOrReadyForRead(probe);
        }
        catch (OperationCanceledException)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Inspect,
                "System Restore inspection was cancelled.");
        }
        catch (Exception ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.Unknown, SystemRestorePointPhase.Inspect,
                $"System Restore inspection failed: {ex.Message}");
        }
    }

    public async Task<SystemRestorePointWorkflowResult> PrepareCreateAsync(
        SystemRestorePointCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var description = NormalizeDescription(request.Description);
        if (description is null || string.IsNullOrWhiteSpace(request.Source))
        {
            return Failed(SystemRestorePointWorkflowStatus.InvalidRequest, SystemRestorePointPhase.Prepare,
                "A non-empty source and a valid restore-point description are required.");
        }

        SystemRestorePointProbe probe;
        try
        {
            probe = await _platform.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Prepare,
                "Restore-point preparation was cancelled.");
        }
        catch (Exception ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.Unknown, SystemRestorePointPhase.Prepare,
                $"System Restore preflight could not be read: {ex.Message}");
        }

        var preflightFailure = ValidateCreatePreflight(probe, _timeProvider.GetUtcNow());
        if (preflightFailure is not null)
        {
            return preflightFailure;
        }

        var now = _timeProvider.GetUtcNow();
        var plan = new SystemRestorePointCreatePlan(
            Guid.NewGuid(),
            string.Empty,
            now,
            now.Add(_timing.PlanLifetime),
            description,
            request.Source.Trim(),
            NormalizeOptional(request.BaselineId),
            probe.SystemVolumeIdentity,
            SystemRestorePointCollections.Freeze(probe.RestorePoints),
            probe.CreationCooldown,
            RequiresAdministrator: true,
            RequiresExplicitConfirmation: true);
        plan = plan with { PlanHash = ComputeHash(plan) };

        try
        {
            await _store.SaveCreatePlanAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Prepare,
                "Saving the prepared restore-point plan was cancelled.");
        }
        catch (Exception ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.StoreFailure, SystemRestorePointPhase.Prepare,
                $"The prepared restore-point plan could not be persisted: {ex.Message}");
        }

        return new SystemRestorePointWorkflowResult(
            true,
            false,
            SystemRestorePointWorkflowStatus.Ready,
            SystemRestorePointPhase.Prepare,
            "Restore-point creation is prepared and frozen. Explicit confirmation is still required.",
            Probe: probe,
            CreatePlan: plan);
    }

    public SystemRestorePointConfirmationToken? ConfirmCreate(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointConfirmationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidence);
        var now = _timeProvider.GetUtcNow();
        if (!evidence.ExplicitlyConfirmed
            || evidence.PlanId != plan.PlanId
            || !FixedEquals(evidence.PlanHash, plan.PlanHash)
            || evidence.ConfirmedAtUtc < plan.PreparedAtUtc
            || evidence.ConfirmedAtUtc > now.AddSeconds(30)
            || plan.ExpiresAtUtc <= now
            || !FixedEquals(plan.PlanHash, ComputeHash(plan with { PlanHash = string.Empty })))
        {
            return null;
        }

        return _confirmation.Issue(plan, now, _timing.ConfirmationLifetime);
    }

    public async Task<SystemRestorePointWorkflowResult> ExecuteCreateAsync(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointConfirmationToken confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmation);
        if (cancellationToken.IsCancellationRequested)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Confirmation,
                "Restore-point creation was cancelled before execution.");
        }

        var frozenFailure = ValidateCreatePlan(plan);
        if (frozenFailure is not null)
        {
            return frozenFailure;
        }

        var now = _timeProvider.GetUtcNow();
        if (plan.ExpiresAtUtc <= now
            || !FixedEquals(plan.PlanHash, ComputeHash(plan with { PlanHash = string.Empty })))
        {
            return Failed(SystemRestorePointWorkflowStatus.Expired, SystemRestorePointPhase.Confirmation,
                "The prepared restore-point plan expired or its frozen content was modified.");
        }

        SystemRestorePointCreatePlan? persisted;
        try
        {
            persisted = await _store.LoadCreatePlanAsync(plan.PlanId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Confirmation,
                "Loading the prepared restore-point plan was cancelled.");
        }
        catch (Exception ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.StoreFailure, SystemRestorePointPhase.Confirmation,
                $"The prepared restore-point plan could not be loaded: {ex.Message}");
        }

        if (persisted is null
            || !FixedEquals(persisted.PlanHash, plan.PlanHash)
            || !FixedEquals(ComputeHash(persisted with { PlanHash = string.Empty }), plan.PlanHash)
            || !_confirmation.TryConsume(plan, confirmation, now))
        {
            return Failed(SystemRestorePointWorkflowStatus.ConfirmationRequired, SystemRestorePointPhase.Confirmation,
                "Confirmation is missing, expired, forged, replayed, or does not match the persisted plan.");
        }

        try
        {
            if (await _store.LoadCreateClaimAsync(plan.PlanId, plan.PlanHash, cancellationToken).ConfigureAwait(false) is not null)
            {
                return Failed(SystemRestorePointWorkflowStatus.ReplayBlocked, SystemRestorePointPhase.Confirmation,
                    "This restore-point creation plan already has a durable claim and cannot be replayed.");
            }
        }
        catch (OperationCanceledException)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Confirmation,
                "Reading the restore-point creation claim was cancelled.");
        }
        catch (Exception ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.StoreFailure, SystemRestorePointPhase.Confirmation,
                $"The restore-point creation claim could not be authenticated: {ex.Message}");
        }

        ISystemRestorePointResourceLease lease;
        try
        {
            lease = await _store.AcquireResourceLeaseAsync(
                plan.ResourceKey,
                _timing.ResourceLeaseTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.DriftCheck,
                "Waiting for the System Restore resource lease was cancelled.");
        }
        catch (TimeoutException ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.TimedOut, SystemRestorePointPhase.DriftCheck,
                $"The System Restore resource lease timed out before any platform write: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.StoreFailure, SystemRestorePointPhase.DriftCheck,
                $"The System Restore resource lease is unavailable: {ex.Message}");
        }

        await using (lease.ConfigureAwait(false))
        {
            SystemRestorePointProbe beforeWrite;
            try
            {
                beforeWrite = await _platform.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.DriftCheck,
                    "Restore-point creation was cancelled before the write phase.");
            }
            catch (Exception ex)
            {
                return Failed(SystemRestorePointWorkflowStatus.Unknown, SystemRestorePointPhase.DriftCheck,
                    $"Authoritative pre-write read failed: {ex.Message}");
            }

            var driftFailure = ValidateNoDrift(plan, beforeWrite, _timeProvider.GetUtcNow());
            if (driftFailure is not null)
            {
                return driftFailure;
            }

            var receiptId = Guid.NewGuid();
            var claim = new SystemRestorePointCreateClaim(
                Guid.NewGuid(),
                plan.PlanId,
                plan.PlanHash,
                receiptId,
                _timeProvider.GetUtcNow(),
                _timeProvider.GetUtcNow(),
                SystemRestorePointCreateClaimState.Executing,
                SystemRestorePointMutationState.Unknown);
            if (cancellationToken.IsCancellationRequested)
            {
                return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Confirmation,
                    "Restore-point creation was cancelled before the durable claim was started.");
            }
            try
            {
                using var claimCts = new CancellationTokenSource(_timing.IndependentReadBackTimeout);
                if (await _store.TryClaimCreatePlanAsync(claim, claimCts.Token).ConfigureAwait(false) is null)
                {
                    return Failed(SystemRestorePointWorkflowStatus.ReplayBlocked, SystemRestorePointPhase.Confirmation,
                        "This restore-point creation plan was already claimed. Its durable claim must be inspected before any retry.");
                }
            }
            catch (OperationCanceledException)
            {
                return Failed(SystemRestorePointWorkflowStatus.TimedOut, SystemRestorePointPhase.Confirmation,
                    "The durable restore-point creation claim timed out before any platform write was allowed.");
            }
            catch (Exception ex)
            {
                return Failed(SystemRestorePointWorkflowStatus.StoreFailure, SystemRestorePointPhase.Confirmation,
                    $"The restore-point creation claim failed: {ex.Message}");
            }

            var receipt = new SystemRestorePointReceipt(
                receiptId,
                plan.PlanId,
                plan.PlanHash,
                _timeProvider.GetUtcNow(),
                null,
                plan.VolumeIdentity,
                plan.Description,
                plan.Source,
                plan.BaselineId,
                SystemRestorePointReceiptState.Executing,
                SystemRestorePointMutationOutcome.Unknown,
                null,
                false,
                false,
                MutationState: SystemRestorePointMutationState.Unknown,
                ClaimId: claim.ClaimId);
            var executingPersistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
            if (executingPersistError is not null)
            {
                var preWriteClaimError = await TryPersistClaimIndependentAsync(claim with
                {
                    UpdatedAtUtc = _timeProvider.GetUtcNow(),
                    State = SystemRestorePointCreateClaimState.Failed,
                    MutationState = SystemRestorePointMutationState.NotChanged,
                    FailureCode = "SYSTEM_RESTORE.CREATE.RECEIPT_NOT_DURABLE",
                    Message = executingPersistError.Message
                }).ConfigureAwait(false);
                return Failed(SystemRestorePointWorkflowStatus.StoreFailure,
                    SystemRestorePointPhase.PersistExecutingReceipt,
                    preWriteClaimError is null
                        ? $"The executing receipt could not be persisted; the claim was closed before any platform write: {executingPersistError.Message}"
                        : $"The executing receipt and the pre-write claim terminal state could not be persisted. No platform write was attempted: {executingPersistError.Message}; {preWriteClaimError.Message}",
                    manualInterventionRequired: preWriteClaimError is not null);
            }

            var callerCancelled = 0;
            using var registration = cancellationToken.Register(() => Interlocked.Exchange(ref callerCancelled, 1));
            using var executionCts = new CancellationTokenSource(_timing.ExecutionDeadline);
            SystemRestorePointMutationResult mutation;
            try
            {
                mutation = await _platform.CreateRestorePointAsync(
                    new SystemRestorePointCreateCommand(
                        plan.PlanId,
                        plan.PlanHash,
                        plan.VolumeIdentity,
                        plan.Description),
                    executionCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                mutation = new SystemRestorePointMutationResult(
                    SystemRestorePointMutationOutcome.TimedOut,
                    null,
                    null,
                    "The isolated restore-point boundary reached its deadline; mutation completion is unknown.",
                    SystemRestorePointMutationState.Unknown);
            }
            catch (Exception ex)
            {
                mutation = new SystemRestorePointMutationResult(
                    SystemRestorePointMutationOutcome.Unknown,
                    null,
                    null,
                    $"The isolated restore-point boundary failed after invocation: {ex.Message}",
                    SystemRestorePointMutationState.Unknown);
            }

            var readBack = await TryReadBackIndependentAsync().ConfigureAwait(false);
            var createdIdentity = readBack.Probe is null
                ? null
                : FindVerifiedNewIdentity(plan, readBack.Probe, mutation.SequenceNumber);

            var cancellationRecorded = Volatile.Read(ref callerCancelled) != 0;
            if (mutation.Outcome != SystemRestorePointMutationOutcome.Succeeded)
            {
                var mutationState = createdIdentity is not null
                    ? SystemRestorePointMutationState.Changed
                    : mutation.MutationState;
                var uncertain = mutationState != SystemRestorePointMutationState.NotChanged;
                receipt = receipt with
                {
                    CompletedAtUtc = _timeProvider.GetUtcNow(),
                    State = uncertain ? SystemRestorePointReceiptState.Unknown : SystemRestorePointReceiptState.Failed,
                    MutationOutcome = mutation.Outcome,
                    MutationState = mutationState,
                    CreatedIdentity = createdIdentity,
                    ReadBackVerified = false,
                    CallerCancellationRequested = cancellationRecorded,
                    FailureCode = uncertain ? "SYSTEM_RESTORE.CREATE.UNKNOWN" : "SYSTEM_RESTORE.CREATE.FAILED",
                    Message = mutation.Message ?? readBack.Error?.Message
                };
                return await FinishFailureAsync(
                    receipt,
                    claim,
                    uncertain ? SystemRestorePointWorkflowStatus.Unknown : MapMutationFailure(mutation.Outcome),
                    uncertain ? "Restore-point creation may have changed the system but could not be verified. Inspect Windows System Protection manually."
                        : mutation.Message ?? "Restore-point creation failed.",
                    uncertain).ConfigureAwait(false);
            }

            if (readBack.Probe is null || createdIdentity is null)
            {
                receipt = receipt with
                {
                    CompletedAtUtc = _timeProvider.GetUtcNow(),
                    State = SystemRestorePointReceiptState.Unknown,
                    MutationOutcome = mutation.Outcome,
                    MutationState = SystemRestorePointMutationState.Unknown,
                    CreatedIdentity = null,
                    ReadBackVerified = false,
                    CallerCancellationRequested = cancellationRecorded,
                    FailureCode = "SYSTEM_RESTORE.CREATE.READBACK_MISMATCH",
                    Message = readBack.Error?.Message ?? "The typed API returned success, but no new matching restore-point identity was read back."
                };
                return await FinishFailureAsync(
                    receipt,
                    claim,
                    SystemRestorePointWorkflowStatus.ReadBackMismatch,
                    receipt.Message!,
                    manualInterventionRequired: true).ConfigureAwait(false);
            }

            receipt = receipt with
            {
                CompletedAtUtc = _timeProvider.GetUtcNow(),
                State = SystemRestorePointReceiptState.Verified,
                MutationOutcome = mutation.Outcome,
                MutationState = SystemRestorePointMutationState.Changed,
                CreatedIdentity = createdIdentity,
                ReadBackVerified = true,
                CallerCancellationRequested = cancellationRecorded,
                Message = cancellationRecorded
                    ? "The restore point was created and independently verified; caller cancellation arrived after the write began."
                    : "The restore point was created and independently verified."
            };
            var terminalPersistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
            var terminalClaimError = await TryPersistClaimIndependentAsync(claim with
            {
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                State = terminalPersistError is null
                    ? SystemRestorePointCreateClaimState.Verified
                    : SystemRestorePointCreateClaimState.Unknown,
                MutationState = SystemRestorePointMutationState.Changed,
                FailureCode = terminalPersistError is null
                    ? null
                    : "SYSTEM_RESTORE.CREATE.TERMINAL_RECEIPT_NOT_DURABLE",
                Message = terminalPersistError is null ? receipt.Message : terminalPersistError.Message
            }).ConfigureAwait(false);
            if (terminalPersistError is not null || terminalClaimError is not null)
            {
                return new SystemRestorePointWorkflowResult(
                    false,
                    true,
                    SystemRestorePointWorkflowStatus.ManualInterventionRequired,
                    SystemRestorePointPhase.PersistTerminalReceipt,
                    terminalPersistError is not null
                        ? $"The restore point was verified, but the terminal receipt could not be persisted: {terminalPersistError.Message}"
                        : $"The restore point and terminal receipt were verified, but the durable claim could not be closed: {terminalClaimError!.Message}",
                    ManualInterventionRequired: true,
                    Receipt: receipt);
            }

            return new SystemRestorePointWorkflowResult(
                true,
                true,
                SystemRestorePointWorkflowStatus.Succeeded,
                SystemRestorePointPhase.Completed,
                receipt.Message!,
                Receipt: receipt);
        }
    }

    public async Task<SystemRestorePointWorkflowResult> PrepareSystemRestoreAsync(
        SystemRestorePointIdentity target,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(source)
            || target.SequenceNumber <= 0
            || string.IsNullOrWhiteSpace(target.VolumeIdentity))
        {
            return Failed(SystemRestorePointWorkflowStatus.InvalidRequest, SystemRestorePointPhase.Prepare,
                "A complete restore-point identity and source are required.",
                manualInterventionRequired: true,
                requiresReboot: true);
        }

        SystemRestorePointProbe probe;
        try
        {
            probe = await _platform.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Prepare,
                "System restore preparation was cancelled.",
                manualInterventionRequired: true,
                requiresReboot: true);
        }
        catch (Exception ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.Unknown, SystemRestorePointPhase.Prepare,
                $"System restore preflight failed: {ex.Message}",
                manualInterventionRequired: true,
                requiresReboot: true);
        }

        var commonFailure = ValidateCommonWritePreflight(probe);
        if (commonFailure is not null)
        {
            return commonFailure with { ManualInterventionRequired = true, RequiresReboot = true };
        }

        if (probe.HasPendingRestart)
        {
            return Failed(SystemRestorePointWorkflowStatus.RebootRequired, SystemRestorePointPhase.Prepare,
                "A pending restart was detected. Automated system restore remains blocked.",
                manualInterventionRequired: true,
                requiresReboot: true);
        }

        if (!ContainsExactIdentity(probe.RestorePoints, target))
        {
            return Failed(SystemRestorePointWorkflowStatus.IdentityDrifted, SystemRestorePointPhase.Prepare,
                "The selected restore-point identity no longer exists on the same system volume.",
                manualInterventionRequired: true,
                requiresReboot: true);
        }

        var now = _timeProvider.GetUtcNow();
        var plan = new SystemRestoreExecutionPlan(
            Guid.NewGuid(),
            string.Empty,
            now,
            now.Add(_timing.PlanLifetime),
            target,
            source.Trim(),
            RequiresAdministrator: true,
            RequiresExplicitConfirmation: true,
            RequiresReboot: true,
            ProductionExecutionAllowed: false);
        plan = plan with { PlanHash = ComputeHash(plan) };
        try
        {
            await _store.SaveRestorePlanAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failed(SystemRestorePointWorkflowStatus.Cancelled, SystemRestorePointPhase.Prepare,
                "Saving the frozen system-restore plan was cancelled.",
                manualInterventionRequired: true,
                requiresReboot: true);
        }
        catch (Exception ex)
        {
            return Failed(SystemRestorePointWorkflowStatus.StoreFailure, SystemRestorePointPhase.Prepare,
                $"The frozen system-restore plan could not be persisted: {ex.Message}",
                manualInterventionRequired: true,
                requiresReboot: true);
        }

        return new SystemRestorePointWorkflowResult(
            false,
            false,
            SystemRestorePointWorkflowStatus.PolicyDenied,
            SystemRestorePointPhase.Prepare,
            "The target identity and plan hash were frozen, but production system-restore execution is permanently fail-closed. Use Windows recovery in an isolated, supervised environment.",
            ManualInterventionRequired: true,
            RequiresReboot: true,
            Probe: probe,
            RestorePlan: plan);
    }

    public Task<SystemRestorePointWorkflowResult> ExecuteSystemRestoreAsync(
        SystemRestoreExecutionPlan plan,
        SystemRestorePointConfirmationToken? confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var hashValid = plan.OwnerId == SystemRestorePointIdentityConstants.OwnerId
            && plan.Scope == SystemRestorePointIdentityConstants.Scope
            && plan.ResourceKey == SystemRestorePointIdentityConstants.ResourceKey
            && !plan.ProductionExecutionAllowed
            && plan.RequiresReboot
            && FixedEquals(plan.PlanHash, ComputeHash(plan with { PlanHash = string.Empty }));
        if (!hashValid)
        {
            return Task.FromResult(Failed(
                SystemRestorePointWorkflowStatus.InvalidRequest,
                SystemRestorePointPhase.Confirmation,
                "The system-restore plan identity, policy, or hash was modified.",
                manualInterventionRequired: true,
                requiresReboot: true));
        }

        return Task.FromResult(Failed(
            SystemRestorePointWorkflowStatus.PolicyDenied,
            SystemRestorePointPhase.Execute,
            "Production system-restore execution is permanently blocked. No platform restore writer exists and no RecoveryId was issued.",
            manualInterventionRequired: true,
            requiresReboot: true));
    }

    internal static string ComputeHash(SystemRestorePointCreatePlan plan)
    {
        var baseline = string.Join(",", plan.BaselineRestorePoints
            .OrderBy(point => point.VolumeIdentity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.SequenceNumber)
            .Select(CanonicalIdentity));
        var canonical = string.Join('|',
            plan.PlanId.ToString("D"),
            plan.PreparedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            plan.ExpiresAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            plan.Description,
            plan.Source,
            plan.BaselineId ?? "-",
            plan.VolumeIdentity,
            baseline,
            plan.CreationCooldown.Ticks.ToString(CultureInfo.InvariantCulture),
            plan.RequiresAdministrator,
            plan.RequiresExplicitConfirmation,
            plan.OwnerId,
            plan.Scope,
            plan.ResourceKey);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string ComputeHash(SystemRestoreExecutionPlan plan)
    {
        var canonical = string.Join('|',
            plan.PlanId.ToString("D"),
            plan.PreparedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            plan.ExpiresAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            CanonicalIdentity(plan.Target),
            plan.Source,
            plan.RequiresAdministrator,
            plan.RequiresExplicitConfirmation,
            plan.RequiresReboot,
            plan.ProductionExecutionAllowed,
            plan.OwnerId,
            plan.Scope,
            plan.ResourceKey);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string CanonicalIdentity(SystemRestorePointIdentity point) => string.Join('~',
        Convert.ToBase64String(Encoding.UTF8.GetBytes(point.VolumeIdentity.ToUpperInvariant())),
        point.SequenceNumber.ToString(CultureInfo.InvariantCulture),
        point.CreatedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
        Convert.ToBase64String(Encoding.UTF8.GetBytes(point.Description)),
        point.RestorePointType.ToString(CultureInfo.InvariantCulture));

    private static SystemRestorePointWorkflowResult FailedOrReadyForRead(SystemRestorePointProbe probe)
    {
        if (!probe.IsWindows || probe.WindowsBuild < SystemRestorePointIdentityConstants.MinimumSupportedBuild)
        {
            return Failed(SystemRestorePointWorkflowStatus.Unsupported, SystemRestorePointPhase.Inspect,
                probe.Message ?? "Windows build 19041 or newer is required.") with { Probe = probe };
        }

        if (probe.ProtectionState is SystemProtectionState.Unknown or SystemProtectionState.Unsupported)
        {
            return Failed(SystemRestorePointWorkflowStatus.Unknown, SystemRestorePointPhase.Inspect,
                probe.Message ?? "System Protection state could not be established authoritatively.") with { Probe = probe };
        }

        if (!string.IsNullOrWhiteSpace(probe.ErrorCode))
        {
            return Failed(SystemRestorePointWorkflowStatus.Unknown, SystemRestorePointPhase.Inspect,
                probe.Message ?? "System Restore state could not be read completely.") with { Probe = probe };
        }

        return new SystemRestorePointWorkflowResult(
            true,
            false,
            SystemRestorePointWorkflowStatus.Ready,
            SystemRestorePointPhase.Inspect,
            probe.Message ?? "System Restore state was read successfully.",
            Probe: probe);
    }

    private static SystemRestorePointWorkflowResult? ValidateCreatePreflight(
        SystemRestorePointProbe probe,
        DateTimeOffset nowUtc)
    {
        var capability = SystemRestorePointCapabilities.EvaluateCreate(probe, nowUtc);
        return capability.IsAvailable
            ? null
            : Failed(capability.Status, SystemRestorePointPhase.Prepare, capability.Reason);
    }

    private static SystemRestorePointWorkflowResult? ValidateCommonWritePreflight(SystemRestorePointProbe probe)
    {
        if (!probe.IsWindows || probe.WindowsBuild < SystemRestorePointIdentityConstants.MinimumSupportedBuild)
        {
            return Failed(SystemRestorePointWorkflowStatus.Unsupported, SystemRestorePointPhase.Prepare,
                probe.Message ?? "Windows build 19041 or newer is required.");
        }

        if (!probe.IsAdministrator)
        {
            return Failed(SystemRestorePointWorkflowStatus.PermissionDenied, SystemRestorePointPhase.Prepare,
                "Administrator permission is required.");
        }

        if (probe.ProtectionState == SystemProtectionState.PolicyDisabled)
        {
            return Failed(SystemRestorePointWorkflowStatus.PolicyDenied, SystemRestorePointPhase.Prepare,
                probe.Message ?? "System Protection is disabled by policy.");
        }

        if (probe.ProtectionState == SystemProtectionState.Disabled)
        {
            return Failed(SystemRestorePointWorkflowStatus.ProtectionDisabled, SystemRestorePointPhase.Prepare,
                probe.Message ?? "System Protection is disabled for the system volume.");
        }

        if (probe.ProtectionState != SystemProtectionState.Enabled
            || string.IsNullOrWhiteSpace(probe.SystemVolumeIdentity))
        {
            return Failed(SystemRestorePointWorkflowStatus.Unknown, SystemRestorePointPhase.Prepare,
                probe.Message ?? "System Protection or system-volume identity could not be verified.");
        }

        return null;
    }

    private static SystemRestorePointWorkflowResult? ValidateCreatePlan(SystemRestorePointCreatePlan plan)
    {
        return plan.OwnerId != SystemRestorePointIdentityConstants.OwnerId
            || plan.Scope != SystemRestorePointIdentityConstants.Scope
            || plan.ResourceKey != SystemRestorePointIdentityConstants.ResourceKey
            || !plan.RequiresAdministrator
            || !plan.RequiresExplicitConfirmation
            || plan.PlanId == Guid.Empty
            || string.IsNullOrWhiteSpace(plan.VolumeIdentity)
            || NormalizeDescription(plan.Description) is null
            ? Failed(SystemRestorePointWorkflowStatus.InvalidRequest, SystemRestorePointPhase.Confirmation,
                "The prepared restore-point plan owner, scope, resource, or input is invalid.")
            : null;
    }

    private static SystemRestorePointWorkflowResult? ValidateNoDrift(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointProbe current,
        DateTimeOffset nowUtc)
    {
        var preflight = ValidateCreatePreflight(current, nowUtc);
        if (preflight is not null)
        {
            return preflight with { Phase = SystemRestorePointPhase.DriftCheck };
        }

        if (!string.Equals(plan.VolumeIdentity, current.SystemVolumeIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(SystemRestorePointWorkflowStatus.IdentityDrifted, SystemRestorePointPhase.DriftCheck,
                "The system-volume identity changed after preparation.");
        }

        var baseline = plan.BaselineRestorePoints.Select(IdentityKey).Order(StringComparer.Ordinal).ToArray();
        var currentKeys = current.RestorePoints.Select(IdentityKey).Order(StringComparer.Ordinal).ToArray();
        if (!baseline.SequenceEqual(currentKeys, StringComparer.Ordinal))
        {
            return Failed(SystemRestorePointWorkflowStatus.IdentityDrifted, SystemRestorePointPhase.DriftCheck,
                "The restore-point set changed after preparation. A new plan is required.");
        }

        return null;
    }

    private static SystemRestorePointIdentity? FindVerifiedNewIdentity(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointProbe readBack,
        long? nativeSequenceNumber)
    {
        if (!string.Equals(plan.VolumeIdentity, readBack.SystemVolumeIdentity, StringComparison.OrdinalIgnoreCase)
            || readBack.ProtectionState != SystemProtectionState.Enabled)
        {
            return null;
        }

        var oldIdentities = plan.BaselineRestorePoints.Select(IdentityKey).ToHashSet(StringComparer.Ordinal);
        var candidates = readBack.RestorePoints.Where(point =>
            !oldIdentities.Contains(IdentityKey(point))
            && string.Equals(point.VolumeIdentity, plan.VolumeIdentity, StringComparison.OrdinalIgnoreCase)
            && string.Equals(point.Description, plan.Description, StringComparison.Ordinal)
            && point.CreatedAtUtc >= plan.PreparedAtUtc.AddMinutes(-1)
            && point.CreatedAtUtc <= _MaximumReadBackCreationTime(plan));
        if (nativeSequenceNumber.HasValue)
        {
            candidates = candidates.Where(point => point.SequenceNumber == nativeSequenceNumber.Value);
        }

        var matches = candidates.OrderByDescending(point => point.CreatedAtUtc).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static DateTimeOffset _MaximumReadBackCreationTime(SystemRestorePointCreatePlan plan) =>
        plan.ExpiresAtUtc.AddMinutes(2);

    private static bool ContainsExactIdentity(
        IEnumerable<SystemRestorePointIdentity> points,
        SystemRestorePointIdentity target) =>
        points.Any(point => string.Equals(IdentityKey(point), IdentityKey(target), StringComparison.Ordinal));

    private static string IdentityKey(SystemRestorePointIdentity point) => CanonicalIdentity(point);

    private async Task<(SystemRestorePointProbe? Probe, Exception? Error)> TryReadBackIndependentAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(_timing.IndependentReadBackTimeout);
            return (await _platform.ProbeAsync(cts.Token).ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private async Task<SystemRestorePointWorkflowResult> FinishFailureAsync(
        SystemRestorePointReceipt receipt,
        SystemRestorePointCreateClaim claim,
        SystemRestorePointWorkflowStatus status,
        string message,
        bool manualInterventionRequired)
    {
        var persistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
        var claimError = await TryPersistClaimIndependentAsync(claim with
        {
            UpdatedAtUtc = _timeProvider.GetUtcNow(),
            State = receipt.MutationState == SystemRestorePointMutationState.NotChanged
                ? SystemRestorePointCreateClaimState.Failed
                : SystemRestorePointCreateClaimState.Unknown,
            MutationState = receipt.MutationState,
            FailureCode = receipt.FailureCode,
            Message = receipt.Message
        }).ConfigureAwait(false);
        if (persistError is not null || claimError is not null)
        {
            return new SystemRestorePointWorkflowResult(
                false,
                ChangedFrom(receipt.MutationState),
                SystemRestorePointWorkflowStatus.StoreFailure,
                SystemRestorePointPhase.PersistTerminalReceipt,
                persistError is not null
                    ? $"{message} Terminal receipt persistence also failed: {persistError.Message}"
                    : $"{message} Durable claim terminal persistence also failed: {claimError!.Message}",
                ManualInterventionRequired: true,
                Receipt: receipt);
        }

        return new SystemRestorePointWorkflowResult(
            false,
            ChangedFrom(receipt.MutationState),
            status,
            SystemRestorePointPhase.Completed,
            message,
            ManualInterventionRequired: manualInterventionRequired,
            Receipt: receipt);
    }

    private async Task<Exception?> TryPersistReceiptIndependentAsync(SystemRestorePointReceipt receipt)
    {
        try
        {
            using var cts = new CancellationTokenSource(_timing.IndependentReadBackTimeout);
            await _store.SaveReceiptAsync(receipt, cts.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<Exception?> TryPersistClaimIndependentAsync(SystemRestorePointCreateClaim claim)
    {
        try
        {
            using var cts = new CancellationTokenSource(_timing.IndependentReadBackTimeout);
            await _store.SaveCreateClaimAsync(claim, cts.Token).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static bool? ChangedFrom(SystemRestorePointMutationState state) => state switch
    {
        SystemRestorePointMutationState.NotChanged => false,
        SystemRestorePointMutationState.Changed => true,
        _ => null
    };

    private static SystemRestorePointWorkflowStatus MapMutationFailure(SystemRestorePointMutationOutcome outcome) => outcome switch
    {
        SystemRestorePointMutationOutcome.TimedOut => SystemRestorePointWorkflowStatus.TimedOut,
        SystemRestorePointMutationOutcome.Cancelled => SystemRestorePointWorkflowStatus.Cancelled,
        SystemRestorePointMutationOutcome.Unknown => SystemRestorePointWorkflowStatus.Unknown,
        _ => SystemRestorePointWorkflowStatus.PlatformFailure
    };

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        return trimmed.Length <= MaximumDescriptionLength && !trimmed.Any(char.IsControl)
            ? trimmed
            : null;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool FixedEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static SystemRestorePointWorkflowResult Failed(
        SystemRestorePointWorkflowStatus status,
        SystemRestorePointPhase phase,
        string message,
        bool manualInterventionRequired = false,
        bool requiresReboot = false) =>
        new(false, false, status, phase, message, manualInterventionRequired, requiresReboot);
}
