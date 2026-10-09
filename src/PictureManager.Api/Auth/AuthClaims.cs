using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using PictureManager.Application.Users;

namespace PictureManager.Api.Auth;

public static class AuthClaims
{
    public const string SecurityStamp = "pm:stamp";
    public const string MustChangePassword = "pm:mcp";
    public const string FolderActions = "pm:fa";

    public static ClaimsPrincipal CreatePrincipal(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(SecurityStamp, user.SecurityStamp.ToString())
        };
        if (user.MustChangePassword)
            claims.Add(new Claim(MustChangePassword, "true"));
        if (user.CanRunFolderActions)
            claims.Add(new Claim(FolderActions, "true"));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    public static int? GetUserId(ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
}
