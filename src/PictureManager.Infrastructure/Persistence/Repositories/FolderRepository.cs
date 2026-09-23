using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
}
