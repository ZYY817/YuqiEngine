using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameOptimizer.Core.Events;
using GameOptimizer.ModuleContracts.Abstractions;

namespace GameOptimizer.Core.Modules;

public sealed class ModuleContext : IModuleContext
{
	private readonly IEventBus _eventBus;

	public IServiceProvider Services { get; }

	public ModuleContext(IServiceProvider services, IEventBus eventBus)
	{
		Services = services;
		_eventBus = eventBus;
	}

	public Task PublishAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _eventBus.PublishAsync(payload, cancellationToken);
	}

	public IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default(CancellationToken))
	{
		return _eventBus.SubscribeAsync<TEvent>(cancellationToken);
	}
}
