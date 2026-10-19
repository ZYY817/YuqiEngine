using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Tests.Changes;

public sealed class BaselineAuthorizationPolicyTests
{
	private static readonly DateTime CheckedUtc = new(2026, 8, 13, 1, 2, 3, DateTimeKind.Utc);

	[Fact]
	public async Task Valid_bound_package_is_allowed()
	{
		BaselineSafetyState state = CompletedState("PKG-1", "ABC");
		var decision = await BaselineAuthorizationPolicy.EvaluateAsync(
			new FakeRepository(state),
			new FakeVerifier(new(true, new("PKG-1", "ABC", CheckedUtc), string.Empty, "ok")));

		Assert.True(decision.Allowed);
	}

	[Theory]
	[InlineData("baseline.package_replaced")]
	[InlineData("baseline.package_integrity_failed")]
	[InlineData("baseline.recoverability_unverified")]
	public async Task Damaged_replaced_or_unrecoverable_package_is_blocked(string code)
	{
		var decision = await BaselineAuthorizationPolicy.EvaluateAsync(
			new FakeRepository(CompletedState("PKG-1", "ABC")),
			new FakeVerifier(BaselineAuthorizationVerification.Failed(code, "failed")));

		Assert.False(decision.Allowed);
	}

	[Fact]
	public async Task Manifest_hash_drift_is_blocked()
	{
		var decision = await BaselineAuthorizationPolicy.EvaluateAsync(
			new FakeRepository(CompletedState("PKG-1", "OLD")),
			new FakeVerifier(new(true, new("PKG-1", "NEW", CheckedUtc), string.Empty, "ok")));

		Assert.False(decision.Allowed);
		Assert.Equal("baseline.authorization_drift", decision.Verification.ErrorCode);
	}

	[Fact]
	public async Task Repository_exception_fails_closed()
	{
		var decision = await BaselineAuthorizationPolicy.EvaluateAsync(
			new FakeRepository(new IOException("repository unavailable")),
			new FakeVerifier(new(true, new("PKG-1", "ABC", CheckedUtc), string.Empty, "ok")));

		Assert.False(decision.Allowed);
		Assert.True(decision.State.IsReadonlyMode);
		Assert.Equal(FirstRunBaselineState.Corrupted, decision.State.FirstRunState);
		Assert.Equal("baseline.repository_failure", decision.Verification.ErrorCode);
	}

	[Fact]
	public async Task Cancellation_is_not_converted_to_authorization()
	{
		using CancellationTokenSource cts = new();
		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			BaselineAuthorizationPolicy.EvaluateAsync(
				new FakeRepository(CompletedState("PKG-1", "ABC")),
				new CancelingVerifier(),
				cts.Token));
	}

	[Fact]
	public async Task Repository_cancellation_is_not_converted_to_authorization()
	{
		using CancellationTokenSource cts = new();
		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			BaselineAuthorizationPolicy.EvaluateAsync(
				new CancelingRepository(),
				new FakeVerifier(new(true, new("PKG-1", "ABC", CheckedUtc), string.Empty, "ok")),
				cts.Token));
	}

	[Fact]
	public void Missing_guided_gate_is_rejected()
	{
		Assert.Throws<ArgumentNullException>(() => BaselineGateRequirement.Require(null));
	}

	[Fact]
	public async Task Concrete_verifier_binds_manifest_hash_and_recoverability()
	{
		string directory = CreatePackageDirectory("manifest-v1");
		try
		{
			BaselinePackageManifest manifest = VerifiedManifest("PKG-1");
			var verifier = new BaselineAuthorizationPackageVerifier(
				new FakeCatalog(manifest, directory),
				new FakeIntegrity(true),
				new FakePathStrategy(directory));

			BaselineAuthorizationVerification result = await verifier.VerifyAsync("PKG-1");

			Assert.True(result.Success);
			Assert.NotNull(result.Evidence);
			Assert.Equal(64, result.Evidence!.ManifestSha256.Length);
			Assert.Equal(CheckedUtc, result.Evidence.RecoverabilityCheckedUtc);
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public async Task Concrete_verifier_rejects_replaced_manifest()
	{
		var verifier = new BaselineAuthorizationPackageVerifier(
			new FakeCatalog(VerifiedManifest("OTHER"), "unused"),
			new FakeIntegrity(true),
			new FakePathStrategy("unused"));

		BaselineAuthorizationVerification result = await verifier.VerifyAsync("PKG-1");

		Assert.False(result.Success);
		Assert.Equal("baseline.package_replaced", result.ErrorCode);
	}

	[Fact]
	public async Task Concrete_verifier_rejects_failed_self_check_before_file_access()
	{
		BaselinePackageManifest manifest = VerifiedManifest("PKG-1");
		manifest.RecoverabilityCheckState = "Failed";
		var verifier = new BaselineAuthorizationPackageVerifier(
			new FakeCatalog(manifest, "unused"),
			new FakeIntegrity(true),
			new FakePathStrategy("unused"));

		BaselineAuthorizationVerification result = await verifier.VerifyAsync("PKG-1");

		Assert.False(result.Success);
		Assert.Equal("baseline.recoverability_unverified", result.ErrorCode);
	}

	[Fact]
	public async Task Concrete_verifier_rejects_integrity_failure()
	{
		var verifier = new BaselineAuthorizationPackageVerifier(
			new FakeCatalog(VerifiedManifest("PKG-1"), "unused"),
			new FakeIntegrity(false),
			new FakePathStrategy("unused"));

		BaselineAuthorizationVerification result = await verifier.VerifyAsync("PKG-1");

		Assert.False(result.Success);
		Assert.Equal("baseline.package_integrity_failed", result.ErrorCode);
	}

	private static BaselineSafetyState CompletedState(string packageId, string sha256) => new()
	{
		FirstRunState = FirstRunBaselineState.CompletedVerified,
		IsReadonlyMode = false,
		AuthorizedPackageId = packageId,
		AuthorizedManifestSha256 = sha256,
		AuthorizedRecoverabilityCheckedUtc = CheckedUtc
	};

	private sealed class FakeRepository : IBaselineAuthorizationRepository
	{
		private readonly BaselineSafetyState? _state;
		private readonly Exception? _exception;

		public FakeRepository(BaselineSafetyState state) => _state = state;
		public FakeRepository(Exception exception) => _exception = exception;

		public Task<BaselineSafetyState> LoadAsync(CancellationToken ct = default) =>
			_exception is null ? Task.FromResult(_state!) : Task.FromException<BaselineSafetyState>(_exception);

		public Task SaveAsync(BaselineSafetyState state, CancellationToken ct = default) => Task.CompletedTask;
	}

	private sealed class FakeVerifier : IBaselineAuthorizationPackageVerifier
	{
		private readonly BaselineAuthorizationVerification _result;
		public FakeVerifier(BaselineAuthorizationVerification result) => _result = result;
		public Task<BaselineAuthorizationVerification> VerifyAsync(string packageId, CancellationToken ct = default) => Task.FromResult(_result);
	}

	private sealed class CancelingVerifier : IBaselineAuthorizationPackageVerifier
	{
		public Task<BaselineAuthorizationVerification> VerifyAsync(string packageId, CancellationToken ct = default) => Task.FromCanceled<BaselineAuthorizationVerification>(ct);
	}

	private sealed class CancelingRepository : IBaselineAuthorizationRepository
	{
		public Task<BaselineSafetyState> LoadAsync(CancellationToken ct = default) => Task.FromCanceled<BaselineSafetyState>(ct);
		public Task SaveAsync(BaselineSafetyState state, CancellationToken ct = default) => Task.FromCanceled(ct);
	}

	private static BaselinePackageManifest VerifiedManifest(string packageId) => new()
	{
		PackageId = packageId,
		PackageType = "BaselineFirstRun",
		LifecycleState = "Active",
		BaselineState = FirstRunBaselineState.CompletedVerified,
		IntegrityState = "Verified",
		RecoverabilityCheckState = "Passed",
		LastRecoverabilityCheckUtc = CheckedUtc
	};

	private static string CreatePackageDirectory(string manifestContent)
	{
		string path = Path.Combine(Path.GetTempPath(), "yuqi-baseline-auth-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		File.WriteAllText(Path.Combine(path, "manifest.json"), manifestContent);
		return path;
	}

	private sealed class FakePathStrategy : IBaselineAuthorizationPathStrategy
	{
		private readonly string _path;
		public FakePathStrategy(string path) => _path = path;
		public Task<string?> ResolvePackageDirectoryAsync(string packageId, CancellationToken ct = default) => Task.FromResult<string?>(_path);
	}

	private sealed class FakeIntegrity : IBaselineIntegrityService
	{
		private readonly bool _success;
		public FakeIntegrity(bool success) => _success = success;
		public Task<BaselinePackageManifest> WriteManifestAsync(string packageDirectory, BaselinePackageManifest manifest, CancellationToken ct = default) => Task.FromResult(manifest);
		public Task<(bool Success, string ErrorMessage)> VerifyPackageAsync(string packageDirectory, BaselinePackageManifest manifest, CancellationToken ct = default) => Task.FromResult((_success, _success ? string.Empty : "corrupt"));
	}

	private sealed class FakeCatalog : IBaselineCatalogService
	{
		private readonly BaselinePackageManifest? _manifest;
		private readonly string? _path;
		public FakeCatalog(BaselinePackageManifest? manifest, string? path) { _manifest = manifest; _path = path; }
		public Task<IReadOnlyList<BaselineCatalogEntry>> GetEntriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BaselineCatalogEntry>>(Array.Empty<BaselineCatalogEntry>());
		public Task UpsertAsync(BaselinePackageManifest manifest, CancellationToken ct = default) => Task.CompletedTask;
		public Task<BaselinePackageManifest?> GetManifestAsync(string packageId, CancellationToken ct = default) => Task.FromResult(_manifest);
		public Task<(bool Success, string ErrorMessage)> VerifyPackageAsync(string packageId, CancellationToken ct = default) => Task.FromResult((true, string.Empty));
		public Task<(bool Success, string ErrorMessage, string ExportPath)> ExportPackageAsync(string packageId, string outputDirectory, CancellationToken ct = default) => Task.FromResult((false, string.Empty, string.Empty));
		public Task<string?> GetPackageDirectoryAsync(string packageId, CancellationToken ct = default) => Task.FromResult(_path);
		public Task<(bool Success, string ErrorMessage)> DeletePackageAsync(string packageId, CancellationToken ct = default) => Task.FromResult((false, string.Empty));
	}
}
