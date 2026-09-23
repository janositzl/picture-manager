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

    Task<Result<AlbumExport>> ExportAsync(int id, string? prefix, CancellationToken cancellationToken = default);
}
