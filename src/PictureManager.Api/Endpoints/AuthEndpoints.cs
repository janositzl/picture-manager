using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Api.Auth;
using PictureManager.Application.Common;
using PictureManager.Application.Users;

namespace PictureManager.Api.Endpoints;

public sealed record MeDto(int Id, string Username, string DisplayName, string Role, bool MustChangePassword, bool CanRunFolderActions)
{
    public static MeDto From(AuthenticatedUser user) =>
        new(user.Id, user.Username, user.DisplayName, user.Role.ToString(), user.MustChangePassword, user.CanRunFolderActions);
}

public static class AuthEndpoints
{
    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder auth)
    {
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(AuthPolicies.LoginRateLimit);
        auth.MapPost("/logout", LogoutAsync);
        auth.MapGet("/me", MeAsync);
        auth.MapPost("/password", ChangePasswordAsync).RequireAuthorization(AuthPolicies.SignedIn).RequireRateLimiting(AuthPolicies.PasswordRateLimit);
    }

    public static async Task<Results<Ok<MeDto>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, HttpContext http, IAuthService service, CancellationToken cancellationToken)
    {
        var user = await service.LoginAsync(request.Username, request.Password, cancellationToken);
        if (user is null)
            return TypedResults.Problem(title: "Invalid username or password.", statusCode: StatusCodes.Status401Unauthorized);

        await SignInAsync(http, user);
        return TypedResults.Ok(MeDto.From(user));
    }

    public static async Task<NoContent> LogoutAsync(HttpContext http)
    {
        // The cookie handler starts the response while expiring the cookie, so the status has to be set first.
        http.Response.StatusCode = StatusCodes.Status204NoContent;
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> MeAsync(
        HttpContext http, IAuthService service, CancellationToken cancellationToken)
    {
        if (AuthClaims.GetUserId(http.User) is not int id || await service.GetActiveUserAsync(id, cancellationToken) is not { } user)
            return TypedResults.Unauthorized();
        return TypedResults.Ok(MeDto.From(user));
    }

    public static async Task<Results<Ok<MeDto>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext http, ICurrentUser currentUser, IAuthService service,
        UserSessionCache sessions, CancellationToken cancellationToken)
    {
        var result = await service.ChangePasswordAsync(currentUser.UserId, request.CurrentPassword, request.NewPassword, cancellationToken);
        if (!result.IsSuccess)
            return new Result<MeDto>(result.Status, null, result.Errors, result.Message).ToOk();

        // The stamp rotated: re-issue this browser's cookie (other sessions of the user stop validating).
        sessions.Evict(result.Value!.Id);
        await SignInAsync(http, result.Value);
        return Result<MeDto>.Ok(MeDto.From(result.Value)).ToOk();
    }

    private static Task SignInAsync(HttpContext http, AuthenticatedUser user) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthClaims.CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = true });
}
