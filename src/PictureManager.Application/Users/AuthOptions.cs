namespace PictureManager.Application.Users;

/// <summary>Bound in Program.cs from the "Auth" configuration section.</summary>
public sealed class AuthOptions
{
    /// <summary>Auth:InitialAdmin:Password — applied only while no active admin has a password.</summary>
    public string? InitialAdminPassword { get; init; }

    public int LoginAttemptsPerMinute { get; init; } = 10;
}
