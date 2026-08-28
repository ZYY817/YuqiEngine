using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GameOptimizer.Core.PowerManagement;

public sealed class PowerManagementOwner : IPowerManagementOwner
{
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(5);
    private const int MinimumSupportedBuild = 19041;

    private readonly IPowerPlatform _platform;
    private readonly IPowerBaselineGate _baselineGate;
    private readonly IPowerWorkflowStore _store;
    private readonly IPowerConfirmationAuthority _confirmation;
    private readonly TimeProvider _timeProvider;
    private readonly PowerManagementTimingOptions _timing;

    public PowerManagementOwner(IPowerPlatform platform, IPowerBaselineGate baselineGate, IPowerWorkflowStore store,
        IPowerConfirmationAuthority confirmation, TimeProvider? timeProvider = null, PowerManagementTimingOptions? timing = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _baselineGate = baselineGate ?? throw new ArgumentNullException(nameof(baselineGate));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timing = timing ?? PowerManagementTimingOptions.Default;
        if (_timing.ResourceLeaseTimeout <= TimeSpan.Zero || _timing.ExecutionDeadline <= TimeSpan.Zero || _timing.IndependentIoTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timing), "All Power workflow timeouts must be positive.");
    }

    public async Task<PowerState> InspectAsync(Guid? targetSchemeId, CancellationToken cancellationToken)
    {
        var environment = await _platform.GetEnvironmentAsync(cancellationToken).ConfigureAwait(false);
        EnsureEnvironmentReadable(environment);
        var schemes = await _platform.EnumerateSchemesAsync(cancellationToken).ConfigureAwait(false);
        var active = await _platform.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var target = targetSchemeId ?? active;
        if (!schemes.Any(item => item.Id == active) || !schemes.Any(item => item.Id == target))
            throw new InvalidOperationException("Power scheme enumeration did not contain the active or requested scheme.");
        var capability = await _platform.GetDisplayTimeoutCapabilityAsync(target, cancellationToken).ConfigureAwait(false);
        var values = await _platform.ReadDisplayTimeoutAsync(target, environment.HasBattery, cancellationToken).ConfigureAwait(false);
        return new PowerState(environment, schemes, active, target, capability, values);
    }

    public async Task<IReadOnlyList<PowerReceipt>> ListRecoverableReceiptsAsync(CancellationToken cancellationToken)
    {
        var receipts = await _store.ListReceiptsAsync(cancellationToken).ConfigureAwait(false);
        return receipts
            .Where(item => item.OwnerId == PowerManagementIdentity.OwnerId
                && item.Scope == PowerManagementIdentity.Scope
                && item.State == PowerReceiptState.Applied
                && item.ApplyVerified
                && !item.RestoreVerified)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(50)
            .ToArray();
    }

    public async Task<PowerWorkflowResult> PrepareAsync(PowerPrepareRequest request, CancellationToken cancellationToken)
    {
        PowerState state;
        try { state = await InspectAsync(request.TargetSchemeId, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.Inspect, "Power inspection was cancelled."); }
        catch (Exception ex) { return Failed(PowerWorkflowStatus.Unsupported, PowerExecutionPhase.Inspect, ex.Message); }
        var validation = ValidateForWrite(state, request);
        if (validation is not null) return validation;

        var now = _timeProvider.GetUtcNow();
        var plan = new PowerPreparedPlan(Guid.NewGuid(), string.Empty, now, now.Add(PlanLifetime), state.ActiveSchemeId,
            request.TargetSchemeId, state.DisplayTimeout, new PowerSettingValues(request.AcSeconds, request.DcSeconds),
            "Balanced: changes display idle behavior for the selected AC/DC power source.",
            "A zero or long timeout can increase energy use; a short timeout can interrupt viewing.",
            true, true, false, false, ResourceKey: PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource);
        plan = plan with { PlanHash = ComputeHash(plan) };
        try { await _store.SavePreparedAsync(plan, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.Prepare, "Saving the prepared power plan was cancelled."); }
        catch (Exception ex) { return Failed(PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.Prepare, $"Prepared plan could not be persisted: {ex.Message}"); }
        return new PowerWorkflowResult(true, false, PowerWorkflowStatus.Ready, PowerExecutionPhase.Prepare,
            "Power change is prepared and frozen. Explicit confirmation and the baseline risk decision are still required.", plan);
    }

    public async Task<PowerWorkflowResult> ExecuteAsync(PowerPreparedPlan plan, PowerConfirmationToken confirmation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmation);
        var identityFailure = ValidateFrozenPlan(plan);
        if (identityFailure is not null) return identityFailure;
        var now = _timeProvider.GetUtcNow();
        if (plan.ExpiresAtUtc <= now || !FixedEquals(plan.PlanHash, ComputeHash(plan with { PlanHash = string.Empty })))
            return Failed(PowerWorkflowStatus.Expired, PowerExecutionPhase.Confirmation, "Prepared plan expired or its frozen content was modified.");

        PowerPreparedPlan? persisted;
        try { persisted = await _store.GetPreparedAsync(plan.PlanId, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.Confirmation, "Loading the prepared plan was cancelled."); }
        catch (Exception ex) { return Failed(PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.Confirmation, ex.Message); }
        if (persisted is null || persisted != plan || !_confirmation.TryConsume(plan, confirmation, now))
            return Failed(PowerWorkflowStatus.ConfirmationRequired, PowerExecutionPhase.Confirmation,
                "Confirmation is missing, expired, forged, or does not match the persisted plan.");

        try
        {
            if (!await _store.TryClaimPlanAsync(plan.PlanId, plan.PlanHash, cancellationToken).ConfigureAwait(false))
                return Failed(PowerWorkflowStatus.ReplayBlocked, PowerExecutionPhase.Confirmation, "This prepared plan was already executed.");
        }
        catch (OperationCanceledException) { return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.Confirmation, "Claiming the prepared plan was cancelled."); }
        catch (Exception ex) { return Failed(PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.Confirmation, $"Prepared plan claim failed: {ex.Message}"); }

        PowerBaselineDecision baseline;
        try { baseline = await _baselineGate.EnsureBaselineAsync(plan, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.BaselineGate, "The baseline risk decision was cancelled."); }
        catch (Exception ex) { return Failed(PowerWorkflowStatus.BaselineBlocked, PowerExecutionPhase.BaselineGate, $"The baseline risk decision failed: {ex.Message}"); }
        if (!baseline.Allowed || string.IsNullOrWhiteSpace(baseline.BaselineId))
            return Failed(PowerWorkflowStatus.BaselineBlocked, PowerExecutionPhase.BaselineGate,
                baseline.Reason ?? "The baseline risk decision did not provide execution evidence.");

        IPowerResourceLease lease;
        try { lease = await _store.AcquireResourceLeaseAsync(plan.ResourceKey, _timing.ResourceLeaseTimeout, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.DriftCheck, "Waiting for the Power resource lease was cancelled."); }
        catch (Exception ex) { return Failed(PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.DriftCheck, $"Power resource lease unavailable: {ex.Message}"); }
        await using (lease.ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.DriftCheck, "Power execution was cancelled before the write phase started.");
            var drift = await CheckPreparedStateAsync(plan, cancellationToken).ConfigureAwait(false);
            if (drift is not null) return drift;

            var receipt = new PowerReceipt(Guid.NewGuid(), plan.PlanId, plan.PlanHash, now, plan.OriginalActiveSchemeId,
                plan.TargetSchemeId, plan.OriginalDisplayTimeout, plan.TargetDisplayTimeout, baseline.BaselineId!, false, false,
                State: PowerReceiptState.Executing, ResourceKey: plan.ResourceKey);
            var executingPersistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
            if (executingPersistError is not null)
                return Failed(PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.Apply,
                    $"Executing receipt could not be durably persisted; no Power write was started: {executingPersistError.Message}");

            var callerCancelled = 0;
            using var registration = cancellationToken.Register(() => Interlocked.Exchange(ref callerCancelled, 1));
            using var executionCts = new CancellationTokenSource(_timing.ExecutionDeadline);
            PowerMutationResult mutation;
            try
            {
                mutation = await _platform.ApplyDisplayTimeoutAsync(plan.TargetSchemeId, plan.TargetDisplayTimeout,
                    plan.TargetSchemeId, executionCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                mutation = new PowerMutationResult(PowerCommandOutcome.TimedOut, Array.Empty<PowerCommandResult>(), "Internal Power execution deadline expired.");
            }
            catch (Exception ex)
            {
                mutation = new PowerMutationResult(PowerCommandOutcome.Failed, Array.Empty<PowerCommandResult>(), $"Power apply could not start or complete: {ex.Message}");
            }

            if (mutation.Outcome != PowerCommandOutcome.Success)
            {
                var status = mutation.Outcome == PowerCommandOutcome.TimedOut ? PowerWorkflowStatus.TimedOut
                    : mutation.Outcome == PowerCommandOutcome.Cancelled ? PowerWorkflowStatus.Cancelled
                    : PowerWorkflowStatus.CommandFailed;
                return await RollbackAfterFailureUnderLeaseAsync(receipt, status, mutation.Detail ?? "Power command failed.",
                    mutation.Commands, Volatile.Read(ref callerCancelled) != 0).ConfigureAwait(false);
            }

            bool verified;
            try { verified = await VerifyAppliedIndependentAsync(plan).ConfigureAwait(false); }
            catch (Exception ex)
            {
                return await RollbackAfterFailureUnderLeaseAsync(receipt, PowerWorkflowStatus.ReadBackMismatch,
                    $"Authoritative read-back failed: {ex.Message}", mutation.Commands, Volatile.Read(ref callerCancelled) != 0).ConfigureAwait(false);
            }
            if (!verified)
                return await RollbackAfterFailureUnderLeaseAsync(receipt, PowerWorkflowStatus.ReadBackMismatch,
                    "Authoritative read-back did not match the frozen target.", mutation.Commands, Volatile.Read(ref callerCancelled) != 0).ConfigureAwait(false);

            receipt = receipt with { ApplyVerified = true, State = PowerReceiptState.Applied, CallerCancellationRequested = Volatile.Read(ref callerCancelled) != 0 };
            var terminalPersistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
            if (terminalPersistError is not null)
                return await RollbackAfterFailureUnderLeaseAsync(receipt with { ApplyVerified = false, State = PowerReceiptState.Executing },
                    PowerWorkflowStatus.StoreFailure, $"Applied state was verified but terminal receipt persistence failed: {terminalPersistError.Message}",
                    mutation.Commands, receipt.CallerCancellationRequested).ConfigureAwait(false);
            var cancellationNote = receipt.CallerCancellationRequested ? " Caller cancellation was recorded after execution started; terminal verification still completed." : string.Empty;
            return new PowerWorkflowResult(true, true, PowerWorkflowStatus.Ready, PowerExecutionPhase.Completed,
                "Display timeout values were applied and verified; recovery is available." + cancellationNote, Receipt: receipt, Commands: mutation.Commands);
        }
    }

    public async Task<PowerWorkflowResult> RestoreAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        PowerReceipt? receipt;
        try { receipt = await _store.GetReceiptAsync(receiptId, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.RecoveryDriftCheck, "Loading the recovery receipt was cancelled."); }
        catch (Exception ex) { return Failed(PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.RecoveryDriftCheck, ex.Message); }
        var receiptFailure = ValidateRecoverableReceipt(receipt);
        if (receiptFailure is not null) return receiptFailure;
        var confirmedReceipt = receipt!;

        IPowerResourceLease lease;
        try { lease = await _store.AcquireResourceLeaseAsync(confirmedReceipt.ResourceKey, _timing.ResourceLeaseTimeout, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.RecoveryDriftCheck, "Waiting for the Power recovery lease was cancelled."); }
        catch (Exception ex) { return Failed(PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.RecoveryDriftCheck, $"Power recovery lease unavailable: {ex.Message}"); }
        await using (lease.ConfigureAwait(false))
        {
            receipt = confirmedReceipt;
            if (cancellationToken.IsCancellationRequested)
                return Failed(PowerWorkflowStatus.Cancelled, PowerExecutionPhase.RecoveryDriftCheck, "Power recovery was cancelled before the write phase started.");
            using (var readCts = new CancellationTokenSource(_timing.IndependentIoTimeout))
            {
                var currentActive = await _platform.GetActiveSchemeAsync(readCts.Token).ConfigureAwait(false);
                var currentValues = await _platform.ReadDisplayTimeoutAsync(receipt.TargetSchemeId,
                    receipt.AppliedDisplayTimeout.DcSeconds.HasValue, readCts.Token).ConfigureAwait(false);
                if (currentActive != receipt.TargetSchemeId || currentValues != receipt.AppliedDisplayTimeout)
                    return Failed(PowerWorkflowStatus.DriftBlocked, PowerExecutionPhase.RecoveryDriftCheck,
                        "Power state changed after apply; recovery was blocked to avoid overwriting newer user or policy changes.");
            }
            if (!await _store.TryClaimRecoveryAsync(receiptId, cancellationToken).ConfigureAwait(false))
                return Failed(PowerWorkflowStatus.ReplayBlocked, PowerExecutionPhase.RecoveryDriftCheck, "This recovery receipt was already consumed.");

            receipt = receipt with { State = PowerReceiptState.Restoring };
            var restoringPersistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
            if (restoringPersistError is not null)
                return Failed(PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.Restore,
                    $"Restoring receipt could not be durably persisted; no recovery write was started: {restoringPersistError.Message}");
            var callerCancelled = 0;
            using var registration = cancellationToken.Register(() => Interlocked.Exchange(ref callerCancelled, 1));
            using var executionCts = new CancellationTokenSource(_timing.ExecutionDeadline);
            PowerMutationResult mutation;
            try
            {
                mutation = await _platform.ApplyDisplayTimeoutAsync(receipt.TargetSchemeId, receipt.OriginalDisplayTimeout,
                    receipt.OriginalActiveSchemeId, executionCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                mutation = new PowerMutationResult(PowerCommandOutcome.TimedOut, Array.Empty<PowerCommandResult>(), "Internal Power recovery deadline expired.");
            }
            catch (Exception ex)
            {
                mutation = new PowerMutationResult(PowerCommandOutcome.Failed, Array.Empty<PowerCommandResult>(), $"Power recovery could not start or complete: {ex.Message}");
            }
            if (mutation.Outcome != PowerCommandOutcome.Success)
            {
                receipt = receipt with { State = PowerReceiptState.RestoreFailed, FailureDetail = mutation.Detail, CallerCancellationRequested = Volatile.Read(ref callerCancelled) != 0 };
                var failedPersistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
                if (failedPersistError is not null)
                    return new PowerWorkflowResult(false, false, PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.Restore,
                        $"Recovery failed and terminal receipt persistence also failed: {failedPersistError.Message}", Receipt: receipt, Commands: mutation.Commands);
                return new PowerWorkflowResult(false, false, mutation.Outcome == PowerCommandOutcome.TimedOut ? PowerWorkflowStatus.TimedOut : PowerWorkflowStatus.CommandFailed,
                    PowerExecutionPhase.Restore, mutation.Detail ?? "Power recovery command failed.", Receipt: receipt, Commands: mutation.Commands);
            }

            bool verified;
            using (var readCts = new CancellationTokenSource(_timing.IndependentIoTimeout))
            {
                var active = await _platform.GetActiveSchemeAsync(readCts.Token).ConfigureAwait(false);
                var values = await _platform.ReadDisplayTimeoutAsync(receipt.TargetSchemeId,
                    receipt.OriginalDisplayTimeout.DcSeconds.HasValue, readCts.Token).ConfigureAwait(false);
                verified = active == receipt.OriginalActiveSchemeId && values == receipt.OriginalDisplayTimeout;
            }
            receipt = receipt with
            {
                RestoreVerified = verified,
                State = verified ? PowerReceiptState.Restored : PowerReceiptState.RestoreFailed,
                FailureDetail = verified ? null : "Recovery authoritative read-back mismatch.",
                CallerCancellationRequested = Volatile.Read(ref callerCancelled) != 0
            };
            var restoreTerminalPersistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
            if (restoreTerminalPersistError is not null)
                return new PowerWorkflowResult(false, verified, PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.Restore,
                    $"Recovery read-back completed but terminal receipt persistence failed: {restoreTerminalPersistError.Message}", Receipt: receipt, Commands: mutation.Commands);
            if (!verified)
                return new PowerWorkflowResult(false, true, PowerWorkflowStatus.ReadBackMismatch, PowerExecutionPhase.ReadBack,
                    receipt.FailureDetail!, Receipt: receipt, Commands: mutation.Commands);
            return new PowerWorkflowResult(true, true, PowerWorkflowStatus.Ready, PowerExecutionPhase.Completed,
                "Original display timeout and active power scheme were restored and verified." +
                (receipt.CallerCancellationRequested ? " Caller cancellation was recorded after recovery started; terminal verification still completed." : string.Empty),
                Receipt: receipt, Commands: mutation.Commands);
        }
    }

    private PowerWorkflowResult? ValidateForWrite(PowerState state, PowerPrepareRequest request)
    {
        var environment = state.Environment;
        if (!environment.IsWindows || environment.WindowsBuild < MinimumSupportedBuild) return Failed(PowerWorkflowStatus.Unsupported, PowerExecutionPhase.Prepare, "Windows build 19041 or newer is required.");
        if (!environment.IsAdministrator) return Failed(PowerWorkflowStatus.PermissionDenied, PowerExecutionPhase.Prepare, "Administrator permission is required for power writes.");
        if (environment.IsPolicyManaged || state.DisplayTimeoutCapability.IsPolicyManaged) return Failed(PowerWorkflowStatus.PolicyManaged, PowerExecutionPhase.Prepare, environment.BlockReason ?? state.DisplayTimeoutCapability.BlockReason ?? "The setting is policy managed.");
        if (environment.IsModernStandby) return Failed(PowerWorkflowStatus.Unsupported, PowerExecutionPhase.Prepare, "Modern Standby semantics are not proven for this setting; write is blocked.");
        if (!state.DisplayTimeoutCapability.IsAvailable) return Failed(PowerWorkflowStatus.Unsupported, PowerExecutionPhase.Prepare, state.DisplayTimeoutCapability.BlockReason ?? "Display timeout is unavailable.");
        if (request.AcSeconds > PowerManagementIdentity.MaximumDisplayTimeoutSeconds || request.AcSeconds < state.DisplayTimeoutCapability.MinimumSeconds || request.AcSeconds > state.DisplayTimeoutCapability.MaximumSeconds) return Failed(PowerWorkflowStatus.InvalidRequest, PowerExecutionPhase.Prepare, "AC timeout is outside the fixed supported range.");
        if (request.DcSeconds.HasValue && !environment.HasBattery) return Failed(PowerWorkflowStatus.InvalidRequest, PowerExecutionPhase.Prepare, "DC timeout is not applicable because no battery was detected.");
        if (environment.HasBattery && !request.DcSeconds.HasValue) return Failed(PowerWorkflowStatus.InvalidRequest, PowerExecutionPhase.Prepare, "Both AC and DC values are required on battery-capable systems.");
        if (request.DcSeconds is > PowerManagementIdentity.MaximumDisplayTimeoutSeconds || request.DcSeconds < state.DisplayTimeoutCapability.MinimumSeconds || request.DcSeconds > state.DisplayTimeoutCapability.MaximumSeconds) return Failed(PowerWorkflowStatus.InvalidRequest, PowerExecutionPhase.Prepare, "DC timeout is outside the fixed supported range.");
        return null;
    }

    private static PowerWorkflowResult? ValidateFrozenPlan(PowerPreparedPlan plan)
    {
        if (plan.OwnerId != PowerManagementIdentity.OwnerId || plan.Scope != PowerManagementIdentity.Scope
            || plan.ResourceKey != PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource
            || plan.TargetDisplayTimeout.AcSeconds > PowerManagementIdentity.MaximumDisplayTimeoutSeconds
            || plan.TargetDisplayTimeout.DcSeconds > PowerManagementIdentity.MaximumDisplayTimeoutSeconds)
            return Failed(PowerWorkflowStatus.InvalidRequest, PowerExecutionPhase.Confirmation, "Prepared plan owner, scope, resource, or value range is invalid.");
        return null;
    }

    private static PowerWorkflowResult? ValidateRecoverableReceipt(PowerReceipt? receipt)
    {
        if (receipt?.State == PowerReceiptState.Restored || receipt?.RestoreVerified == true)
            return Failed(PowerWorkflowStatus.ReplayBlocked, PowerExecutionPhase.RecoveryDriftCheck, "This recovery receipt was already completed.");
        if (receipt is null || !receipt.ApplyVerified || receipt.State != PowerReceiptState.Applied)
            return Failed(PowerWorkflowStatus.RecoveryUnavailable, PowerExecutionPhase.RecoveryDriftCheck, "No applied and verified recovery receipt exists.");
        if (receipt.OwnerId != PowerManagementIdentity.OwnerId || receipt.Scope != PowerManagementIdentity.Scope
            || receipt.ResourceKey != PowerManagementIdentity.DisplayTimeoutAndActiveSchemeResource
            || receipt.OriginalDisplayTimeout.AcSeconds > PowerManagementIdentity.MaximumDisplayTimeoutSeconds
            || receipt.OriginalDisplayTimeout.DcSeconds > PowerManagementIdentity.MaximumDisplayTimeoutSeconds)
            return Failed(PowerWorkflowStatus.RecoveryUnavailable, PowerExecutionPhase.RecoveryDriftCheck, "Recovery receipt owner, scope, resource, or range is invalid.");
        return null;
    }

    private async Task<PowerWorkflowResult?> CheckPreparedStateAsync(PowerPreparedPlan plan, CancellationToken cancellationToken)
    {
        var environment = await _platform.GetEnvironmentAsync(cancellationToken).ConfigureAwait(false);
        var active = await _platform.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        var values = await _platform.ReadDisplayTimeoutAsync(plan.TargetSchemeId, plan.OriginalDisplayTimeout.DcSeconds.HasValue, cancellationToken).ConfigureAwait(false);
        return !environment.IsAdministrator || environment.IsPolicyManaged || environment.IsModernStandby || active != plan.OriginalActiveSchemeId || values != plan.OriginalDisplayTimeout
            ? Failed(PowerWorkflowStatus.DriftBlocked, PowerExecutionPhase.DriftCheck, "Environment, active scheme, or setting values changed after preparation.") : null;
    }

    private async Task<bool> VerifyAppliedIndependentAsync(PowerPreparedPlan plan)
    {
        using var cts = new CancellationTokenSource(_timing.IndependentIoTimeout);
        var active = await _platform.GetActiveSchemeAsync(cts.Token).ConfigureAwait(false);
        var values = await _platform.ReadDisplayTimeoutAsync(plan.TargetSchemeId, plan.TargetDisplayTimeout.DcSeconds.HasValue, cts.Token).ConfigureAwait(false);
        return active == plan.TargetSchemeId && values == plan.TargetDisplayTimeout;
    }

    private async Task<PowerWorkflowResult> RollbackAfterFailureUnderLeaseAsync(PowerReceipt receipt, PowerWorkflowStatus status,
        string message, IReadOnlyList<PowerCommandResult> commands, bool callerCancellationRequested)
    {
        PowerMutationResult rollback;
        try
        {
            using var rollbackCts = new CancellationTokenSource(_timing.ExecutionDeadline);
            rollback = await _platform.ApplyDisplayTimeoutAsync(receipt.TargetSchemeId, receipt.OriginalDisplayTimeout,
                receipt.OriginalActiveSchemeId, rollbackCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { rollback = new PowerMutationResult(PowerCommandOutcome.Failed, Array.Empty<PowerCommandResult>(), ex.Message); }
        var rollbackVerified = false;
        if (rollback.Outcome == PowerCommandOutcome.Success)
        {
            try
            {
                using var readCts = new CancellationTokenSource(_timing.IndependentIoTimeout);
                rollbackVerified = await _platform.GetActiveSchemeAsync(readCts.Token).ConfigureAwait(false) == receipt.OriginalActiveSchemeId
                    && await _platform.ReadDisplayTimeoutAsync(receipt.TargetSchemeId, receipt.OriginalDisplayTimeout.DcSeconds.HasValue, readCts.Token).ConfigureAwait(false) == receipt.OriginalDisplayTimeout;
            }
            catch { rollbackVerified = false; }
        }
        receipt = receipt with { FailureDetail = message, RestoreVerified = rollbackVerified, State = PowerReceiptState.ApplyFailed, CallerCancellationRequested = callerCancellationRequested };
        var persistError = await TryPersistReceiptIndependentAsync(receipt).ConfigureAwait(false);
        if (persistError is not null)
            return new PowerWorkflowResult(false, false, PowerWorkflowStatus.StoreFailure, PowerExecutionPhase.Rollback,
                $"{message} Rollback verification={rollbackVerified}, but terminal receipt persistence failed: {persistError.Message}",
                Receipt: receipt, Commands: commands.Concat(rollback.Commands).ToArray());
        return new PowerWorkflowResult(false, false, status, PowerExecutionPhase.Rollback,
            rollbackVerified ? $"{message} Original power state was restored and verified." : $"{message} Automatic rollback could not be verified; inspect the durable terminal receipt.",
            Receipt: receipt, Commands: commands.Concat(rollback.Commands).ToArray());
    }

    private async Task PersistReceiptIndependentAsync(PowerReceipt receipt)
    {
        using var cts = new CancellationTokenSource(_timing.IndependentIoTimeout);
        await _store.SaveReceiptAsync(receipt, cts.Token).ConfigureAwait(false);
    }

    private async Task<Exception?> TryPersistReceiptIndependentAsync(PowerReceipt receipt)
    {
        try { await PersistReceiptIndependentAsync(receipt).ConfigureAwait(false); return null; }
        catch (Exception ex) { return ex; }
    }

    private static void EnsureEnvironmentReadable(PowerEnvironment environment)
    {
        if (!environment.IsWindows) throw new PlatformNotSupportedException(environment.BlockReason ?? "Power management is available only on Windows.");
        if (environment.WindowsBuild < MinimumSupportedBuild) throw new PlatformNotSupportedException("Windows build 19041 or newer is required.");
    }

    internal static string ComputeHash(PowerPreparedPlan plan)
    {
        var canonical = string.Join('|', plan.PlanId.ToString("D"), plan.PreparedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            plan.ExpiresAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture), plan.OriginalActiveSchemeId.ToString("D"),
            plan.TargetSchemeId.ToString("D"), plan.OriginalDisplayTimeout.AcSeconds.ToString(CultureInfo.InvariantCulture),
            plan.OriginalDisplayTimeout.DcSeconds?.ToString(CultureInfo.InvariantCulture) ?? "-",
            plan.TargetDisplayTimeout.AcSeconds.ToString(CultureInfo.InvariantCulture), plan.TargetDisplayTimeout.DcSeconds?.ToString(CultureInfo.InvariantCulture) ?? "-",
            plan.Risk, plan.Drawback, plan.RequiresAdministrator, plan.RequiresExplicitConfirmation, plan.RequiresRestart,
            plan.RequiresSignOut, plan.OwnerId, plan.Scope, plan.ResourceKey);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool FixedEquals(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch (FormatException) { return false; }
    }

    private static PowerWorkflowResult Failed(PowerWorkflowStatus status, PowerExecutionPhase phase, string message) => new(false, false, status, phase, message);
}
