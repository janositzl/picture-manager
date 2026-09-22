using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IImageEnrichmentService
{
    Task EnrichAsync(int imageId, CancellationToken cancellationToken = default);
}
