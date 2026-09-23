using System;
using PictureManager.Application.Images;

namespace PictureManager.Application.Albums;

public sealed record AlbumSummaryRow(
    int Id,
    string Name,
    string? Description,
    int ImageCount,
    int? CoverImageId,
    string? CoverContentHash,
    DateTime UpdatedAt);

/// <summary>IsMissing = the image is not visible (missing on disk, or its folder/root is inactive).</summary>
public sealed record AlbumImageRow(ImageRow Image, int SortOrder, bool IsMissing);

public sealed record AlbumExportRow(string RootName, string? RootAlias, string RelativePath, string FileName, string Extension);
