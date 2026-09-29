using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IImageRepository
{
    Task<Image?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Image>> GetByFolderIdAsync(int folderId, CancellationToken cancellationToken = default);
    Task<Image> AddAsync(Image image, CancellationToken cancellationToken = default);
    Task<Image?> GetByFolderAndFileNameAsync(int folderId, string fileName, string extension, CancellationToken cancellationToken = default);
    Task<Image?> GetByIdWithFolderAsync(int id, CancellationToken cancellationToken = default);
    /// <summary>Ids of images still Pending (never enriched) whose folder is active and that aren't
    /// already known missing -- orphaned by a scan whose in-memory enrichment queue was lost (e.g. a
    /// restart mid-Enriching).</summary>
    Task<IReadOnlyList<int>> GetPendingImageIdsAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns a MISSING (MissingSinceUtc != null) image with this content hash, if any -- used for move detection. Never returns an active (non-missing) image, even if one shares the hash.</summary>
    Task<Image?> GetMissingByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);
    Task UpdateAsync(Image image, CancellationToken cancellationToken = default);
    Task DeleteAsync(Image image, CancellationToken cancellationToken = default);
}
