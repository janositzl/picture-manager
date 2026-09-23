using System.Collections.Generic;
using System.Threading;

namespace PictureManager.Application.Scanning;

public interface IScanQueue
{
    void Enqueue(QueuedScan scan);
    IAsyncEnumerable<QueuedScan> ReadAllAsync(CancellationToken cancellationToken = default);
}
