using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Application.Users;
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

    private const int MaxAlbumNameLength = 300;

    public async Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.AppUsers.AsNoTracking()
            .OrderBy(u => u.NormalizedUsername)
            .Select(u => new UserRow(u, u.Albums.Count))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DirectoryEntry>> ListActiveDirectoryAsync(int excludingUserId, CancellationToken cancellationToken = default) =>
        await _dbContext.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && u.Id != excludingUserId)
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Id)
            .Select(u => new DirectoryEntry(u.Id, u.DisplayName))
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default) =>
        _dbContext.AppUsers.CountAsync(u => u.IsActive && u.Role == UserRole.Admin, cancellationToken);

    public async Task<AppUser> AddAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        _dbContext.AppUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public Task<int> CountAlbumsAsync(int userId, CancellationToken cancellationToken = default) =>
        _dbContext.Albums.CountAsync(a => a.OwnerUserId == userId, cancellationToken);

    public async Task DeleteAsync(AppUser user, int? transferAlbumsToUserId, CancellationToken cancellationToken = default)
    {
        var albums = await _dbContext.Albums.Where(a => a.OwnerUserId == user.Id).ToListAsync(cancellationToken);
        if (transferAlbumsToUserId is int target)
        {
            var taken = (await _dbContext.Albums.Where(a => a.OwnerUserId == target).Select(a => a.Name).ToListAsync(cancellationToken))
                .Select(name => name.ToLowerInvariant()).ToHashSet();
            foreach (var album in albums.OrderBy(a => a.Id))
            {
                var name = taken.Contains(album.Name.ToLowerInvariant()) ? ClashFreeName(album.Name, user.Username, taken) : album.Name;
                taken.Add(name.ToLowerInvariant());
                album.Name = name;
                album.OwnerUserId = target;
            }
        }
        else
        {
            // The database cascades the albums' entries (AlbumImages) with them.
            _dbContext.Albums.RemoveRange(albums);
        }

        _dbContext.AppUsers.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string ClashFreeName(string name, string username, HashSet<string> taken)
    {
        var suffix = $" (from {username})";
        // Leave room for a counter so the result still fits the column.
        var stemLength = MaxAlbumNameLength - suffix.Length - 10;
        var stem = name.Length > stemLength ? name[..stemLength] : name;
        var candidate = stem + suffix;
        for (var n = 2; taken.Contains(candidate.ToLowerInvariant()); n++)
            candidate = $"{stem}{suffix} {n}";
        return candidate;
    }
}
