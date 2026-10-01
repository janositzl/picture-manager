using System.Collections.Generic;
using System.Threading;

namespace PictureManager.Application.Faces;

public interface IFaceRecognitionQueue
{
    void Enqueue(QueuedFaceRecognition item);
    IAsyncEnumerable<QueuedFaceRecognition> ReadAllAsync(CancellationToken cancellationToken = default);
}
