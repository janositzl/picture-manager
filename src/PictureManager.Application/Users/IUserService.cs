using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Users;

/// <summary>Admin operations on accounts. Callers evict the user's cached session afterwards (the Api layer owns that cache).</summary>
public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Conflict when the username is taken (ignoring case).</summary>
    Task<Result<UserDto>> CreateAsync(UserCreateInput input, CancellationToken cancellationToken = default);

    /// <summary>Conflict when it would disable or demote the caller, or remove the last active admin.</summary>
    Task<Result<UserDto>> UpdateAsync(int id, UserUpdateInput input, CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(int id, string? newPassword, CancellationToken cancellationToken = default);

    /// <summary>Conflict when it would delete the caller or the last active admin. Albums go to the caller or are deleted.</summary>
    Task<Result> DeleteAsync(int id, AlbumDisposition albums, CancellationToken cancellationToken = default);

    /// <summary>Active users other than the caller, for choosing who to share with.</summary>
    Task<IReadOnlyList<DirectoryEntry>> DirectoryAsync(CancellationToken cancellationToken = default);
}
