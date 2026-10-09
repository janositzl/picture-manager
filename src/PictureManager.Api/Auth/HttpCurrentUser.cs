using System.Security.Claims;
using PictureManager.Application.Common;
using PictureManager.Model;

namespace PictureManager.Api.Auth;

/// <summary>Reads the caller from the request's claims. Only valid inside an authorized endpoint.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user
            : throw new InvalidOperationException("ICurrentUser used outside an authenticated request.");

    public int UserId => AuthClaims.GetUserId(Principal) ?? throw new InvalidOperationException("Session has no user id.");
    public bool IsAdmin => Principal.IsInRole(nameof(UserRole.Admin));
    public bool CanRunFolderActions => Principal.HasClaim(AuthClaims.FolderActions, "true");
}
