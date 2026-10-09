using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Users;

public interface IAuthService
{
    /// <summary>Null for any failure: unknown, inactive, no password or wrong password.</summary>
    Task<AuthenticatedUser?> LoginAsync(string? username, string? password, CancellationToken cancellationToken = default);

    Task<Result<AuthenticatedUser>> ChangePasswordAsync(int userId, string? currentPassword, string? newPassword, CancellationToken cancellationToken = default);

    /// <summary>The user's current snapshot, or null when the account is gone or disabled.</summary>
    Task<AuthenticatedUser?> GetActiveUserAsync(int userId, CancellationToken cancellationToken = default);
}
