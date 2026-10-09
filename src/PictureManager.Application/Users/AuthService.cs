using System;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Users;

public sealed class AuthService(IAppUserRepository users, IPasswordHasher hasher, IClock clock) : IAuthService
{
    public async Task<AuthenticatedUser?> LoginAsync(string? username, string? password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return null;

        var user = await users.GetByNormalizedUsernameAsync(AppUser.NormalizeUsername(username), cancellationToken);
        if (user is not { IsActive: true, PasswordHash: { } hash } || !hasher.Verify(hash, password))
            return null;

        user.LastLoginAt = clock.UtcNow;
        await users.UpdateAsync(user, cancellationToken);
        return AuthenticatedUser.From(user);
    }

    public async Task<Result<AuthenticatedUser>> ChangePasswordAsync(
        int userId, string? currentPassword, string? newPassword, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is not { IsActive: true })
            return Result.NotFound();

        if (user.PasswordHash is null || string.IsNullOrEmpty(currentPassword) || !hasher.Verify(user.PasswordHash, currentPassword))
            return Result.Invalid("currentPassword", "Current password is incorrect.");
        if (newPassword is null || newPassword.Length < PasswordRules.MinLength)
            return Result.Invalid("newPassword", $"Must be at least {PasswordRules.MinLength} characters.");
        if (newPassword == currentPassword)
            return Result.Invalid("newPassword", "Must differ from the current password.");

        user.PasswordHash = hasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();
        await users.UpdateAsync(user, cancellationToken);
        return Result<AuthenticatedUser>.Ok(AuthenticatedUser.From(user));
    }

    public async Task<AuthenticatedUser?> GetActiveUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is { IsActive: true } ? AuthenticatedUser.From(user) : null;
    }
}
