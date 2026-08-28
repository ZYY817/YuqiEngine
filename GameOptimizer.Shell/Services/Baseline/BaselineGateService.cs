using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineGateService : IBaselineGateService
{
	private readonly IBaselineAuthorizationRepository _repository;

	private readonly IBaselineAuthorizationPackageVerifier _packageVerifier;

	private readonly IBaselineRiskAcknowledgementPrompt _riskAcknowledgementPrompt;

	private readonly ILogger<BaselineGateService> _logger;

	public BaselineGateService(
		BaselineSafetyStateStore stateStore,
		IBaselineCatalogService catalogService,
		IBaselineIntegrityService integrityService,
		IBaselineRiskAcknowledgementPrompt riskAcknowledgementPrompt,
		ILogger<BaselineGateService> logger)
		: this(
			new BaselineSafetyStateAuthorizationRepository(stateStore),
			new BaselineAuthorizationPackageVerifier(
				catalogService,
				integrityService,
				new BaselineCatalogAuthorizationPathStrategy(catalogService)),
			riskAcknowledgementPrompt,
			logger)
	{
	}

	public BaselineGateService(
		IBaselineAuthorizationRepository repository,
		IBaselineAuthorizationPackageVerifier packageVerifier,
		IBaselineRiskAcknowledgementPrompt riskAcknowledgementPrompt,
		ILogger<BaselineGateService> logger)
	{
		_repository = repository ?? throw new ArgumentNullException(nameof(repository));
		_packageVerifier = packageVerifier ?? throw new ArgumentNullException(nameof(packageVerifier));
		_riskAcknowledgementPrompt = riskAcknowledgementPrompt ?? throw new ArgumentNullException(nameof(riskAcknowledgementPrompt));
		_logger = logger ?? throw new ArgumentNullException("logger");
	}

	public async Task<BaselineGateDecision> EnsureCanOptimizeAsync(string entryPoint, CancellationToken ct = default(CancellationToken))
	{
		BaselineAuthorizationDecision authorization = await BaselineAuthorizationPolicy.EvaluateAsync(_repository, _packageVerifier, ct).ConfigureAwait(continueOnCapturedContext: false);
		BaselineSafetyState state = authorization.State;
		string safeEntryPoint = (string.IsNullOrWhiteSpace(entryPoint) ? "unknown" : entryPoint);
		if (authorization.Allowed)
		{
			return new BaselineGateDecision(
				BaselineGateDecisionKind.Allowed,
				"首次安全基线有效，恢复保障已验证。",
				state,
				CreateEvidenceId("verified", safeEntryPoint, state));
		}

		try
		{
			var acknowledged = await _riskAcknowledgementPrompt.ConfirmAsync(
				new BaselineRiskAcknowledgementRequest(
					safeEntryPoint,
					state.FirstRunState,
					"当前没有可验证的首次安全基线。继续后若结果不符合预期，不能使用“恢复到首次安全基线”完整回退。请确认你理解这一恢复限制，并决定是否继续本次操作。",
					"恢复能力：仍可使用该功能自身生成的恢复材料、隔离区、撤销入口或现有系统还原点（如适用）；若这些能力不可用，可能需要手动恢复。"),
				ct).ConfigureAwait(false);
			if (acknowledged)
			{
				_logger.LogWarning(
					"[BaselineGate][EnsureCanOptimize] Proceeding with per-operation risk acknowledgement. entry={EntryPoint}, state={State}",
					safeEntryPoint,
					state.FirstRunState);
				return new BaselineGateDecision(
					BaselineGateDecisionKind.AllowedWithRiskAcknowledgement,
					"用户已确认本次操作在无可验证首次安全基线下继续；其他安全检查保持生效。",
					state,
					CreateEvidenceId("acknowledged", safeEntryPoint, state));
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex,
				"[BaselineGate][EnsureCanOptimize] Risk acknowledgement prompt failed. entry={EntryPoint}, state={State}",
				safeEntryPoint,
				state.FirstRunState);
			return new BaselineGateDecision(
				BaselineGateDecisionKind.BlockedRiskNotAcknowledged,
				"无法取得本次无基线风险确认，未执行操作。",
				state);
		}

		_logger.LogInformation(
			"[BaselineGate][EnsureCanOptimize] Risk acknowledgement declined. entry={EntryPoint}, state={State}",
			safeEntryPoint,
			state.FirstRunState);
		return new BaselineGateDecision(
			BaselineGateDecisionKind.BlockedRiskNotAcknowledged,
			"用户未确认在无可验证首次安全基线下继续，未执行操作。",
			state);
	}

	private static string CreateEvidenceId(string mode, string entryPoint, BaselineSafetyState state)
	{
		var payload = string.Join('\u001F',
			mode,
			entryPoint,
			state.FirstRunState,
			state.AuthorizedPackageId ?? string.Empty,
			state.AuthorizedManifestSha256 ?? string.Empty,
			Guid.NewGuid().ToString("N"));
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
	}

	public Task<BaselineSafetyState> GetStateAsync(CancellationToken ct = default(CancellationToken))
	{
		return _repository.LoadAsync(ct);
	}

	public async Task SetReadonlyModeAsync(bool isReadonly, string entryPoint, string? reason = null, CancellationToken ct = default(CancellationToken))
	{
		BaselineSafetyState state = await _repository.LoadAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		state.IsReadonlyMode = isReadonly;
		state.LastEntryPoint = (string.IsNullOrWhiteSpace(entryPoint) ? "unknown" : entryPoint);
		state.LastError = reason;
		await _repository.SaveAsync(state, ct).ConfigureAwait(continueOnCapturedContext: false);
		_logger.LogInformation("[BaselineGate][SetReadonlyMode] readonly={Readonly}, entry={EntryPoint}, reason={Reason}", isReadonly, state.LastEntryPoint, reason ?? string.Empty);
	}

	public async Task MarkBaselineCompletedVerifiedAsync(string entryPoint, string packageId, CancellationToken ct = default(CancellationToken))
	{
		BaselineAuthorizationVerification verification = await _packageVerifier.VerifyAsync(packageId, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!verification.Success || verification.Evidence is null)
		{
			await MarkBaselineFailureAsync(entryPoint, FirstRunBaselineState.Corrupted, verification.Message, ct).ConfigureAwait(false);
			throw new InvalidOperationException("Baseline authorization verification failed: " + verification.ErrorCode);
		}

		BaselineSafetyState state = await _repository.LoadAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		state.FirstRunState = FirstRunBaselineState.CompletedVerified;
		state.IsReadonlyMode = false;
		state.CompletedUtc = DateTime.UtcNow;
		state.LastEntryPoint = (string.IsNullOrWhiteSpace(entryPoint) ? "unknown" : entryPoint);
		state.LastError = null;
		state.AuthorizedPackageId = verification.Evidence.PackageId;
		state.AuthorizedManifestSha256 = verification.Evidence.ManifestSha256;
		state.AuthorizedRecoverabilityCheckedUtc = verification.Evidence.RecoverabilityCheckedUtc;
		await _repository.SaveAsync(state, ct).ConfigureAwait(continueOnCapturedContext: false);
		_logger.LogInformation("[BaselineGate][MarkBaselineCompletedVerified] entry={EntryPoint}", state.LastEntryPoint);
	}

	public async Task MarkBaselineFailureAsync(string entryPoint, FirstRunBaselineState state, string? reason = null, CancellationToken ct = default(CancellationToken))
	{
		if (state == FirstRunBaselineState.CompletedVerified)
		{
			throw new ArgumentOutOfRangeException("state", "Failure state cannot be CompletedVerified.");
		}
		BaselineSafetyState current = await _repository.LoadAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		current.FirstRunState = state;
		current.LastEntryPoint = (string.IsNullOrWhiteSpace(entryPoint) ? "unknown" : entryPoint);
		current.LastError = reason;
		current.AuthorizedPackageId = null;
		current.AuthorizedManifestSha256 = null;
		current.AuthorizedRecoverabilityCheckedUtc = null;
		await _repository.SaveAsync(current, ct).ConfigureAwait(continueOnCapturedContext: false);
		_logger.LogWarning("[BaselineGate][MarkBaselineFailure] state={State}, entry={EntryPoint}, reason={Reason}", state, current.LastEntryPoint, reason ?? string.Empty);
	}
}
