namespace GameOptimizer.Core.PowerManagement;

public interface IPowerPlatform
{
    Task<PowerEnvironment> GetEnvironmentAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PowerScheme>> EnumerateSchemesAsync(CancellationToken cancellationToken);
    Task<Guid> GetActiveSchemeAsync(CancellationToken cancellationToken);
    Task<PowerSettingCapability> GetDisplayTimeoutCapabilityAsync(Guid schemeId, CancellationToken cancellationToken);
    Task<PowerSettingValues> ReadDisplayTimeoutAsync(Guid schemeId, bool includeDc, CancellationToken cancellationToken);
    Task<PowerMutationResult> ApplyDisplayTimeoutAsync(
        Guid schemeId,
        PowerSettingValues values,
        Guid schemeToActivate,
        CancellationToken cancellationToken);
}

public interface IPowerBaselineGate
{
    Task<PowerBaselineDecision> EnsureBaselineAsync(PowerPreparedPlan plan, CancellationToken cancellationToken);
}

public interface IPowerWorkflowStore
{
    Task SavePreparedAsync(PowerPreparedPlan plan, CancellationToken cancellationToken);
    Task<PowerPreparedPlan?> GetPreparedAsync(Guid planId, CancellationToken cancellationToken);
    Task<bool> TryClaimPlanAsync(Guid planId, string planHash, CancellationToken cancellationToken);
    Task SaveReceiptAsync(PowerReceipt receipt, CancellationToken cancellationToken);
    Task<PowerReceipt?> GetReceiptAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PowerReceipt>> ListReceiptsAsync(CancellationToken cancellationToken);
    Task<bool> TryClaimRecoveryAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<IPowerResourceLease> AcquireResourceLeaseAsync(string resourceKey, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IPowerConfirmationSecretProvider
{
    byte[] GetConfirmationSecret();
}

public interface IPowerResourceLease : IAsyncDisposable
{
    string ResourceKey { get; }
}

public interface IPowerConfirmationAuthority
{
    PowerConfirmationToken Issue(PowerPreparedPlan plan, DateTimeOffset nowUtc, TimeSpan lifetime);
    bool TryConsume(PowerPreparedPlan plan, PowerConfirmationToken token, DateTimeOffset nowUtc);
}

public interface IPowerManagementOwner
{
    Task<PowerState> InspectAsync(Guid? targetSchemeId, CancellationToken cancellationToken);
    Task<PowerWorkflowResult> PrepareAsync(PowerPrepareRequest request, CancellationToken cancellationToken);
    Task<PowerWorkflowResult> ExecuteAsync(
        PowerPreparedPlan plan,
        PowerConfirmationToken confirmation,
        CancellationToken cancellationToken);
    Task<PowerWorkflowResult> RestoreAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PowerReceipt>> ListRecoverableReceiptsAsync(CancellationToken cancellationToken);
}
