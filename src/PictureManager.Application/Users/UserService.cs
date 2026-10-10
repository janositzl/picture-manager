using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Users;

public sealed partial class UserService(IAppUserRepository users, IPasswordHasher hasher, IClock clock, ICurrentUser caller) : IUserService
{
    public const int MaxUsernameLength = 64;
    public const int MaxDisplayNameLength = 200;

    [GeneratedRegex(@"^[A-Za-z0-9._@-]+$")]
    private static partial Regex UsernamePattern();

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await users.ListAsync(cancellationToken)).Select(row => ToDto(row.User, row.AlbumCount)).ToList();

    public async Task<Result<UserDto>> CreateAsync(UserCreateInput input, CancellationToken cancellationToken = default)
    {
        if (ValidateUsername(input.Username, out var username) is { } badUsername) return badUsername;
        if (ValidateDisplayName(input.DisplayName, out var displayName) is { } badName) return badName;
        if (ValidatePassword(input.Password, "password") is { } badPassword) return badPassword;
        if (ParseRole(input.Role, out var role) is { } badRole) return badRole;

        var normalized = AppUser.NormalizeUsername(username);
        if (await users.GetByNormalizedUsernameAsync(normalized, cancellationToken) is not null)
            return Result.Conflict($"A user named '{username}' already exists.");

        var user = await users.AddAsync(new AppUser
        {
            Username = username,
            NormalizedUsername = normalized,
            DisplayName = displayName,
            Role = role,
            PasswordHash = hasher.Hash(input.Password!),
            IsActive = true,
            MustChangePassword = true,
            CanRunFolderActions = input.CanRunFolderActions,
            CreatedAt = clock.UtcNow
        }, cancellationToken);
        return Result<UserDto>.Ok(ToDto(user, 0));
    }

    public async Task<Result<UserDto>> UpdateAsync(int id, UserUpdateInput input, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result.NotFound();

        var displayName = user.DisplayName;
        if (input.DisplayName is not null && ValidateDisplayName(input.DisplayName, out displayName) is { } badName) return badName;
        var role = user.Role;
        if (input.Role is not null && ParseRole(input.Role, out role) is { } badRole) return badRole;
        var isActive = input.IsActive ?? user.IsActive;
        var canRunFolderActions = input.CanRunFolderActions ?? user.CanRunFolderActions;

        if (id == caller.UserId && (!isActive || role != UserRole.Admin))
            return Result.Conflict("You can't disable or demote your own account.");
        var removesAnAdmin = user is { IsActive: true, Role: UserRole.Admin } && (!isActive || role != UserRole.Admin);
        if (removesAnAdmin && await users.CountActiveAdminsAsync(cancellationToken) <= 1)
            return Result.Conflict("At least one active administrator is required.");

        var stampChanged = role != user.Role || isActive != user.IsActive || canRunFolderActions != user.CanRunFolderActions;
        user.DisplayName = displayName;
        user.Role = role;
        user.IsActive = isActive;
        user.CanRunFolderActions = canRunFolderActions;
        if (stampChanged)
            user.SecurityStamp = Guid.NewGuid();
        await users.UpdateAsync(user, cancellationToken);
        return Result<UserDto>.Ok(ToDto(user, await users.CountAlbumsAsync(id, cancellationToken)));
    }

    public async Task<Result> ResetPasswordAsync(int id, string? newPassword, CancellationToken cancellationToken = default)
    {
        if (ValidatePassword(newPassword, "newPassword") is { } bad) return bad;
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result.NotFound();

        user.PasswordHash = hasher.Hash(newPassword!);
        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid();
        await users.UpdateAsync(user, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(int id, AlbumDisposition albums, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return Result.NotFound();
        if (id == caller.UserId)
            return Result.Conflict("You can't delete your own account.");
        if (user is { IsActive: true, Role: UserRole.Admin } && await users.CountActiveAdminsAsync(cancellationToken) <= 1)
            return Result.Conflict("At least one active administrator is required.");

        await users.DeleteAsync(user, albums == AlbumDisposition.Transfer ? caller.UserId : null, cancellationToken);
        return Result.Ok();
    }

    public Task<IReadOnlyList<DirectoryEntry>> DirectoryAsync(CancellationToken cancellationToken = default) =>
        users.ListActiveDirectoryAsync(caller.UserId, cancellationToken);

    private static UserDto ToDto(AppUser user, int albumCount) => new(
        user.Id, user.Username, user.DisplayName, user.Role.ToString(), user.IsActive, user.CanRunFolderActions,
        user.MustChangePassword, user.CreatedAt, user.LastLoginAt, albumCount);

    private static Result? ValidateUsername(string? raw, out string username)
    {
        username = raw?.Trim() ?? string.Empty;
        if (username.Length == 0) return Result.Invalid("username", "Username is required.");
        if (username.Length > MaxUsernameLength) return Result.Invalid("username", $"Must be at most {MaxUsernameLength} characters.");
        if (!UsernamePattern().IsMatch(username)) return Result.Invalid("username", "Use letters, digits, '.', '_', '-' or '@' only.");
        return null;
    }

    private static Result? ValidateDisplayName(string? raw, out string displayName)
    {
        displayName = raw?.Trim() ?? string.Empty;
        if (displayName.Length == 0) return Result.Invalid("displayName", "Display name is required.");
        if (displayName.Length > MaxDisplayNameLength) return Result.Invalid("displayName", $"Must be at most {MaxDisplayNameLength} characters.");
        return null;
    }

    private static Result? ValidatePassword(string? password, string field) =>
        password is null || password.Length < PasswordRules.MinLength
            ? Result.Invalid(field, $"Must be at least {PasswordRules.MinLength} characters.")
            : null;

    private static Result? ParseRole(string? raw, out UserRole role)
    {
        role = UserRole.User;
        // Enum.TryParse also accepts numbers ("1"); only the names are valid here.
        if (raw is not null && !int.TryParse(raw, out _) && Enum.TryParse(raw, ignoreCase: true, out UserRole parsed) && Enum.IsDefined(parsed))
        {
            role = parsed;
            return null;
        }
        return Result.Invalid("role", "Must be User or Admin.");
    }
}
