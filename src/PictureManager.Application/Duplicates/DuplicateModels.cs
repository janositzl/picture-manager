using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PictureManager.Application.Images;

namespace PictureManager.Application.Duplicates;

public sealed record DuplicateGroupKey(string ContentHash, int Count);

public sealed record DuplicateMemberRow(ImageRow Image, string RootName, string RelativePath);

public sealed record DuplicateImageItem(
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
    string FolderPath);

/// <summary>Images sharing a ContentHash. Read-only review aid; the hash samples size + first/last 64KB.</summary>
public sealed record DuplicateGroup(string ContentHash, int Count, IReadOnlyList<DuplicateImageItem> Images);

public sealed record DuplicateCursor(
    [property: JsonPropertyName("c")] int Count,
    [property: JsonPropertyName("h")] string ContentHash);
