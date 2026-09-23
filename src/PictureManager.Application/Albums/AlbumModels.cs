using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PictureManager.Application.Common;

namespace PictureManager.Application.Albums;

public sealed record AlbumSummary(int Id, string Name, string? Description, int ImageCount, string? CoverThumbnailUrl, DateTime UpdatedAt);

public sealed record AlbumDetail(int Id, string Name, string? Description, int ImageCount, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>The ImageListItem shape plus IsMissing. Missing entries stay listed with null URLs.</summary>
public sealed record AlbumImageItem(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string? ThumbnailUrl,
    string? PreviewUrl,
    bool IsMissing)
{
    public static AlbumImageItem From(AlbumImageRow row)
    {
        var image = row.Image;
        return new AlbumImageItem(
            image.Id, image.FolderId, image.FileName, image.Extension, image.Width, image.Height, image.DateTaken,
            image.IsFavorite,
            row.IsMissing ? null : ImageUrls.Thumbnail(image.Id, image.ContentHash),
            row.IsMissing ? null : ImageUrls.Preview(image.Id, image.ContentHash),
            row.IsMissing);
    }
}

public sealed record AlbumCreate(string? Name, string? Description);

/// <summary>Name null = unchanged. DescriptionSpecified distinguishes "clear" (null) from "unchanged".</summary>
public sealed record AlbumUpdate(string? Name, bool DescriptionSpecified, string? Description);

/// <summary>Exactly one of ImageIds / FolderId.</summary>
public sealed record AlbumAddImages(IReadOnlyList<int>? ImageIds, int? FolderId);

public sealed record AlbumAddResult(int Added, int Skipped);

public sealed record AlbumExport(string FileName, string Content);

public sealed record AlbumImageCursor(
    [property: JsonPropertyName("s")] int SortOrder,
    [property: JsonPropertyName("i")] int ImageId);
