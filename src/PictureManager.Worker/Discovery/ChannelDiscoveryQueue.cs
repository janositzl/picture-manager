using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using PictureManager.Application.Discovery;

namespace PictureManager.Worker.Discovery;

public sealed class ChannelDiscoveryQueue : IDiscoveryQueue
{
    private readonly Channel<QueuedDiscovery> _channel = Channel.CreateUnbounded<QueuedDiscovery>();

    public void Enqueue(QueuedDiscovery discovery) => _channel.Writer.TryWrite(discovery);

    public async IAsyncEnumerable<QueuedDiscovery> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var discovery in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return discovery;
        }
    }
}
