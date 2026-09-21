using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
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
}
