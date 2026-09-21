using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class AppUserRepository : IAppUserRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public AppUserRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AppUser?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }
}
