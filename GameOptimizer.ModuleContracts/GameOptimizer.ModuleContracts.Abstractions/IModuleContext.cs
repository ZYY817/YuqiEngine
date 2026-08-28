using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.ModuleContracts.Abstractions;

public interface IModuleContext
{
	IServiceProvider Services { get; }

	Task PublishAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default(CancellationToken));

	IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default(CancellationToken));
}
