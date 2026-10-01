using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using PictureManager.Application.Faces;

namespace PictureManager.Worker.Faces;

public sealed class ChannelFaceRecognitionQueue : IFaceRecognitionQueue
{
    private readonly Channel<QueuedFaceRecognition> _channel = Channel.CreateUnbounded<QueuedFaceRecognition>();

    public void Enqueue(QueuedFaceRecognition item) => _channel.Writer.TryWrite(item);

    public async IAsyncEnumerable<QueuedFaceRecognition> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }
}
