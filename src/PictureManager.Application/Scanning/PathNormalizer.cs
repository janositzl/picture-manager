using System;
using System.Text;

namespace PictureManager.Application.Scanning;

public static class PathNormalizer
{
    public static string Normalize(string value) => value.Normalize(NormalizationForm.FormC);

    public static string Combine(string basePath, string segment)
    {
        var normalizedSegment = Normalize(segment);
        return string.IsNullOrEmpty(basePath) ? normalizedSegment : $"{basePath}/{normalizedSegment}";
    }

    public static bool NormalizedEquals(string? a, string? b) =>
        string.Equals(Normalize(a ?? string.Empty), Normalize(b ?? string.Empty), StringComparison.OrdinalIgnoreCase);
}
