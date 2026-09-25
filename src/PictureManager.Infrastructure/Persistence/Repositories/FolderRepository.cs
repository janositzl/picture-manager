using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Folders;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Queries;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class FolderRepository : IFolderRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public FolderRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Folder?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Folder>> GetChildrenAsync(int? parentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.Where(f => f.ParentId == parentId).ToListAsync(cancellationToken);
    }

    public async Task<Folder> AddAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        _dbContext.Folders.Add(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return folder;
    }

    public async Task<Folder?> GetByRootAndRelativePathAsync(int rootId, string relativePath, CancellationToken cancellationToken = default)
    {
        var normalized = relativePath.ToLowerInvariant();
        return await _dbContext.Folders.FirstOrDefaultAsync(
            f => f.RootId == rootId && f.RelativePath.ToLower() == normalized, cancellationToken);
    }

    public async Task UpdateAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        _dbContext.Folders.Update(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsVisibleAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.AsNoTracking().WhereVisible().AnyAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<FolderNode>> GetVisibleRootFoldersAsync(CancellationToken cancellationToken = default)
    {
        return await ToNodes(_dbContext.Folders.AsNoTracking().WhereVisible().Where(f => f.ParentId == null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FolderNode>> GetVisibleChildrenAsync(int parentId, CancellationToken cancellationToken = default)
    {
        return await ToNodes(_dbContext.Folders.AsNoTracking().WhereVisible().Where(f => f.ParentId == parentId))
            .ToListAsync(cancellationToken);
    }

    public async Task<FolderDetail?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var folder = await _dbContext.Folders.AsNoTracking().WhereVisible()
            .Where(f => f.Id == id)
            .Select(f => new
            {
                f.Id,
                f.Name,
                f.RootId,
                RootName = f.Root!.Name,
                f.RelativePath,
                ImageCount = f.Images.Count(i => i.MissingSinceUtc == null),
                IsMissing = f.MissingSinceUtc != null
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (folder is null)
            return null;

        var ancestorPaths = AncestorPaths(folder.RelativePath);
        var crumbs = await _dbContext.Folders.AsNoTracking()
            .Where(f => f.RootId == folder.RootId && ancestorPaths.Contains(f.RelativePath))
            .Select(f => new { f.Id, f.Name, f.RelativePath })
            .ToListAsync(cancellationToken);

        var breadcrumb = crumbs
            .OrderBy(c => c.RelativePath.Length)
            .Select(c => new BreadcrumbItem(c.Id, c.Name))
            .ToList();

        return new FolderDetail(folder.Id, folder.Name, folder.RootId, folder.RootName, folder.RelativePath,
            folder.ImageCount, folder.IsMissing, breadcrumb);
    }

    public async Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.AsNoTracking()
            .Where(f => !f.IsActive)
            .OrderBy(f => f.Root!.Name).ThenBy(f => f.RelativePath)
            .Select(f => new RemovedFolder(f.Id, f.Name, f.Root!.Name, f.RelativePath))
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveFromCollectionAsync(int folderId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The folder's own images go explicitly (the folder row survives as a tombstone). Everything
        // beneath goes by deleting the direct children: the database cascades Folder.ParentId ->
        // deeper folders, Image.FolderId -> their images, and AlbumImage.ImageId -> album entries.
        await _dbContext.Images.Where(i => i.FolderId == folderId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Folders.Where(f => f.ParentId == folderId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.Folders.Where(f => f.Id == folderId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.IsActive, false), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteSubtreeAsync(int folderId, CancellationToken cancellationToken = default)
    {
        await _dbContext.Folders.Where(f => f.Id == folderId).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task MarkSubtreeMissingAsync(int folderId, DateTime missingSinceUtc, CancellationToken cancellationToken = default)
    {
        // One statement for the whole subtree: image visibility checks each folder's own flag, not its
        // ancestors', so every descendant must carry the mark.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            WITH RECURSIVE subtree AS (
                SELECT "Id" FROM "Folders" WHERE "Id" = {folderId}
                UNION ALL
                SELECT f."Id" FROM "Folders" f JOIN subtree s ON f."ParentId" = s."Id"
            )
            UPDATE "Folders" SET "MissingSinceUtc" = {missingSinceUtc}
            WHERE "Id" IN (SELECT "Id" FROM subtree) AND "MissingSinceUtc" IS NULL AND "IsActive"
            """, cancellationToken);
    }

    public async Task RenameRootFolderAsync(int rootId, string name, CancellationToken cancellationToken = default)
    {
        await _dbContext.Folders.Where(f => f.RootId == rootId && f.ParentId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.Name, name), cancellationToken);
    }

    public async Task<bool> HasUndiscoveredFoldersAsync(int rootId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Folders.AsNoTracking()
            .AnyAsync(f => f.RootId == rootId && f.IsActive && f.ChildrenDiscoveredAt == null, cancellationToken);
    }

    public async Task SetScanStatusAsync(int folderId, FolderScanStatus status, CancellationToken cancellationToken = default)
    {
        await _dbContext.Folders.Where(f => f.Id == folderId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ScanStatus, status), cancellationToken);
    }

    public async Task MarkSubtreeScannedAsync(int folderId, DateTime scannedAtUtc, CancellationToken cancellationToken = default)
    {
        // One statement for the whole subtree, each folder stamped with its own current present-image
        // count (a correlated subquery per row, same shape as MarkSubtreeMissingAsync's subtree CTE).
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            WITH RECURSIVE subtree AS (
                SELECT "Id" FROM "Folders" WHERE "Id" = {folderId}
                UNION ALL
                SELECT f."Id" FROM "Folders" f JOIN subtree s ON f."ParentId" = s."Id"
            )
            UPDATE "Folders" SET
                "ScanStatus" = {(int)FolderScanStatus.Idle},
                "LastScannedAt" = {scannedAtUtc},
                "LastScanFileCount" = (
                    SELECT COUNT(*) FROM "Images" i
                    WHERE i."FolderId" = "Folders"."Id" AND i."MissingSinceUtc" IS NULL
                )
            WHERE "Id" IN (SELECT "Id" FROM subtree) AND "IsActive"
            """, cancellationToken);
    }

    private static IQueryable<FolderNode> ToNodes(IQueryable<Folder> folders) =>
        folders
            .OrderBy(f => f.Name.ToLower()).ThenBy(f => f.Id)
            .Select(f => new FolderNode(
                f.Id,
                f.Name,
                f.Children.Any(c => c.IsActive),
                f.Images.Count(i => i.MissingSinceUtc == null),
                f.MissingSinceUtc != null));

    // "", "a", "a/b" for "a/b": the root's top folder plus every ancestor and the folder itself.
    private static List<string> AncestorPaths(string relativePath)
    {
        var paths = new List<string> { string.Empty };
        if (relativePath.Length == 0)
            return paths;

        var segments = relativePath.Split('/');
        for (var i = 1; i <= segments.Length; i++)
            paths.Add(string.Join('/', segments, 0, i));
        return paths;
    }
}
