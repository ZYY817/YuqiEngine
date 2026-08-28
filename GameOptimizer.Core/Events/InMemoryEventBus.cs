// File: GameOptimizer.Core/Events/InMemoryEventBus.cs
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace GameOptimizer.Core.Events;

public sealed class InMemoryEventBus : IEventBus
{
    private const int DefaultChannelCapacity = 1000;
    
    private readonly ConcurrentDictionary<Type, object> _channels = new();
    private readonly ConcurrentDictionary<Type, int> _subscriberCounts = new();
    private readonly int _channelCapacity;

    public event Action<Type, object>? OnEventDropped;

    public InMemoryEventBus() : this(DefaultChannelCapacity) { }

    public InMemoryEventBus(int channelCapacity)
    {
        _channelCapacity = channelCapacity > 0 ? channelCapacity : DefaultChannelCapacity;
    }

    public int GetSubscriberCount<TEvent>() => 
        _subscriberCounts.TryGetValue(typeof(TEvent), out var count) ? count : 0;

    public async Task PublishAsync<TEvent>(TEvent payload, CancellationToken cancellationToken = default)
    {
        var channel = GetOrCreateChannel<TEvent>();
        
        if (channel.Writer.TryWrite(payload))
            return;

        var written = await channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false);
        if (written)
        {
            if (!channel.Writer.TryWrite(payload))
            {
                Debug.WriteLine($"[EventBus] Event dropped: {typeof(TEvent).Name}");
                OnEventDropped?.Invoke(typeof(TEvent), payload!);
            }
        }
        else
        {
            Debug.WriteLine($"[EventBus] Channel closed for: {typeof(TEvent).Name}");
        }
    }

    public async IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = GetOrCreateChannel<TEvent>();
        
        _subscriberCounts.AddOrUpdate(typeof(TEvent), 1, (_, count) => count + 1);
        
        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            _subscriberCounts.AddOrUpdate(typeof(TEvent), 0, (_, count) => Math.Max(0, count - 1));
        }
    }

    private Channel<TEvent> GetOrCreateChannel<TEvent>()
    {
        return (Channel<TEvent>)_channels.GetOrAdd(typeof(TEvent), _ =>
        {
            var options = new BoundedChannelOptions(_channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = false
            };
            return Channel.CreateBounded<TEvent>(options);
        });
    }
}
