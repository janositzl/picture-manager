using System;

namespace PictureManager.Application.Common;

/// <summary>
/// Derivative URLs carry ?v={ContentHash}: a changed file gets a new URL, which is what makes the
/// phase 4 `immutable` Cache-Control safe on these id-keyed routes. Null until enrichment hashed the file.
/// </summary>
public static class ImageUrls
{
    public static string? Thumbnail(int id, string contentHash) => Build(id, "thumbnail", contentHash);

    public static string? Preview(int id, string contentHash) => Build(id, "preview", contentHash);

    private static string? Build(int id, string kind, string contentHash) =>
        string.IsNullOrEmpty(contentHash) ? null : $"/api/images/{id}/{kind}?v={Uri.EscapeDataString(contentHash)}";
}
