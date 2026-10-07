using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Thumbnails;

public interface IThumbnailService
{
    /// <summary>
    /// Returns the absolute path to a cached WebP derivative of the source image at the requested size,
    /// generating and caching it first if it doesn't already exist. Returns null if the source file cannot
    /// be decoded as an image (never throws for that case). A filesystem error while writing the cache
    /// propagates as an exception -- it is not mapped to null.
    /// </summary>
    Task<string?> GetOrCreateDerivativePathAsync(
        string contentHash,
        string sourcePath,
        int? orientation,
        DerivativeSize size,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every cached derivative of the content hash. Best effort: files that are missing or
    /// can't be deleted are skipped, and nothing is thrown for them.
    /// </summary>
    void DeleteDerivatives(string contentHash);
}
