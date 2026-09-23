using System;
using System.Collections.Generic;

namespace PictureManager.Application.Settings;

public static class SettingsNormalizer
{
    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>Trims, rejects blanks and path separators, de-duplicates case-insensitively (first spelling wins).</summary>
    public static bool TryNormalizeFolderNames(IEnumerable<string?>? values, out List<string> normalized, out string? error)
    {
        normalized = new List<string>();
        error = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values ?? Array.Empty<string?>())
        {
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                error = "Values must not be blank.";
                return false;
            }
            if (value.IndexOfAny(Separators) >= 0)
            {
                error = $"'{value}' must not contain '/' or '\\'.";
                return false;
            }
            if (seen.Add(value))
                normalized.Add(value);
        }

        return true;
    }

    /// <summary>Trims, lowercases, adds the leading '.', rejects blanks/"."/separators, de-duplicates.</summary>
    public static bool TryNormalizeExtensions(IEnumerable<string?>? values, out List<string> normalized, out string? error)
    {
        normalized = new List<string>();
        error = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in values ?? Array.Empty<string?>())
        {
            var value = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
            {
                error = "Values must not be blank.";
                return false;
            }
            if (!value.StartsWith('.'))
                value = "." + value;
            if (value == ".")
            {
                error = "'.' is not an extension.";
                return false;
            }
            if (value.IndexOfAny(Separators) >= 0)
            {
                error = $"'{value}' must not contain '/' or '\\'.";
                return false;
            }
            if (seen.Add(value))
                normalized.Add(value);
        }

        return true;
    }
}
