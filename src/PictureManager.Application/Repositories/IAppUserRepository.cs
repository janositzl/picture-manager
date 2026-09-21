using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}
