using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Users;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AppUser?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default);
    Task<AppUser?> GetFirstActiveAdminAsync(CancellationToken cancellationToken = default);
    Task<bool> AnyActiveAdminWithPasswordAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(AppUser user, CancellationToken cancellationToken = default);

    /// <summary>Every account ordered by username, with the number of albums each owns.</summary>
    Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Active accounts except <paramref name="excludingUserId"/>, ordered by display name.</summary>
    Task<IReadOnlyList<DirectoryEntry>> ListActiveDirectoryAsync(int excludingUserId, CancellationToken cancellationToken = default);

    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default);

    Task<AppUser> AddAsync(AppUser user, CancellationToken cancellationToken = default);

    Task<int> CountAlbumsAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the user in one save. Their albums move to <paramref name="transferAlbumsToUserId"/> (names that clash
    /// there, ignoring case, get " (from {username})" appended) or, when null, are deleted with their entries.
    /// </summary>
    Task DeleteAsync(AppUser user, int? transferAlbumsToUserId, CancellationToken cancellationToken = default);
}
