using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Duplicates;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Queries;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class ImageQueryRepository : IImageQueryRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public ImageQueryRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ImageRow>> ListAsync(
        ImageListFilter filter, ImageSort sort, SortDirection direction, ImageKeyset? after, int take,
        CancellationToken cancellationToken = default)
    {
        // Hidden images are listed only for one folder's "Show hidden" view.
        var query = filter.IncludeHidden && filter.FolderId is not null
            ? _dbContext.Images.AsNoTracking().WhereExisting()
            : _dbContext.Images.AsNoTracking().WhereVisible();

        if (filter.FolderId is int folderId)
            query = query.Where(i => i.FolderId == folderId);

        if (!string.IsNullOrWhiteSpace(filter.FolderName))
        {
            var pattern = LikePatterns.Contains(filter.FolderName);
            query = query.Where(i => EF.Functions.ILike(i.Folder!.Name, pattern, LikePatterns.EscapeCharacter));
        }

        if (!string.IsNullOrWhiteSpace(filter.FileName))
        {
            var pattern = LikePatterns.Contains(filter.FileName);
            query = query.Where(i => EF.Functions.ILike(i.FileName, pattern, LikePatterns.EscapeCharacter));
        }

        if (filter.FavoritesOnly)
            query = query.Where(i => i.IsFavorite);

        if (filter.PersonId is int personId)
        {
            query = filter.PersonState switch
            {
                PersonFaceState.Confirmed => query.Where(i => _dbContext.Faces.Any(f => f.ImageId == i.Id && f.PersonId == personId
                    && f.AssignmentState == FaceAssignmentState.Confirmed)),
                PersonFaceState.Suggested => query.Where(i => _dbContext.Faces.Any(f => f.ImageId == i.Id && f.PersonId == personId
                        && f.AssignmentState == FaceAssignmentState.Suggested)
                    && !_dbContext.Faces.Any(f => f.ImageId == i.Id && f.PersonId == personId
                        && f.AssignmentState == FaceAssignmentState.Confirmed)),
                _ => query.Where(i => _dbContext.Faces.Any(f => f.ImageId == i.Id && f.PersonId == personId
                    && (f.AssignmentState == FaceAssignmentState.Suggested || f.AssignmentState == FaceAssignmentState.Confirmed))),
            };
        }

        if (filter.HasFaces is bool hasFaces)
        {
            query = hasFaces
                ? query.Where(i => _dbContext.Faces.Any(f => f.ImageId == i.Id && f.AssignmentState != FaceAssignmentState.Ignored))
                : query.Where(i => !_dbContext.Faces.Any(f => f.ImageId == i.Id && f.AssignmentState != FaceAssignmentState.Ignored));
        }

        query = ApplyKeyset(query, sort, direction, after);
        query = ApplyOrder(query, sort, direction);

        var rows = await query.Take(take).Select(ImageProjections.ToRow).ToListAsync(cancellationToken);
        if (filter.PersonId is int forPerson && rows.Count > 0)
            rows = await WithFaceIdsAsync(rows, forPerson, filter.PersonState, cancellationToken);
        return rows;
    }

    // Per photo, the person's best face for the state being listed (so a face crop can stand in for the thumbnail).
    private async Task<List<ImageRow>> WithFaceIdsAsync(List<ImageRow> rows, int personId, PersonFaceState? state, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var faces = _dbContext.Faces.AsNoTracking().Where(f => ids.Contains(f.ImageId) && f.PersonId == personId);
        faces = state switch
        {
            PersonFaceState.Confirmed => faces.Where(f => f.AssignmentState == FaceAssignmentState.Confirmed),
            PersonFaceState.Suggested => faces.Where(f => f.AssignmentState == FaceAssignmentState.Suggested),
            _ => faces.Where(f => f.AssignmentState == FaceAssignmentState.Suggested || f.AssignmentState == FaceAssignmentState.Confirmed),
        };
        var best = (await faces.Select(f => new { f.ImageId, f.Id, f.QualityScore, Confirmed = f.AssignmentState == FaceAssignmentState.Confirmed })
                .ToListAsync(cancellationToken))
            .GroupBy(f => f.ImageId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Confirmed).ThenByDescending(f => f.QualityScore).ThenBy(f => f.Id).First().Id);
        return rows.Select(r => best.TryGetValue(r.Id, out var faceId) ? r with { FaceId = faceId } : r).ToList();
    }

    public async Task<ImageDetailRow?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.AsNoTracking().WhereExisting()
            .Where(i => i.Id == id)
            .Select(i => new ImageDetailRow(
                new ImageRow(i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
                    i.ContentHash, i.SortDate, i.FileName.ToLower(), i.Folder!.Root!.Name, i.Folder.RelativePath, i.IndexState, null, i.IsHidden),
                i.FileSize, i.FileModified, i.Orientation, i.CameraMake, i.CameraModel, i.LensModel,
                i.Latitude, i.Longitude, i.RawMetadata, i.Folder!.Root!.Name, i.Folder.RelativePath))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumRef>> GetAlbumsContainingAsync(int imageId, int ownerUserId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.AsNoTracking()
            .Where(ai => ai.ImageId == imageId && ai.Album!.OwnerUserId == ownerUserId)
            .OrderBy(ai => ai.Album!.Name.ToLower()).ThenBy(ai => ai.AlbumId)
            .Select(ai => new AlbumRef(ai.AlbumId, ai.Album!.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> SetHiddenAsync(IReadOnlyCollection<int> ids, bool isHidden, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await _dbContext.Images.WhereExisting()
            .Where(i => idList.Contains(i.Id) && i.IsHidden != isHidden)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.IsHidden, isHidden)
                .SetProperty(i => i.UpdatedAt, updatedAtUtc),
                cancellationToken);
    }

    public async Task<bool> SetFavoriteAsync(int id, bool isFavorite, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        var updated = await _dbContext.Images.WhereVisible()
            .Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.IsFavorite, isFavorite)
                .SetProperty(i => i.UpdatedAt, updatedAtUtc),
                cancellationToken);
        return updated > 0;
    }

    public async Task<IReadOnlyList<int>> GetVisibleIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => idList.Contains(i.Id))
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetVisibleIdsInFolderAsync(int folderId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => i.FolderId == folderId)
            .OrderBy(i => i.SortDate).ThenBy(i => i.Id)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateGroupKey>> GetDuplicateGroupsAsync(DuplicateGroupKey? after, int take, CancellationToken cancellationToken = default)
    {
        var groups = _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => i.ContentHash != "")
            .GroupBy(i => i.ContentHash)
            .Where(g => g.Count() > 1)
            .Select(g => new { ContentHash = g.Key, Count = g.Count() });

        if (after is not null)
        {
            var afterCount = after.Count;
            var afterHash = after.ContentHash;
            groups = groups.Where(g => g.Count < afterCount || (g.Count == afterCount && string.Compare(g.ContentHash, afterHash) > 0));
        }

        return await groups
            .OrderByDescending(g => g.Count).ThenBy(g => g.ContentHash)
            .Take(take)
            .Select(g => new DuplicateGroupKey(g.ContentHash, g.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DuplicateMemberRow>> GetDuplicateMembersAsync(IReadOnlyCollection<string> contentHashes, CancellationToken cancellationToken = default)
    {
        var hashes = contentHashes.ToList();
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => hashes.Contains(i.ContentHash))
            .Select(MemberProjection)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PerceptualHashRow>> GetPerceptualHashesAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => i.PerceptualHash != null && i.PerceptualHash != "")
            .Select(i => new PerceptualHashRow(i.Id, i.PerceptualHash!))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DuplicateMemberRow>> GetMembersByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => idList.Contains(i.Id))
            .Select(MemberProjection)
            .ToListAsync(cancellationToken);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Image, DuplicateMemberRow>> MemberProjection = i => new DuplicateMemberRow(
        new ImageRow(i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
            i.ContentHash, i.SortDate, i.FileName.ToLower(), i.Folder!.Root!.Name, i.Folder.RelativePath, i.IndexState, null, i.IsHidden),
        i.Folder!.Root!.Name,
        i.Folder.RelativePath,
        i.FileSize);

    private static IQueryable<Image> ApplyKeyset(IQueryable<Image> query, ImageSort sort, SortDirection direction, ImageKeyset? after)
    {
        if (after is null)
            return query;

        var id = after.Id;
        if (sort == ImageSort.Date)
        {
            var date = after.SortDate ?? throw new ArgumentException("A date keyset needs SortDate.", nameof(after));
            return direction == SortDirection.Desc
                ? query.Where(i => i.SortDate < date || (i.SortDate == date && i.Id < id))
                : query.Where(i => i.SortDate > date || (i.SortDate == date && i.Id > id));
        }

        var name = after.SortName ?? throw new ArgumentException("A name keyset needs SortName.", nameof(after));
        return direction == SortDirection.Desc
            ? query.Where(i => string.Compare(i.FileName.ToLower(), name) < 0 || (i.FileName.ToLower() == name && i.Id < id))
            : query.Where(i => string.Compare(i.FileName.ToLower(), name) > 0 || (i.FileName.ToLower() == name && i.Id > id));
    }

    private static IQueryable<Image> ApplyOrder(IQueryable<Image> query, ImageSort sort, SortDirection direction) =>
        (sort, direction) switch
        {
            (ImageSort.Date, SortDirection.Desc) => query.OrderByDescending(i => i.SortDate).ThenByDescending(i => i.Id),
            (ImageSort.Date, SortDirection.Asc) => query.OrderBy(i => i.SortDate).ThenBy(i => i.Id),
            (ImageSort.Name, SortDirection.Desc) => query.OrderByDescending(i => i.FileName.ToLower()).ThenByDescending(i => i.Id),
            _ => query.OrderBy(i => i.FileName.ToLower()).ThenBy(i => i.Id)
        };
}
