using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Images;

namespace PictureManager.Application.Repositories;

/// <summary>Read-side image queries. Every method applies the visibility rule (see phase 5 spec).</summary>
public interface IImageQueryRepository
{
    Task<IReadOnlyList<ImageRow>> ListAsync(
        ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take,
        CancellationToken cancellationToken = default);

    Task<ImageDetailRow?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumRef>> GetAlbumsContainingAsync(int imageId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Returns false when no visible image has this id.</summary>
    Task<bool> SetFavoriteAsync(int id, bool isFavorite, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetVisibleIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Visible images directly in the folder, ordered by (SortDate, Id) ascending.</summary>
    Task<IReadOnlyList<int>> GetVisibleIdsInFolderAsync(int folderId, CancellationToken cancellationToken = default);
}
