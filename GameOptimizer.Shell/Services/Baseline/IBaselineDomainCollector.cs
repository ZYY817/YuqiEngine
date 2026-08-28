using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Shell.Services.Baseline;

public interface IBaselineDomainCollector
{
	IReadOnlyList<BaselineDomainDefinition> GetDefinitions();

	Task<BaselineDomainArtifact> CollectDomainAsync(BaselineDomainDefinition domain, string packageDirectory, CancellationToken ct = default(CancellationToken));
}
