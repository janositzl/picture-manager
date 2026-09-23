using PictureManager.Model;

namespace PictureManager.Application.Roots;

/// <summary>
/// A root's Name and Alias each end up as one path segment in album exports (Alias ?? Name), so both
/// must be valid single segments. Callers trim before validating.
/// </summary>
public static class RootNameRules
{
    public const int MaxLength = 200;

    /// <returns>An error message, or null when the value is valid.</returns>
    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Must not be blank.";
        if (value.Length > MaxLength)
            return $"Must be at most {MaxLength} characters.";
        if (value.IndexOfAny(new[] { '/', '\\' }) >= 0)
            return "Must not contain '/' or '\\'.";
        return null;
    }

    public static string ExportSegment(ImageRoot root) => root.Alias ?? root.Name;
}
