using System;

namespace PictureManager.Application.Common;

/// <summary>
/// Derivative URLs carry ?v={ContentHash}: a changed file gets a new URL, which is what makes the
/// phase 4 `immutable` Cache-Control safe on these id-keyed routes. Null until enrichment hashed the file.
/// A rotated thumbnail appends the rotation to the version, so rotating changes the URL too.
/// </summary>
public static class ImageUrls
{
    public static string? Thumbnail(int id, string contentHash, int rotation = 0) =>
        Build(id, "thumbnail", contentHash, rotation);

    public static string? Preview(int id, string contentHash) => Build(id, "preview", contentHash);

    private static string? Build(int id, string kind, string contentHash, int rotation = 0)
    {
        if (string.IsNullOrEmpty(contentHash))
            return null;

        var version = rotation == 0 ? contentHash : $"{contentHash}-r{rotation}";
        return $"/api/images/{id}/{kind}?v={Uri.EscapeDataString(version)}";
    }
}
