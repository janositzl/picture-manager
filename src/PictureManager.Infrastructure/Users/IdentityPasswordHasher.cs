using System;
using Microsoft.AspNetCore.Identity;
using PictureManager.Model;
using AppPasswordHasher = PictureManager.Application.Users.IPasswordHasher;

namespace PictureManager.Infrastructure.Users;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (the format also records its own iteration count).</summary>
public sealed class IdentityPasswordHasher : AppPasswordHasher
{
    private static readonly AppUser NoUser = new();
    private readonly PasswordHasher<AppUser> _inner = new();

    public string Hash(string password) => _inner.HashPassword(NoUser, password);

    public bool Verify(string hash, string password)
    {
        try
        {
            return _inner.VerifyHashedPassword(NoUser, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
