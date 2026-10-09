namespace PictureManager.Application.Users;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>False for a wrong password or an unreadable hash; never throws.</summary>
    bool Verify(string hash, string password);
}

public static class PasswordRules
{
    public const int MinLength = 8;
}
