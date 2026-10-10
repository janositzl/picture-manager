using System;
using PictureManager.Model;

namespace PictureManager.Application.Users;

/// <summary>What happens to a deleted user's albums.</summary>
public enum AlbumDisposition
{
    Transfer,
    Delete
}

public sealed record UserDto(
    int Id, string Username, string DisplayName, string Role, bool IsActive, bool CanRunFolderActions,
    bool MustChangePassword, DateTime CreatedAt, DateTime? LastLoginAt, int AlbumCount);

/// <summary>A user with the number of albums they own, as the repository lists them.</summary>
public sealed record UserRow(AppUser User, int AlbumCount);

/// <summary>An active user as offered by the share picker.</summary>
public sealed record DirectoryEntry(int Id, string DisplayName);

public sealed record UserCreateInput(string? Username, string? DisplayName, string? Password, string? Role, bool CanRunFolderActions);

/// <summary>Null means "leave unchanged".</summary>
public sealed record UserUpdateInput(string? DisplayName, string? Role, bool? IsActive, bool? CanRunFolderActions);
