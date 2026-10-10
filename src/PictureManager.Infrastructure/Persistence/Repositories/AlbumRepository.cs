using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Albums;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class AlbumRepository : IAlbumRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public AlbumRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Album?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Albums.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Album>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Albums.ToListAsync(cancellationToken);
    }

    public async Task<Album> AddAsync(Album album, CancellationToken cancellationToken = default)
    {
        _dbContext.Albums.Add(album);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return album;
    }

    public async Task<IReadOnlyList<AlbumSummaryRow>> GetSummariesAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Albums.AsNoTracking()
            .Where(a => a.OwnerUserId == userId || a.Shares.Any(s => s.UserId == userId))
            .OrderBy(a => a.Name.ToLower()).ThenBy(a => a.Id)
            .Select(a => new AlbumSummaryRow(
                a.Id,
                a.Name,
                a.Description,
                a.AlbumImages.Count(),
                a.AlbumImages.Where(ai => ai.Image!.ContentHash != "")
                    .OrderByDescending(ai => ai.ImageId == a.CoverImageId).ThenBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
                    .Select(ai => (int?)ai.ImageId).FirstOrDefault(),
                a.AlbumImages.Where(ai => ai.Image!.ContentHash != "")
                    .OrderByDescending(ai => ai.ImageId == a.CoverImageId).ThenBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
                    .Select(ai => ai.Image!.ContentHash).FirstOrDefault(),
                a.UpdatedAt,
                a.OwnerUserId == userId,
                a.Shares.Any(s => s.UserId == userId && s.Permission == SharePermission.Editor),
                a.OwnerUser!.DisplayName,
                a.Shares.Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<AccessibleAlbum?> GetAccessibleAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Albums
            .Where(a => a.Id == id && (a.OwnerUserId == userId || a.Shares.Any(s => s.UserId == userId)))
            .Select(a => new
            {
                Album = a,
                OwnerDisplayName = a.OwnerUser!.DisplayName,
                IsEditor = a.Shares.Any(s => s.UserId == userId && s.Permission == SharePermission.Editor)
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new AccessibleAlbum(row.Album, AlbumAccessRules.From(row.Album.OwnerUserId == userId, row.IsEditor), row.OwnerDisplayName);
    }

    public async Task<IReadOnlyList<AlbumShareRow>> GetSharesAsync(int albumId, CancellationToken cancellationToken = default) =>
        await _dbContext.AlbumShares.AsNoTracking()
            .Where(s => s.AlbumId == albumId)
            .OrderBy(s => s.User!.DisplayName).ThenBy(s => s.UserId)
            .Select(s => new AlbumShareRow(s.UserId, s.User!.DisplayName, s.Permission))
            .ToListAsync(cancellationToken);

    public async Task SetShareAsync(int albumId, int userId, SharePermission permission, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var share = await _dbContext.AlbumShares.FirstOrDefaultAsync(s => s.AlbumId == albumId && s.UserId == userId, cancellationToken);
        if (share is null)
            _dbContext.AlbumShares.Add(new AlbumShare { AlbumId = albumId, UserId = userId, Permission = permission, CreatedAt = nowUtc });
        else
            share.Permission = permission;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveShareAsync(int albumId, int userId, CancellationToken cancellationToken = default) =>
        await _dbContext.AlbumShares.Where(s => s.AlbumId == albumId && s.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<int> CountImagesAsync(int albumId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.CountAsync(ai => ai.AlbumId == albumId, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(int ownerUserId, string name, int? excludeAlbumId, CancellationToken cancellationToken = default)
    {
        var lowered = name.ToLower();
        return await _dbContext.Albums.AnyAsync(
            a => a.OwnerUserId == ownerUserId && a.Name.ToLower() == lowered && (excludeAlbumId == null || a.Id != excludeAlbumId),
            cancellationToken);
    }

    public async Task UpdateAsync(Album album, CancellationToken cancellationToken = default)
    {
        _dbContext.Albums.Update(album);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Album album, CancellationToken cancellationToken = default)
    {
        _dbContext.Albums.Remove(album);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumImageRow>> ListImagesAsync(
        int albumId, int userId, int? afterSortOrder, int? afterImageId, int take, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AlbumImages.AsNoTracking().Where(ai => ai.AlbumId == albumId);
        if (afterSortOrder is int sortOrder && afterImageId is int imageId)
            query = query.Where(ai => ai.SortOrder > sortOrder || (ai.SortOrder == sortOrder && ai.ImageId > imageId));

        return await query
            .OrderBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
            .Take(take)
            .Select(ai => new AlbumImageRow(
                new ImageRow(ai.Image!.Id, ai.Image.FolderId, ai.Image.FileName, ai.Image.Extension, ai.Image.Width,
                    ai.Image.Height, ai.Image.DateTaken, ai.Image.Favorites.Any(f => f.UserId == userId), ai.Image.ContentHash, ai.Image.SortDate,
                    ai.Image.FileName.ToLower(), ai.Image.Folder!.Root!.Name, ai.Image.Folder.RelativePath, IndexState.Indexed, null, false, ai.Image.ThumbnailRotation),
                ai.SortOrder,
                ai.Image.IsHidden || ai.Image.MissingSinceUtc != null || !ai.Image.Folder!.IsActive || ai.Image.Folder.MissingSinceUtc != null || !ai.Image.Folder.Root!.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetOrderedImageIdsAsync(int albumId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.AsNoTracking()
            .Where(ai => ai.AlbumId == albumId)
            .OrderBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
            .Select(ai => ai.ImageId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetImageIdsSortedAsync(int albumId, AlbumSortKey key, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AlbumImages.AsNoTracking().Where(ai => ai.AlbumId == albumId);
        var ordered = key switch
        {
            AlbumSortKey.DateAscending => query.OrderBy(ai => ai.Image!.SortDate),
            AlbumSortKey.DateDescending => query.OrderByDescending(ai => ai.Image!.SortDate),
            _ => query.OrderBy(ai => ai.Image!.FileName.ToLower()),
        };
        return await ordered.ThenBy(ai => ai.ImageId).Select(ai => ai.ImageId).ToListAsync(cancellationToken);
    }

    public async Task AppendImagesAsync(int albumId, IReadOnlyList<int> imageIds, DateTime addedAtUtc, CancellationToken cancellationToken = default)
    {
        var maxSortOrder = await _dbContext.AlbumImages
            .Where(ai => ai.AlbumId == albumId)
            .MaxAsync(ai => (int?)ai.SortOrder, cancellationToken) ?? -1;

        for (var i = 0; i < imageIds.Count; i++)
        {
            _dbContext.AlbumImages.Add(new AlbumImage
            {
                AlbumId = albumId,
                ImageId = imageIds[i],
                SortOrder = maxSortOrder + 1 + i,
                AddedAt = addedAtUtc
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveImagesAsync(int albumId, IReadOnlyCollection<int> imageIds, CancellationToken cancellationToken = default)
    {
        var ids = imageIds.ToList();
        await _dbContext.AlbumImages
            .Where(ai => ai.AlbumId == albumId && ids.Contains(ai.ImageId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ReorderAsync(int albumId, IReadOnlyList<int> orderedImageIds, CancellationToken cancellationToken = default)
    {
        var ids = orderedImageIds.ToArray();
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "AlbumImages" AS ai
            SET "SortOrder" = o.ord - 1
            FROM unnest({ids}) WITH ORDINALITY AS o(image_id, ord)
            WHERE ai."AlbumId" = {albumId} AND ai."ImageId" = o.image_id
            """, cancellationToken);
    }

    public async Task TouchAsync(int albumId, DateTime updatedAtUtc, CancellationToken cancellationToken = default)
    {
        await _dbContext.Albums.Where(a => a.Id == albumId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UpdatedAt, updatedAtUtc), cancellationToken);
    }

    public async Task SetCoverAsync(int albumId, int imageId, CancellationToken cancellationToken = default)
    {
        await _dbContext.Albums.Where(a => a.Id == albumId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CoverImageId, imageId), cancellationToken);
    }

    public async Task<IReadOnlyList<AlbumExportRow>> GetExportRowsAsync(int albumId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AlbumImages.AsNoTracking()
            .Where(ai => ai.AlbumId == albumId)
            .OrderBy(ai => ai.SortOrder).ThenBy(ai => ai.ImageId)
            .Select(ai => new AlbumExportRow(
                ai.Image!.Folder!.Root!.Name,
                ai.Image.Folder.Root.Alias,
                ai.Image.Folder.RelativePath,
                ai.Image.FileName,
                ai.Image.Extension))
            .ToListAsync(cancellationToken);
    }
}
