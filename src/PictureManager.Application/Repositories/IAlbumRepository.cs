using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Albums;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAlbumRepository
{
    Task<Album?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Album> AddAsync(Album album, CancellationToken cancellationToken = default);

    /// <summary>
    /// Albums <paramref name="userId"/> owns or has been shared, ordered by lower(Name); cover = the chosen CoverImageId while it is
    /// still in the album with a content hash, otherwise the first entry by SortOrder with a content hash.
    /// </summary>
    Task<IReadOnlyList<AlbumSummaryRow>> GetSummariesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>The album (tracked) with the caller's access, or null when it doesn't exist or isn't theirs or shared with them.</summary>
    Task<AccessibleAlbum?> GetAccessibleAsync(int id, int userId, CancellationToken cancellationToken = default);

    /// <summary>The album's shares, ordered by the user's display name.</summary>
    Task<IReadOnlyList<AlbumShareRow>> GetSharesAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>Adds the share, or changes its permission when the user already has one.</summary>
    Task SetShareAsync(int albumId, int userId, SharePermission permission, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>False when the user had no share on the album.</summary>
    Task<bool> RemoveShareAsync(int albumId, int userId, CancellationToken cancellationToken = default);

    Task<int> CountImagesAsync(int albumId, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(int ownerUserId, string name, int? excludeAlbumId, CancellationToken cancellationToken = default);

    Task UpdateAsync(Album album, CancellationToken cancellationToken = default);

    Task DeleteAsync(Album album, CancellationToken cancellationToken = default);

    /// <summary>Album entries in (SortOrder, ImageId) order, after the given keyset when provided. IsFavorite is <paramref name="userId"/>'s own favorite.</summary>
    Task<IReadOnlyList<AlbumImageRow>> ListImagesAsync(int albumId, int userId, int? afterSortOrder, int? afterImageId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetOrderedImageIdsAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>Image ids sorted by the key (ties broken by ImageId), ignoring the stored SortOrder.</summary>
    Task<IReadOnlyList<int>> GetImageIdsSortedAsync(int albumId, AlbumSortKey key, CancellationToken cancellationToken = default);

    /// <summary>Adds entries after the current maximum SortOrder, in the given order. Ids must not already be in the album.</summary>
    Task AppendImagesAsync(int albumId, IReadOnlyList<int> imageIds, DateTime addedAtUtc, CancellationToken cancellationToken = default);

    Task RemoveImagesAsync(int albumId, IReadOnlyCollection<int> imageIds, CancellationToken cancellationToken = default);

    /// <summary>One statement: SortOrder = position (0..n-1) of each id in orderedImageIds.</summary>
    Task ReorderAsync(int albumId, IReadOnlyList<int> orderedImageIds, CancellationToken cancellationToken = default);

    Task TouchAsync(int albumId, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Stores the album's chosen cover image; the caller checks that the image is in the album.</summary>
    Task SetCoverAsync(int albumId, int imageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumExportRow>> GetExportRowsAsync(int albumId, CancellationToken cancellationToken = default);
}
