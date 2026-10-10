using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Albums;

/// <summary>All operations are scoped to ICurrentUser; another owner's album behaves as unknown.</summary>
public interface IAlbumService
{
    Task<IReadOnlyList<AlbumSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<AlbumDetail>> CreateAsync(AlbumCreate input, CancellationToken cancellationToken = default);

    Task<Result<AlbumDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<AlbumDetail>> UpdateAsync(int id, AlbumUpdate input, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<AlbumImageItem>>> ListImagesAsync(int id, string? cursor, int? limit, CancellationToken cancellationToken = default);

    Task<Result<AlbumAddResult>> AddImagesAsync(int id, AlbumAddImages input, CancellationToken cancellationToken = default);

    Task<Result> RemoveImagesAsync(int id, IReadOnlyList<int>? imageIds, CancellationToken cancellationToken = default);

    Task<Result> MoveImageAsync(int id, int imageId, int? afterImageId, CancellationToken cancellationToken = default);

    /// <summary>Rewrites the album's stored order to the given sort: "dateAsc", "dateDesc" or "name".</summary>
    Task<Result> SortAsync(int id, string? by, CancellationToken cancellationToken = default);

    /// <summary>Chooses the album's cover; the image must be in the album.</summary>
    Task<Result> SetCoverAsync(int id, int? imageId, CancellationToken cancellationToken = default);

    Task<Result<AlbumExport>> ExportAsync(int id, string? prefix, CancellationToken cancellationToken = default);

    /// <summary>Owner only.</summary>
    Task<Result<IReadOnlyList<AlbumShareDto>>> GetSharesAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Owner only. Adds the share or changes its permission. Invalid for yourself, an inactive or unknown user, or a permission other than Viewer/Editor.</summary>
    Task<Result<AlbumShareDto>> SetShareAsync(int id, int userId, string? permission, CancellationToken cancellationToken = default);

    /// <summary>The owner may remove anyone's share; anyone else only their own (leaving the album).</summary>
    Task<Result> RemoveShareAsync(int id, int userId, CancellationToken cancellationToken = default);
}
