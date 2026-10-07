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
    Task<IReadOnlyList<ImageRow>> ListAsync(
        ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take,
        CancellationToken cancellationToken = default);

    Task<ImageDetailRow?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlbumRef>> GetAlbumsContainingAsync(int imageId, int ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Returns false when no visible image has this id.</summary>
    /// <summary>Sets IsHidden on every existing image in `ids`; unknown or missing ids are skipped. Returns the rows changed.</summary>
    Task<int> SetHiddenAsync(IReadOnlyCollection<int> ids, bool isHidden, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Adds `degrees` (clockwise, a multiple of 90) to ThumbnailRotation, modulo 360, on every existing image in `ids`. Returns the rows changed.</summary>
    Task<int> RotateThumbnailsAsync(IReadOnlyCollection<int> ids, int degrees, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    Task<bool> SetFavoriteAsync(int id, bool isFavorite, DateTime updatedAtUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<int>> GetVisibleIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>Visible images directly in the folder, ordered by (SortDate, Id) ascending.</summary>
    Task<IReadOnlyList<int>> GetVisibleIdsInFolderAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>ContentHash groups with 2+ visible, hashed members; ordered by Count desc, ContentHash asc; after = keyset.</summary>
    Task<IReadOnlyList<DuplicateGroupKey>> GetDuplicateGroupsAsync(DuplicateGroupKey? after, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DuplicateMemberRow>> GetDuplicateMembersAsync(IReadOnlyCollection<string> contentHashes, CancellationToken cancellationToken = default);

    /// <summary>Visible images with a computed (non-empty) perceptual hash.</summary>
    Task<IReadOnlyList<PerceptualHashRow>> GetPerceptualHashesAsync(CancellationToken cancellationToken = default);

    /// <summary>Same projection as GetDuplicateMembersAsync, for visible images with the given ids.</summary>
    Task<IReadOnlyList<DuplicateMemberRow>> GetMembersByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
}
