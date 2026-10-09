using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Users;

public interface IAdminBootstrapper
{
    /// <summary>Gives the first active admin the configured initial password when no admin can log in yet. True when it did.</summary>
    Task<bool> EnsureInitialAdminPasswordAsync(CancellationToken cancellationToken = default);
}
