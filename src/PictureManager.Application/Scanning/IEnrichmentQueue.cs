using System.Collections.Generic;
using System.Threading;

namespace PictureManager.Application.Scanning;

public interface IEnrichmentQueue
{
    void Enqueue(int scanJobId, int imageId);
    IAsyncEnumerable<(int ScanJobId, int ImageId)> ReadAllAsync(CancellationToken cancellationToken = default);
}
