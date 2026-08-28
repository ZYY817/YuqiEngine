namespace GameOptimizer.Core.SystemRestore;

public interface ISystemRestorePointPlatform
{
    Task<SystemRestorePointProbe> ProbeAsync(CancellationToken cancellationToken);

    Task<SystemRestorePointMutationResult> CreateRestorePointAsync(
        SystemRestorePointCreateCommand command,
        CancellationToken cancellationToken);
}

public interface ISystemRestorePointWorkflowStore
{
    Task SaveCreatePlanAsync(SystemRestorePointCreatePlan plan, CancellationToken cancellationToken);
    Task<SystemRestorePointCreatePlan?> LoadCreatePlanAsync(Guid planId, CancellationToken cancellationToken);
    Task<SystemRestorePointCreateClaim?> TryClaimCreatePlanAsync(
        SystemRestorePointCreateClaim claim,
        CancellationToken cancellationToken);
    Task<SystemRestorePointCreateClaim?> LoadCreateClaimAsync(
        Guid planId,
        string planHash,
        CancellationToken cancellationToken);
    Task SaveCreateClaimAsync(
        SystemRestorePointCreateClaim claim,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SystemRestorePointCreateClaim>> ListCreateClaimsAsync(
        CancellationToken cancellationToken);
    Task SaveRestorePlanAsync(SystemRestoreExecutionPlan plan, CancellationToken cancellationToken);
    Task<SystemRestoreExecutionPlan?> LoadRestorePlanAsync(Guid planId, CancellationToken cancellationToken);
    Task SaveReceiptAsync(SystemRestorePointReceipt receipt, CancellationToken cancellationToken);
    Task<SystemRestorePointReceipt?> LoadReceiptAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<SystemRestorePointReceiptRecoveryResult> RecoverReceiptAsync(
        SystemRestorePointReceipt replacement,
        CancellationToken cancellationToken);
    Task<ISystemRestorePointResourceLease> AcquireResourceLeaseAsync(
        string resourceKey,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface ISystemRestorePointResourceLease : IAsyncDisposable
{
    string ResourceKey { get; }
}

public interface ISystemRestorePointConfirmationAuthority
{
    SystemRestorePointConfirmationToken Issue(
        SystemRestorePointCreatePlan plan,
        DateTimeOffset nowUtc,
        TimeSpan lifetime);

    SystemRestorePointConfirmationToken Issue(
        SystemRestoreExecutionPlan plan,
        DateTimeOffset nowUtc,
        TimeSpan lifetime);

    bool TryConsume(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointConfirmationToken token,
        DateTimeOffset nowUtc);

    bool TryConsume(
        SystemRestoreExecutionPlan plan,
        SystemRestorePointConfirmationToken token,
        DateTimeOffset nowUtc);
}

public interface ISystemRestorePointOwner
{
    Task<SystemRestorePointWorkflowResult> InspectAsync(CancellationToken cancellationToken = default);

    Task<SystemRestorePointWorkflowResult> PrepareCreateAsync(
        SystemRestorePointCreateRequest request,
        CancellationToken cancellationToken = default);

    SystemRestorePointConfirmationToken? ConfirmCreate(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointConfirmationEvidence evidence);

    Task<SystemRestorePointWorkflowResult> ExecuteCreateAsync(
        SystemRestorePointCreatePlan plan,
        SystemRestorePointConfirmationToken confirmation,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemRestorePointWorkflowResult>> ReconcileInterruptedCreatesAsync(
        CancellationToken cancellationToken = default);

    Task<SystemRestorePointWorkflowResult> ReconcileInterruptedCreateAsync(
        Guid planId,
        CancellationToken cancellationToken = default);

    Task<SystemRestorePointWorkflowResult> ResolveInterruptedCreateAsync(
        SystemRestorePointManualResolution resolution,
        CancellationToken cancellationToken = default);

    Task<SystemRestorePointWorkflowResult> PrepareSystemRestoreAsync(
        SystemRestorePointIdentity target,
        string source,
        CancellationToken cancellationToken = default);

    Task<SystemRestorePointWorkflowResult> ExecuteSystemRestoreAsync(
        SystemRestoreExecutionPlan plan,
        SystemRestorePointConfirmationToken? confirmation,
        CancellationToken cancellationToken = default);
}

public interface ISystemRestorePointStartupReconciliation
{
    SystemRestorePointStartupReconciliationResult? LastResult => null;

    Task<SystemRestorePointStartupReconciliationResult> RunAsync(
        CancellationToken cancellationToken = default);
}
