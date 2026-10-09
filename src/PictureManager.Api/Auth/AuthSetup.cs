using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using PictureManager.Application.Common;
using PictureManager.Application.Users;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;

namespace PictureManager.Api.Auth;

public static class AuthPolicies
{
    /// <summary>Any valid session, including one that must change its password first (/api/auth/password).</summary>
    public const string SignedIn = "SignedIn";
    public const string Active = "Active";
    public const string AdminOnly = "AdminOnly";
    public const string FolderActions = "FolderActions";
    public const string LoginRateLimit = "login";
    public const string PasswordRateLimit = "password";
}

public static class AuthSetup
{
    public static IServiceCollection AddPictureManagerAuth(this IServiceCollection services, AuthOptions options)
    {
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddSingleton<UserSessionCache>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddDataProtection()
            .SetApplicationName("PictureManager")
            .PersistKeysToDbContext<PictureManagerDbContext>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.Cookie.Name = "pm.auth";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Strict;
                // Home LAN deployments are often plain http; behind TLS the cookie is Secure automatically.
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.ExpireTimeSpan = TimeSpan.FromDays(14);
                cookie.SlidingExpiration = true;
                // An API: answer with a status code, never a redirect to a login page.
                cookie.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                cookie.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                cookie.Events.OnValidatePrincipal = CookieSessionValidator.ValidateAsync;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.SignedIn, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.Active, policy => policy.RequireAuthenticatedUser().RequireAssertion(HasNoPendingPasswordChange))
            .AddPolicy(AuthPolicies.AdminOnly, policy => policy.RequireAuthenticatedUser().RequireAssertion(HasNoPendingPasswordChange)
                .RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.FolderActions, policy => policy.RequireAuthenticatedUser().RequireAssertion(HasNoPendingPasswordChange)
                .RequireClaim(AuthClaims.FolderActions, "true"));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(AuthPolicies.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = options.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
            // Password changes verify the current password, so they are guessable too: limit per signed-in user.
            limiter.AddPolicy(AuthPolicies.PasswordRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                AuthClaims.GetUserId(http.User)?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = options.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    private static bool HasNoPendingPasswordChange(AuthorizationHandlerContext context) =>
        !context.User.HasClaim(AuthClaims.MustChangePassword, "true");
}
