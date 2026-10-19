namespace GameOptimizer.Core.SystemRestore;

public sealed partial class SystemRestorePointOwner
{
    public async Task<IReadOnlyList<SystemRestorePointWorkflowResult>> ReconcileInterruptedCreatesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SystemRestorePointCreateClaim> claims;
        try
        {
            claims = await _store.ListCreateClaimsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new[] { ReconciliationFailure(SystemRestorePointWorkflowStatus.Cancelled, "Startup reconciliation was cancelled.") };
        }
        catch (Exception ex)
        {
            return new[] { ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                $"Startup reconciliation could not authenticate the durable claim set: {ex.Message}") };
        }

        var results = new List<SystemRestorePointWorkflowResult>();
        foreach (var claim in claims.Where(item => item.State is
                     SystemRestorePointCreateClaimState.Executing or
                     SystemRestorePointCreateClaimState.Interrupted or
                     SystemRestorePointCreateClaimState.Unknown))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(ReconciliationFailure(SystemRestorePointWorkflowStatus.Cancelled,
                    "Startup reconciliation was cancelled before all executing claims were inspected."));
                break;
            }
            results.Add(await ReconcileInterruptedCreateAsync(claim.PlanId, cancellationToken).ConfigureAwait(false));
        }
        return results;
    }

    public async Task<SystemRestorePointWorkflowResult> ReconcileInterruptedCreateAsync(
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        if (planId == Guid.Empty)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.InvalidRequest, "A valid plan identity is required.");
        }

        SystemRestorePointCreatePlan? plan;
        SystemRestorePointCreateClaim? claim;
        try
        {
            plan = await _store.LoadCreatePlanAsync(planId, cancellationToken).ConfigureAwait(false);
            if (plan is null || !FixedEquals(plan.PlanHash, ComputeHash(plan with { PlanHash = string.Empty })))
            {
                return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                    "The interrupted plan is missing or its frozen hash is invalid.");
            }
            claim = await _store.LoadCreateClaimAsync(planId, plan.PlanHash, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.Cancelled, "Interrupted-create reconciliation was cancelled.");
        }
        catch (Exception ex)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                $"The interrupted plan or claim could not be authenticated: {ex.Message}");
        }

        if (claim is null)
        {
            return new SystemRestorePointWorkflowResult(
                false,
                false,
                SystemRestorePointWorkflowStatus.ReplayBlocked,
                SystemRestorePointPhase.Reconcile,
                "No durable create claim exists. Reconciliation performed no write.");
        }

        ISystemRestorePointResourceLease lease;
        try
        {
            lease = await _store.AcquireResourceLeaseAsync(plan.ResourceKey, _timing.ResourceLeaseTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.Cancelled, "Waiting for the reconciliation lease was cancelled.");
        }
        catch (TimeoutException ex)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.TimedOut, $"The reconciliation lease timed out: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure, $"The reconciliation lease is unavailable: {ex.Message}");
        }

        await using (lease.ConfigureAwait(false))
        {
            try
            {
                claim = await _store.LoadCreateClaimAsync(planId, plan.PlanHash, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The durable claim disappeared after the resource lease was acquired.");
            }
            catch (Exception ex)
            {
                return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                    $"The durable claim could not be re-authenticated under lease: {ex.Message}");
            }

            var receiptRead = await TryLoadReceiptForReconciliationAsync(claim.ReceiptId, cancellationToken).ConfigureAwait(false);
            if (receiptRead.Error is OperationCanceledException)
            {
                return ReconciliationFailure(SystemRestorePointWorkflowStatus.Cancelled,
                    "Interrupted-create reconciliation was cancelled before any reconciliation write.");
            }
            if (claim.State != SystemRestorePointCreateClaimState.Executing)
            {
                return ExistingTerminalResult(claim, receiptRead.Receipt, receiptRead.Error);
            }

            if (receiptRead.Error is null && receiptRead.Receipt is { State: not SystemRestorePointReceiptState.Executing } terminal)
            {
                return await CloseClaimFromTerminalReceiptAsync(claim, terminal).ConfigureAwait(false);
            }

            var readBack = await TryReadBackIndependentAsync().ConfigureAwait(false);
            var createdIdentity = readBack.Probe is null ? null : FindVerifiedNewIdentity(plan, readBack.Probe, null);
            var now = _timeProvider.GetUtcNow();
            var terminalReceipt = createdIdentity is not null
                ? CreateVerifiedReconciliationReceipt(plan, claim, receiptRead.Receipt, createdIdentity, now)
                : CreateInterruptedReceipt(plan, claim, receiptRead.Receipt, now,
                    readBack.Error?.Message ?? "No unique matching restore point could be proved after restart.");

            Exception? receiptPersistError;
            if (receiptRead.Error is InvalidDataException || receiptRead.Receipt is null)
            {
                try
                {
                    using var cts = new CancellationTokenSource(_timing.IndependentReadBackTimeout);
                    _ = await _store.RecoverReceiptAsync(terminalReceipt, cts.Token).ConfigureAwait(false);
                    receiptPersistError = null;
                }
                catch (Exception ex)
                {
                    receiptPersistError = ex;
                }
            }
            else
            {
                receiptPersistError = await TryPersistReceiptIndependentAsync(terminalReceipt).ConfigureAwait(false);
            }

            if (receiptPersistError is not null)
            {
                return new SystemRestorePointWorkflowResult(
                    false,
                    createdIdentity is null ? null : true,
                    SystemRestorePointWorkflowStatus.StoreFailure,
                    SystemRestorePointPhase.Reconcile,
                    $"Reconciliation readback completed, but its terminal receipt is not durable: {receiptPersistError.Message}",
                    ManualInterventionRequired: true,
                    Probe: readBack.Probe,
                    Receipt: terminalReceipt);
            }

            var terminalClaim = claim with
            {
                UpdatedAtUtc = now,
                State = createdIdentity is not null
                    ? SystemRestorePointCreateClaimState.Verified
                    : SystemRestorePointCreateClaimState.Interrupted,
                MutationState = createdIdentity is not null
                    ? SystemRestorePointMutationState.Changed
                    : SystemRestorePointMutationState.Unknown,
                FailureCode = createdIdentity is not null ? null : "SYSTEM_RESTORE.CREATE.INTERRUPTED",
                Message = terminalReceipt.Message
            };
            var claimPersistError = await TryPersistClaimIndependentAsync(terminalClaim).ConfigureAwait(false);
            if (claimPersistError is not null)
            {
                return new SystemRestorePointWorkflowResult(
                    false,
                    createdIdentity is null ? null : true,
                    SystemRestorePointWorkflowStatus.StoreFailure,
                    SystemRestorePointPhase.Reconcile,
                    $"The reconciliation receipt is durable, but the claim could not be closed: {claimPersistError.Message}",
                    ManualInterventionRequired: true,
                    Probe: readBack.Probe,
                    Receipt: terminalReceipt);
            }

            return new SystemRestorePointWorkflowResult(
                createdIdentity is not null,
                createdIdentity is null ? null : true,
                createdIdentity is not null
                    ? SystemRestorePointWorkflowStatus.Succeeded
                    : SystemRestorePointWorkflowStatus.ManualInterventionRequired,
                SystemRestorePointPhase.Reconcile,
                terminalReceipt.Message ?? "Interrupted-create reconciliation completed.",
                ManualInterventionRequired: createdIdentity is null,
                Probe: readBack.Probe,
                Receipt: terminalReceipt);
        }
    }

    public async Task<SystemRestorePointWorkflowResult> ResolveInterruptedCreateAsync(
        SystemRestorePointManualResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        if (resolution.PlanId == Guid.Empty || resolution.ClaimId == Guid.Empty || string.IsNullOrWhiteSpace(resolution.Evidence))
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.InvalidRequest,
                "Manual resolution requires plan/claim identities and non-empty evidence.");
        }

        var current = await ReconcileInterruptedCreateAsync(resolution.PlanId, cancellationToken).ConfigureAwait(false);
        if (current.Success || current.Changed == false)
        {
            return current;
        }
        if (resolution.Disposition == SystemRestorePointManualResolutionDisposition.AcknowledgeUnknown)
        {
            return current with
            {
                Success = false,
                Changed = null,
                Status = SystemRestorePointWorkflowStatus.ManualInterventionRequired,
                Phase = SystemRestorePointPhase.Reconcile,
                Message = "The interrupted mutation remains Unknown. Manual acknowledgement does not assert that the system was unchanged.",
                ManualInterventionRequired = true
            };
        }

        if (resolution.VerifiedIdentity is null)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.InvalidRequest,
                "ConfirmVerifiedChanged requires an exact typed restore-point identity.");
        }

        SystemRestorePointCreatePlan plan;
        SystemRestorePointCreateClaim claim;
        SystemRestorePointReceipt receipt;
        try
        {
            plan = await _store.LoadCreatePlanAsync(resolution.PlanId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The manual-resolution plan is missing.");
            claim = await _store.LoadCreateClaimAsync(plan.PlanId, plan.PlanHash, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The manual-resolution claim is missing.");
            if (claim.ClaimId != resolution.ClaimId
                || claim.State is not (SystemRestorePointCreateClaimState.Interrupted or SystemRestorePointCreateClaimState.Unknown))
            {
                throw new InvalidDataException("The claim identity or state is not eligible for manual resolution.");
            }
            receipt = await _store.LoadReceiptAsync(claim.ReceiptId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The manual-resolution receipt is missing.");
        }
        catch (Exception ex)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                $"Manual resolution could not authenticate its durable evidence: {ex.Message}");
        }

        ISystemRestorePointResourceLease lease;
        try
        {
            lease = await _store.AcquireResourceLeaseAsync(plan.ResourceKey, _timing.ResourceLeaseTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                $"Manual resolution could not acquire the System Restore lease: {ex.Message}");
        }

        await using (lease.ConfigureAwait(false))
        {
            var readBack = await TryReadBackIndependentAsync().ConfigureAwait(false);
            var proved = readBack.Probe is not null
                && ContainsExactIdentity(readBack.Probe.RestorePoints, resolution.VerifiedIdentity)
                && FindVerifiedNewIdentity(plan, readBack.Probe, resolution.VerifiedIdentity.SequenceNumber) is { } candidate
                && candidate == resolution.VerifiedIdentity;
            if (!proved)
            {
                return new SystemRestorePointWorkflowResult(
                    false,
                    null,
                    SystemRestorePointWorkflowStatus.ReadBackMismatch,
                    SystemRestorePointPhase.Reconcile,
                    "Manual evidence did not match one unique authoritative restore-point identity; the mutation remains Unknown.",
                    ManualInterventionRequired: true,
                    Probe: readBack.Probe,
                    Receipt: receipt);
            }

            var now = _timeProvider.GetUtcNow();
            var verifiedReceipt = receipt with
            {
                CompletedAtUtc = now,
                State = SystemRestorePointReceiptState.Verified,
                MutationOutcome = SystemRestorePointMutationOutcome.Succeeded,
                MutationState = SystemRestorePointMutationState.Changed,
                CreatedIdentity = resolution.VerifiedIdentity,
                ReadBackVerified = true,
                FailureCode = null,
                Message = $"Interrupted creation was manually adjudicated against authoritative readback. Evidence SHA-256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(resolution.Evidence.Trim())))}"
            };
            var receiptError = await TryPersistReceiptIndependentAsync(verifiedReceipt).ConfigureAwait(false);
            var claimError = receiptError is null
                ? await TryPersistClaimIndependentAsync(claim with
                {
                    UpdatedAtUtc = now,
                    State = SystemRestorePointCreateClaimState.Verified,
                    MutationState = SystemRestorePointMutationState.Changed,
                    FailureCode = null,
                    Message = verifiedReceipt.Message
                }).ConfigureAwait(false)
                : null;
            if (receiptError is not null || claimError is not null)
            {
                return new SystemRestorePointWorkflowResult(
                    false,
                    true,
                    SystemRestorePointWorkflowStatus.StoreFailure,
                    SystemRestorePointPhase.Reconcile,
                    $"Manual verification succeeded, but durable closure failed: {(receiptError ?? claimError)!.Message}",
                    ManualInterventionRequired: true,
                    Probe: readBack.Probe,
                    Receipt: verifiedReceipt);
            }

            return new SystemRestorePointWorkflowResult(
                true,
                true,
                SystemRestorePointWorkflowStatus.Succeeded,
                SystemRestorePointPhase.Reconcile,
                verifiedReceipt.Message!,
                Probe: readBack.Probe,
                Receipt: verifiedReceipt);
        }
    }

    private async Task<SystemRestorePointWorkflowResult> CloseClaimFromTerminalReceiptAsync(
        SystemRestorePointCreateClaim claim,
        SystemRestorePointReceipt receipt)
    {
        if (receipt.PlanId != claim.PlanId || receipt.ClaimId != claim.ClaimId || !FixedEquals(receipt.PlanHash, claim.PlanHash))
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                "The terminal receipt does not match the authenticated claim.");
        }
        var targetState = receipt.State switch
        {
            SystemRestorePointReceiptState.Verified => SystemRestorePointCreateClaimState.Verified,
            SystemRestorePointReceiptState.Failed => SystemRestorePointCreateClaimState.Failed,
            _ => SystemRestorePointCreateClaimState.Interrupted
        };
        var mutationState = receipt.State switch
        {
            SystemRestorePointReceiptState.Verified => SystemRestorePointMutationState.Changed,
            SystemRestorePointReceiptState.Failed => SystemRestorePointMutationState.NotChanged,
            _ => SystemRestorePointMutationState.Unknown
        };
        var error = await TryPersistClaimIndependentAsync(claim with
        {
            UpdatedAtUtc = _timeProvider.GetUtcNow(),
            State = targetState,
            MutationState = mutationState,
            FailureCode = receipt.FailureCode,
            Message = receipt.Message
        }).ConfigureAwait(false);
        if (error is not null)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                $"The terminal receipt is durable, but the executing claim could not be closed: {error.Message}");
        }
        return ExistingTerminalResult(claim with { State = targetState, MutationState = mutationState }, receipt, null);
    }

    private static SystemRestorePointWorkflowResult ExistingTerminalResult(
        SystemRestorePointCreateClaim claim,
        SystemRestorePointReceipt? receipt,
        Exception? receiptError)
    {
        if (receiptError is not null || receipt is null)
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                $"The terminal claim has no authenticated matching receipt: {receiptError?.Message ?? "receipt missing"}");
        }
        var success = claim.State == SystemRestorePointCreateClaimState.Verified
            && receipt.State == SystemRestorePointReceiptState.Verified;
        var stateMatches = success
            || claim.State == SystemRestorePointCreateClaimState.Failed
                && receipt.State == SystemRestorePointReceiptState.Failed
            || claim.State is SystemRestorePointCreateClaimState.Interrupted or SystemRestorePointCreateClaimState.Unknown
                && receipt.State == SystemRestorePointReceiptState.Unknown;
        if (!stateMatches
            || receipt.PlanId != claim.PlanId
            || receipt.ClaimId != claim.ClaimId
            || !FixedEquals(receipt.PlanHash, claim.PlanHash))
        {
            return ReconciliationFailure(SystemRestorePointWorkflowStatus.StoreFailure,
                "The durable terminal claim and receipt are contradictory; automatic reconciliation is blocked.");
        }
        bool? changed = success ? true : claim.State == SystemRestorePointCreateClaimState.Failed ? false : null;
        return new SystemRestorePointWorkflowResult(
            success,
            changed,
            success ? SystemRestorePointWorkflowStatus.Succeeded : SystemRestorePointWorkflowStatus.ManualInterventionRequired,
            SystemRestorePointPhase.Reconcile,
            receipt.Message ?? "The durable terminal claim was inspected.",
            ManualInterventionRequired: !success && changed is null,
            Receipt: receipt);
    }

    private async Task<(SystemRestorePointReceipt? Receipt, Exception? Error)> TryLoadReceiptForReconciliationAsync(
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await _store.LoadReceiptAsync(receiptId, cancellationToken).ConfigureAwait(false), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static SystemRestorePointReceipt CreateVerifiedReconciliationReceipt(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointCreateClaim claim,
        SystemRestorePointReceipt? existing,
        SystemRestorePointIdentity identity,
        DateTimeOffset now) => new(
            claim.ReceiptId,
            plan.PlanId,
            plan.PlanHash,
            existing?.StartedAtUtc ?? claim.ClaimedAtUtc,
            now,
            plan.VolumeIdentity,
            plan.Description,
            plan.Source,
            plan.BaselineId,
            SystemRestorePointReceiptState.Verified,
            SystemRestorePointMutationOutcome.Succeeded,
            identity,
            true,
            existing?.CallerCancellationRequested ?? false,
            Message: "A unique matching restore point was authoritatively read back after restart.",
            MutationState: SystemRestorePointMutationState.Changed,
            ClaimId: claim.ClaimId);

    private static SystemRestorePointReceipt CreateInterruptedReceipt(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointCreateClaim claim,
        SystemRestorePointReceipt? existing,
        DateTimeOffset now,
        string reason) => new(
            claim.ReceiptId,
            plan.PlanId,
            plan.PlanHash,
            existing?.StartedAtUtc ?? claim.ClaimedAtUtc,
            now,
            plan.VolumeIdentity,
            plan.Description,
            plan.Source,
            plan.BaselineId,
            SystemRestorePointReceiptState.Unknown,
            SystemRestorePointMutationOutcome.Unknown,
            null,
            false,
            existing?.CallerCancellationRequested ?? false,
            "SYSTEM_RESTORE.CREATE.INTERRUPTED",
            $"The process stopped after the durable claim was created. {reason}",
            MutationState: SystemRestorePointMutationState.Unknown,
            ClaimId: claim.ClaimId);

    private static SystemRestorePointWorkflowResult ReconciliationFailure(
        SystemRestorePointWorkflowStatus status,
        string message) => new(
            false,
            null,
            status,
            SystemRestorePointPhase.Reconcile,
            message,
            ManualInterventionRequired: status is not (SystemRestorePointWorkflowStatus.Cancelled or SystemRestorePointWorkflowStatus.InvalidRequest));
}
