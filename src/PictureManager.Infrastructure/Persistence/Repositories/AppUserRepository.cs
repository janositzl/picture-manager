using System.Linq;
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

    public Task<AppUser?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default) =>
        _dbContext.AppUsers.FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, cancellationToken);

    public Task<AppUser?> GetFirstActiveAdminAsync(CancellationToken cancellationToken = default) =>
        _dbContext.AppUsers.Where(u => u.IsActive && u.Role == UserRole.Admin).OrderBy(u => u.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> AnyActiveAdminWithPasswordAsync(CancellationToken cancellationToken = default) =>
        _dbContext.AppUsers.AnyAsync(u => u.IsActive && u.Role == UserRole.Admin && u.PasswordHash != null, cancellationToken);

    public async Task UpdateAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        _dbContext.AppUsers.Update(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
