using System.Collections.Generic;
using System.Threading;

namespace PictureManager.Application.Discovery;

public interface IDiscoveryQueue
{
    void Enqueue(QueuedDiscovery discovery);
    IAsyncEnumerable<QueuedDiscovery> ReadAllAsync(CancellationToken cancellationToken = default);
}
