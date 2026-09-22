using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IScanService
{
    Task<int> StartScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default);
}
