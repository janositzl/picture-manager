using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

public sealed class ChannelScanQueue : IScanQueue
{
    private readonly Channel<QueuedScan> _channel = Channel.CreateUnbounded<QueuedScan>();

    public void Enqueue(QueuedScan scan) => _channel.Writer.TryWrite(scan);

    public async IAsyncEnumerable<QueuedScan> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var scan in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return scan;
        }
    }
}
