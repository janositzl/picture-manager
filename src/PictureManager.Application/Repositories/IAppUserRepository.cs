using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<AppUser?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default);
    Task<AppUser?> GetFirstActiveAdminAsync(CancellationToken cancellationToken = default);
    Task<bool> AnyActiveAdminWithPasswordAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(AppUser user, CancellationToken cancellationToken = default);
}
