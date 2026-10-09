using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Users;

public sealed class AdminBootstrapper(
    IAppUserRepository users, IPasswordHasher hasher, AuthOptions options, ILogger<AdminBootstrapper> logger) : IAdminBootstrapper
{
    public async Task<bool> EnsureInitialAdminPasswordAsync(CancellationToken cancellationToken = default)
    {
        if (await users.AnyActiveAdminWithPasswordAsync(cancellationToken))
            return false;

        var admin = await users.GetFirstActiveAdminAsync(cancellationToken);
        if (admin is null)
        {
            logger.LogError("No active admin account exists; nobody can log in");
            return false;
        }

        var password = options.InitialAdminPassword;
        if (string.IsNullOrWhiteSpace(password) || password.Length < PasswordRules.MinLength)
        {
            logger.LogError(
                "No admin can log in yet. Set Auth__InitialAdmin__Password (at least {MinLength} characters) and restart to enable the '{Username}' account",
                PasswordRules.MinLength, admin.Username);
            return false;
        }

        admin.PasswordHash = hasher.Hash(password);
        admin.MustChangePassword = true;
        admin.SecurityStamp = Guid.NewGuid();
        await users.UpdateAsync(admin, cancellationToken);
        logger.LogInformation("Initial password set for admin '{Username}'; it must be changed at first login", admin.Username);
        return true;
    }
}
