using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

public sealed class ChannelEnrichmentQueue : IEnrichmentQueue
{
    private readonly Channel<(int ScanJobId, int ImageId)> _channel = Channel.CreateUnbounded<(int, int)>();

    public void Enqueue(int scanJobId, int imageId) => _channel.Writer.TryWrite((scanJobId, imageId));

    public async IAsyncEnumerable<(int ScanJobId, int ImageId)> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
