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

    /// <summary>The owner's albums ordered by lower(Name); cover = first entry by SortOrder with a content hash.</summary>
    Task<IReadOnlyList<AlbumSummaryRow>> GetSummariesAsync(int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Tracked album if it exists AND belongs to the owner; otherwise null.</summary>
    Task<Album?> GetOwnedAsync(int id, int ownerUserId, CancellationToken cancellationToken = default);

    Task<int> CountImagesAsync(int albumId, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(int ownerUserId, string name, int? excludeAlbumId, CancellationToken cancellationToken = default);

    Task UpdateAsync(Album album, CancellationToken cancellationToken = default);

    Task DeleteAsync(Album album, CancellationToken cancellationToken = default);

    /// <summary>Album entries in (SortOrder, ImageId) order, after the given keyset when provided.</summary>
    Task<IReadOnlyList<AlbumImageRow>> ListImagesAsync(int albumId, int? afterSortOrder, int? afterImageId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetOrderedImageIdsAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>Adds entries after the current maximum SortOrder, in the given order. Ids must not already be in the album.</summary>
    Task AppendImagesAsync(int albumId, IReadOnlyList<int> imageIds, DateTime addedAtUtc, CancellationToken cancellationToken = default);

    Task RemoveImagesAsync(int albumId, IReadOnlyCollection<int> imageIds, CancellationToken cancellationToken = default);

    /// <summary>One statement: SortOrder = position (0..n-1) of each id in orderedImageIds.</summary>
    Task ReorderAsync(int albumId, IReadOnlyList<int> orderedImageIds, CancellationToken cancellationToken = default);

    Task TouchAsync(int albumId, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumExportRow>> GetExportRowsAsync(int albumId, CancellationToken cancellationToken = default);
}
