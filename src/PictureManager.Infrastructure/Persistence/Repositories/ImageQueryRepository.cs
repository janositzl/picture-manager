using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
        var query = _dbContext.Images.AsNoTracking().WhereVisible();

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

        query = ApplyKeyset(query, sort, direction, after);
        query = ApplyOrder(query, sort, direction);

        return await query.Take(take).Select(ImageProjections.ToRow).ToListAsync(cancellationToken);
    }

    public async Task<ImageDetailRow?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.AsNoTracking().WhereVisible()
            .Where(i => i.Id == id)
            .Select(i => new ImageDetailRow(
                new ImageRow(i.Id, i.FolderId, i.FileName, i.Extension, i.Width, i.Height, i.DateTaken, i.IsFavorite,
                    i.ContentHash, i.SortDate, i.FileName.ToLower()),
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
