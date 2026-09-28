using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class ImageRootRepository : IImageRootRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public ImageRootRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ImageRoot?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ImageRoots.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ImageRoot>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ImageRoots.ToListAsync(cancellationToken);
    }

    public async Task<ImageRoot> AddAsync(ImageRoot imageRoot, CancellationToken cancellationToken = default)
    {
        _dbContext.ImageRoots.Add(imageRoot);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return imageRoot;
    }

    public async Task UpdateAsync(ImageRoot imageRoot, CancellationToken cancellationToken = default)
    {
        _dbContext.ImageRoots.Update(imageRoot);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await _dbContext.ImageRoots.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}
