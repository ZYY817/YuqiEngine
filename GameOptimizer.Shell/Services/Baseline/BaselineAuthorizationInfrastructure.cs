using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public sealed class BaselineCatalogAuthorizationPathStrategy : IBaselineAuthorizationPathStrategy
{
	private readonly IBaselineCatalogService _catalog;

	public BaselineCatalogAuthorizationPathStrategy(IBaselineCatalogService catalog) =>
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

	public Task<string?> ResolvePackageDirectoryAsync(string packageId, CancellationToken ct = default) =>
		_catalog.GetPackageDirectoryAsync(packageId, ct);
}

public sealed class BaselineAuthorizationPackageVerifier : IBaselineAuthorizationPackageVerifier
{
	private readonly IBaselineCatalogService _catalog;
	private readonly IBaselineIntegrityService _integrity;
	private readonly IBaselineAuthorizationPathStrategy _paths;

	public BaselineAuthorizationPackageVerifier(IBaselineCatalogService catalog, IBaselineIntegrityService integrity, IBaselineAuthorizationPathStrategy paths)
	{
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		_integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
	}

	public async Task<BaselineAuthorizationVerification> VerifyAsync(string packageId, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(packageId))
		{
			return BaselineAuthorizationVerification.Failed("baseline.package_id_missing", "Baseline package identity is missing.");
		}

		BaselinePackageManifest? manifest = await _catalog.GetManifestAsync(packageId, ct).ConfigureAwait(false);
		if (manifest is null || !string.Equals(manifest.PackageId, packageId, StringComparison.Ordinal))
		{
			return BaselineAuthorizationVerification.Failed("baseline.package_replaced", "Baseline package manifest is missing or its identity changed.");
		}

		if (!string.Equals(manifest.PackageType, "BaselineFirstRun", StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(manifest.LifecycleState, "Active", StringComparison.OrdinalIgnoreCase) ||
			manifest.BaselineState != FirstRunBaselineState.CompletedVerified ||
			!string.Equals(manifest.IntegrityState, "Verified", StringComparison.OrdinalIgnoreCase))
		{
			return BaselineAuthorizationVerification.Failed("baseline.package_not_verified", "Baseline package is not in an active verified state.");
		}

		if (!string.Equals(manifest.RecoverabilityCheckState, "Passed", StringComparison.OrdinalIgnoreCase) || manifest.LastRecoverabilityCheckUtc is null)
		{
			return BaselineAuthorizationVerification.Failed("baseline.recoverability_unverified", "Baseline recoverability has not passed verification.");
		}

		string? packageDirectory = await _paths.ResolvePackageDirectoryAsync(packageId, ct).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(packageDirectory))
		{
			return BaselineAuthorizationVerification.Failed("baseline.package_path_missing", "Baseline package directory cannot be resolved.");
		}

		(bool success, string error) = await _integrity.VerifyPackageAsync(packageDirectory, manifest, ct).ConfigureAwait(false);
		if (!success)
		{
			return BaselineAuthorizationVerification.Failed("baseline.package_integrity_failed", error);
		}

		string manifestPath = Path.Combine(packageDirectory, "manifest.json");
		if (!File.Exists(manifestPath))
		{
			return BaselineAuthorizationVerification.Failed("baseline.manifest_missing", "Baseline manifest file is missing.");
		}

		byte[] bytes = await File.ReadAllBytesAsync(manifestPath, ct).ConfigureAwait(false);
		string sha256 = Convert.ToHexString(SHA256.HashData(bytes));
		return new(true, new(packageId, sha256, manifest.LastRecoverabilityCheckUtc.Value), string.Empty, "Baseline package authorization evidence is valid.");
	}
}
