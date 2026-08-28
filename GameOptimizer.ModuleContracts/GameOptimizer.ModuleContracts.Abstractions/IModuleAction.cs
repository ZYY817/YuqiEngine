using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.ModuleContracts.Models;

namespace GameOptimizer.ModuleContracts.Abstractions;

public interface IModuleAction
{
	string Id { get; }

	ActionDescriptor Descriptor { get; }

	Task ExecuteAsync(IModuleContext context, CancellationToken cancellationToken);
}
