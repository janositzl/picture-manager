using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using PictureManager.Application.Common;

namespace PictureManager.Application.Images;

public sealed record ImageListItem(
    int Id,
    int FolderId,
    string FileName,
    string Extension,
    int? Width,
    int? Height,
    DateTime? DateTaken,
    bool IsFavorite,
    string? ThumbnailUrl,
    string? PreviewUrl)
{
    public static ImageListItem From(ImageRow row) => new(
        row.Id, row.FolderId, row.FileName, row.Extension, row.Width, row.Height, row.DateTaken, row.IsFavorite,
        ImageUrls.Thumbnail(row.Id, row.ContentHash), ImageUrls.Preview(row.Id, row.ContentHash));
}

public sealed record ImageDetail(
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
    long FileSize,
    DateTime FileModified,
    int? Orientation,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    double? Latitude,
    double? Longitude,
    JsonElement? RawMetadata,
    string FolderPath,
    IReadOnlyList<AlbumRef> Albums);

public sealed record ImageListRequest(
    int? FolderId = null,
    string? Folder = null,
    string? FileName = null,
    bool FavoritesOnly = false,
    string? Sort = null,
    string? Order = null,
    string? Cursor = null,
    int? Limit = null);

/// <summary>Cursor payload. Key = SortDate ticks (date sort) or the DB's lower(FileName) (name sort).</summary>
public sealed record ImageCursor(
    [property: JsonPropertyName("s")] string Sort,
    [property: JsonPropertyName("o")] string Order,
    [property: JsonPropertyName("k")] string Key,
    [property: JsonPropertyName("i")] int Id);
