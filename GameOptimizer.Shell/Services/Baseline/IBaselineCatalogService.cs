using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public interface IBaselineCatalogService
{
	Task<IReadOnlyList<BaselineCatalogEntry>> GetEntriesAsync(CancellationToken ct = default(CancellationToken));

	Task UpsertAsync(BaselinePackageManifest manifest, CancellationToken ct = default(CancellationToken));

	Task<BaselinePackageManifest?> GetManifestAsync(string packageId, CancellationToken ct = default(CancellationToken));

	Task<(bool Success, string ErrorMessage)> VerifyPackageAsync(string packageId, CancellationToken ct = default(CancellationToken));

	Task<(bool Success, string ErrorMessage, string ExportPath)> ExportPackageAsync(string packageId, string outputDirectory, CancellationToken ct = default(CancellationToken));

	Task<string?> GetPackageDirectoryAsync(string packageId, CancellationToken ct = default(CancellationToken));

	Task<(bool Success, string ErrorMessage)> DeletePackageAsync(string packageId, CancellationToken ct = default(CancellationToken));
}
