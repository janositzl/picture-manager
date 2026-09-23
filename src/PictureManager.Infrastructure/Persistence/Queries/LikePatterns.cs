namespace PictureManager.Infrastructure.Persistence.Queries;

internal static class LikePatterns
{
    // Npgsql's EF.Functions.ILike(match, pattern) two-argument overload translates to
    // "ILIKE @pattern ESCAPE ''", which disables escaping entirely (verified against the generated
    // SQL). The three-argument overload lets us pin the escape character explicitly, so callers must
    // pass EscapeCharacter alongside a pattern built by Contains.
    public const string EscapeCharacter = @"\";

    public static string Contains(string term) =>
        "%" + term.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
}
