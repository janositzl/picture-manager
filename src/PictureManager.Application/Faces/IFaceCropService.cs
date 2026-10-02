using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Faces;

public interface IFaceCropService
{
    /// <summary>Path to a cached JPEG crop of the face, created on first request. Null if the face or its image is gone.</summary>
    Task<string?> GetOrCreateCropPathAsync(int faceId, CancellationToken cancellationToken = default);
}
