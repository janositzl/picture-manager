using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using PictureManager.Application.Images;

namespace PictureManager.Application.Duplicates;

public sealed record DuplicateGroupKey(string ContentHash, int Count);

public sealed record DuplicateMemberRow(ImageRow Image, string RootName, string RelativePath, long FileSize);

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
    string FolderPath,
    long FileSize);

/// <summary>Images sharing a ContentHash. Read-only review aid; the hash samples size + first/last 64KB.</summary>
public sealed record DuplicateGroup(string ContentHash, int Count, IReadOnlyList<DuplicateImageItem> Images);

public sealed record DuplicateCursor(
    [property: JsonPropertyName("c")] int Count,
    [property: JsonPropertyName("h")] string ContentHash);

public sealed record PerceptualHashRow(int Id, string PerceptualHash);

/// <summary>Visually similar images (resized / re-encoded copies). Images are ordered best copy first.</summary>
public sealed record SimilarGroup(string Key, int Count, int MaxDistance, IReadOnlyList<DuplicateImageItem> Images);

public sealed record SimilarCursor([property: JsonPropertyName("o")] int Offset);
