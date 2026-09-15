using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameOptimizer.Core.Events;

public interface IEventBus
{
	Task PublishAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default(CancellationToken));

	IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken = default(CancellationToken));
}
