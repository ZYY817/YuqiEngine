using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public interface IBaselineIntegrityService
{
	Task<BaselinePackageManifest> WriteManifestAsync(string packageDirectory, BaselinePackageManifest manifest, CancellationToken ct = default(CancellationToken));

	Task<(bool Success, string ErrorMessage)> VerifyPackageAsync(string packageDirectory, BaselinePackageManifest manifest, CancellationToken ct = default(CancellationToken));
}
