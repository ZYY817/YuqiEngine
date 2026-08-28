using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public interface IFirstRunBaselineOrchestrator
{
	IReadOnlyList<BaselineDomainDefinition> GetDefinitions();

	Task<BaselineRuntimeState> GetOrCreateRuntimeStateAsync(bool forceReset, string? creationSource = null, CancellationToken ct = default(CancellationToken));

	Task<BaselineRunResult> RunAsync(IProgress<BaselineProgressEvent>? progress = null, CancellationToken ct = default(CancellationToken));

	Task<BaselineManualGuideCheckResult> RecheckManualGuideAsync(CancellationToken ct = default(CancellationToken));

	Task<BaselinePackageManifest?> GetCurrentManifestAsync(CancellationToken ct = default(CancellationToken));
}
