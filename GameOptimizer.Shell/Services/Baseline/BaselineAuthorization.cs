using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed record BaselineAuthorizationEvidence(
	string PackageId,
	string ManifestSha256,
	DateTime RecoverabilityCheckedUtc);

public sealed record BaselineAuthorizationVerification(
	bool Success,
	BaselineAuthorizationEvidence? Evidence,
	string ErrorCode,
	string Message)
{
	public static BaselineAuthorizationVerification Failed(string errorCode, string message) =>
		new(false, null, errorCode, message);
}

public interface IBaselineAuthorizationRepository
{
	Task<BaselineSafetyState> LoadAsync(CancellationToken ct = default);

	Task SaveAsync(BaselineSafetyState state, CancellationToken ct = default);
}

public interface IBaselineAuthorizationPackageVerifier
{
	Task<BaselineAuthorizationVerification> VerifyAsync(string packageId, CancellationToken ct = default);
}

public interface IBaselineAuthorizationPathStrategy
{
	Task<string?> ResolvePackageDirectoryAsync(string packageId, CancellationToken ct = default);
}

public sealed record BaselineAuthorizationDecision(
	bool Allowed,
	BaselineSafetyState State,
	BaselineAuthorizationVerification Verification);

public static class BaselineAuthorizationPolicy
{
	public static async Task<BaselineAuthorizationDecision> EvaluateAsync(
		IBaselineAuthorizationRepository repository,
		IBaselineAuthorizationPackageVerifier verifier,
		CancellationToken ct = default)
	{
		ArgumentNullException.ThrowIfNull(repository);
		ArgumentNullException.ThrowIfNull(verifier);
		try
		{
			BaselineSafetyState state = await repository.LoadAsync(ct).ConfigureAwait(false);
			if (state.FirstRunState != FirstRunBaselineState.CompletedVerified || state.IsReadonlyMode)
			{
				return new(false, state, BaselineAuthorizationVerification.Failed("baseline.gate_not_completed", "Baseline gate is not completed and writable."));
			}

			BaselineAuthorizationVerification verification = await verifier.VerifyAsync(state.AuthorizedPackageId ?? string.Empty, ct).ConfigureAwait(false);
			bool matches = verification.Success && verification.Evidence is not null &&
				string.Equals(state.AuthorizedPackageId, verification.Evidence.PackageId, StringComparison.Ordinal) &&
				string.Equals(state.AuthorizedManifestSha256, verification.Evidence.ManifestSha256, StringComparison.OrdinalIgnoreCase) &&
				state.AuthorizedRecoverabilityCheckedUtc == verification.Evidence.RecoverabilityCheckedUtc;
			return new(matches, state, matches
				? verification
				: BaselineAuthorizationVerification.Failed("baseline.authorization_drift", "Baseline package authorization evidence changed."));
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			BaselineSafetyState blocked = new()
			{
				FirstRunState = FirstRunBaselineState.Corrupted,
				IsReadonlyMode = true,
				LastEntryPoint = "baseline_authorization_repository_failure",
				LastError = ex.Message
			};
			return new(false, blocked, BaselineAuthorizationVerification.Failed("baseline.repository_failure", ex.Message));
		}
	}
}

public static class BaselineGateRequirement
{
	public static IBaselineGateService Require(IBaselineGateService? gate) =>
		gate ?? throw new ArgumentNullException(nameof(gate), "A baseline gate is mandatory for guided execution.");
}
