using System;
using PictureManager.Model;

namespace PictureManager.Application.Users;

/// <summary>What the API knows about a signed-in user; also what the session cookie is validated against.</summary>
public sealed record AuthenticatedUser(
    int Id, string Username, string DisplayName, UserRole Role,
    bool MustChangePassword, bool CanRunFolderActions, Guid SecurityStamp)
{
    public bool IsAdmin => Role == UserRole.Admin;

    public static AuthenticatedUser From(AppUser user) => new(
        user.Id, user.Username, user.DisplayName, user.Role, user.MustChangePassword,
        user.Role == UserRole.Admin || user.CanRunFolderActions, user.SecurityStamp);
}
