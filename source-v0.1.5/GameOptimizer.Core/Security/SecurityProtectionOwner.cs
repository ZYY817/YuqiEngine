namespace GameOptimizer.Core.Security;

public sealed class SecurityProtectionOwner : ISecurityProtectionOwner
{
    private readonly ISecurityProtectionInspector _inspector;
    private readonly TimeProvider _clock;
    private readonly SecurityProtectionTimingOptions _timing;

    public SecurityProtectionOwner(
        ISecurityProtectionInspector inspector,
        TimeProvider? clock = null,
        SecurityProtectionTimingOptions? timing = null)
    {
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _clock = clock ?? TimeProvider.System;
        _timing = timing ?? SecurityProtectionTimingOptions.Default;
        if (_timing.InspectionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timing), "Security inspection timeout must be positive.");
    }

    public async Task<SecurityProtectionDecision> EvaluateAsync(
        SecurityProtectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
            return Denied(request, SecurityProtectionStatus.Cancelled, "Security policy evaluation was cancelled before inspection.");

        if (request.Intent == SecurityProtectionIntent.WeakenProtection)
        {
            return Denied(request, SecurityProtectionStatus.PolicyDenied,
                "Yuqi Engine security policy forbids disabling or weakening Defender, Firewall, UAC, SmartScreen, exploit mitigations, or security notifications for optimization.");
        }

        SecurityProtectionEnvironment environment;
        using var timeout = new CancellationTokenSource(_timing.InspectionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            environment = await _inspector.InspectAsync(request.Area, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Denied(request, SecurityProtectionStatus.Cancelled, "Security protection inspection was cancelled; no mutation was attempted.");
        }
        catch (OperationCanceledException)
        {
            return Denied(request, SecurityProtectionStatus.TimedOut, "Security protection inspection timed out; no mutation was attempted.");
        }
        catch (Exception ex)
        {
            return Denied(request, SecurityProtectionStatus.PlatformFailure,
                $"Security protection inspection failed closed: {ex.GetType().Name}.");
        }

        if (!environment.IsWindows || environment.WindowsBuild < SecurityProtectionIdentity.MinimumWindowsBuild)
            return Denied(request, SecurityProtectionStatus.Unsupported, "This Windows build is not supported by the security protection boundary.");
        if (!environment.IsAdministrator)
            return Denied(request, SecurityProtectionStatus.PermissionDenied, "Administrator authorization is required before any security repair can be prepared.");
        if (environment.HasThirdPartySecurityProvider)
            return Denied(request, SecurityProtectionStatus.ThirdPartySecurityProvider, "A third-party security provider owns this protection state; Yuqi Engine will not override it.");
        if (environment.IsPolicyManaged)
            return Denied(request, SecurityProtectionStatus.ManagedByPolicy, "This protection state is managed by policy and cannot be overridden.");
        if (environment.IsTamperProtected)
            return Denied(request, SecurityProtectionStatus.TamperProtected, "Tamper Protection owns this state; bypass is forbidden.");
        if (!environment.IsStateComplete || string.IsNullOrWhiteSpace(environment.StateFingerprint))
            return Denied(request, SecurityProtectionStatus.PartialState, "Authoritative security state is incomplete; recovery cannot be planned.");
        if (!string.IsNullOrWhiteSpace(request.ExpectedStateFingerprint)
            && !string.Equals(request.ExpectedStateFingerprint, environment.StateFingerprint, StringComparison.Ordinal))
            return Denied(request, SecurityProtectionStatus.DriftBlocked, "Security state changed since inspection; recovery is blocked.");

        return Denied(request, SecurityProtectionStatus.RecoveryUnproven,
            "No fixed, authoritative security recovery adapter with complete snapshot and readback is registered; recovery remains unavailable.");
    }

    private SecurityProtectionDecision Denied(
        SecurityProtectionRequest request,
        SecurityProtectionStatus status,
        string message) =>
        new(Guid.NewGuid(), _clock.GetUtcNow(), status, request.Area, request.Intent, false, message);
}

public sealed class UnavailableSecurityProtectionOwner : ISecurityProtectionOwner
{
    public static UnavailableSecurityProtectionOwner Instance { get; } = new();
    private UnavailableSecurityProtectionOwner() { }

    public Task<SecurityProtectionDecision> EvaluateAsync(
        SecurityProtectionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = request.Intent == SecurityProtectionIntent.WeakenProtection
            ? SecurityProtectionStatus.PolicyDenied
            : SecurityProtectionStatus.RecoveryUnproven;
        return Task.FromResult(new SecurityProtectionDecision(
            Guid.NewGuid(), DateTimeOffset.UtcNow, status, request.Area, request.Intent, false,
            request.Intent == SecurityProtectionIntent.WeakenProtection
                ? "Security protection reductions are denied by product policy."
                : "The typed security recovery owner is not connected; recovery is blocked."));
    }
}
