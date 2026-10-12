namespace PictureManager.Api;

/// <summary>
/// The optional "PathBase" setting: the sub-path the app is served under behind a reverse proxy (e.g. /pm).
/// Accepts it with or without leading/trailing slashes; unset, blank or "/" means the site root.
/// </summary>
public static class PathBaseSetting
{
    public static PathString Parse(string? value)
    {
        var trimmed = value?.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? PathString.Empty : new PathString("/" + trimmed);
    }
}
