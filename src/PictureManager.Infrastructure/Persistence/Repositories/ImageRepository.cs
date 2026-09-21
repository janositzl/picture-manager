using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class ImageRepository : IImageRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public ImageRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Image?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Image>> GetByFolderIdAsync(int folderId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Images.Where(i => i.FolderId == folderId).ToListAsync(cancellationToken);
    }

    public async Task<Image> AddAsync(Image image, CancellationToken cancellationToken = default)
    {
        _dbContext.Images.Add(image);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return image;
    }
}
