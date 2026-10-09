using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PictureManager.Application.Users;

namespace PictureManager.Api.Auth;

/// <summary>Rejects a cookie whose user was disabled or whose security stamp moved on (password/role/permission change).</summary>
public static class CookieSessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var userId = principal is null ? null : AuthClaims.GetUserId(principal);
        if (userId is not int id)
        {
            await RejectAsync(context);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var current = await services.GetRequiredService<UserSessionCache>().GetAsync(
            id, () => services.GetRequiredService<IAuthService>().GetActiveUserAsync(id, context.HttpContext.RequestAborted));

        if (current is null || principal!.FindFirst(AuthClaims.SecurityStamp)?.Value != current.SecurityStamp.ToString())
            await RejectAsync(context);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
