using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
}
