using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Duplicates;
using PictureManager.Application.Images;

namespace PictureManager.Application.Repositories;

/// <summary>Read-side image queries. Every method applies the visibility rule (see phase 5 spec).</summary>
public interface IImageQueryRepository
{
    /// <summary>IsFavorite on the rows (and FavoritesOnly) is <paramref name="userId"/>'s own favorite.</summary>
    Task<IReadOnlyList<ImageRow>> ListAsync(
        int userId, ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take,
        CancellationToken cancellationToken = default);

    Task<ImageDetailRow?> GetVisibleDetailAsync(int id, int userId, CancellationToken cancellationToken = default);

    /// <summary>Albums containing the image that <paramref name="userId"/> owns or has been shared, ordered by name.</summary>
    Task<IReadOnlyList<AlbumRefRow>> GetAlbumsContainingAsync(int imageId, int userId, CancellationToken cancellationToken = default);

    /// <summary>Sets IsHidden on every existing image in `ids`; unknown or missing ids are skipped. Returns the rows changed.</summary>
    Task<int> SetHiddenAsync(IReadOnlyCollection<int> ids, bool isHidden, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Adds `degrees` (clockwise, a multiple of 90) to ThumbnailRotation, modulo 360, on every existing image in `ids`. Returns the rows changed.</summary>
    Task<int> RotateThumbnailsAsync(IReadOnlyCollection<int> ids, int degrees, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Adds or removes <paramref name="userId"/>'s favorite; idempotent. False when no visible image has this id.</summary>
    Task<bool> SetFavoriteAsync(int id, int userId, bool isFavorite, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetVisibleIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Visible images directly in the folder, ordered by (SortDate, Id) ascending.</summary>
    Task<IReadOnlyList<int>> GetVisibleIdsInFolderAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>ContentHash groups with 2+ visible, hashed members; ordered by Count desc, ContentHash asc; after = keyset.</summary>
    Task<IReadOnlyList<DuplicateGroupKey>> GetDuplicateGroupsAsync(DuplicateGroupKey? after, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DuplicateMemberRow>> GetDuplicateMembersAsync(IReadOnlyCollection<string> contentHashes, int userId, CancellationToken cancellationToken = default);

    /// <summary>Visible images with a computed (non-empty) perceptual hash.</summary>
    Task<IReadOnlyList<PerceptualHashRow>> GetPerceptualHashesAsync(CancellationToken cancellationToken = default);

    /// <summary>Same projection as GetDuplicateMembersAsync, for visible images with the given ids.</summary>
    Task<IReadOnlyList<DuplicateMemberRow>> GetMembersByIdsAsync(IReadOnlyCollection<int> ids, int userId, CancellationToken cancellationToken = default);
}
