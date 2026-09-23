using System;
using PictureManager.Model;

namespace PictureManager.Tests.Support;

/// <summary>Terse builders for Postgres-backed tests. Add the returned graph to a context and save.</summary>
public static class TestData
{
    public static readonly DateTime Utc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static ImageRoot Root(string name, string? alias = null, bool isActive = true) => new()
    {
        Name = name,
        Alias = alias,
        MountPath = "/mnt/" + name,
        IsActive = isActive,
        CreatedUtc = Utc
    };

    public static Folder Folder(ImageRoot root, string relativePath, Folder? parent = null, bool isActive = true, DateTime? missingSinceUtc = null) => new()
    {
        Root = root,
        Parent = parent,
        Name = relativePath.Length == 0 ? root.Name : relativePath[(relativePath.LastIndexOf('/') + 1)..],
        RelativePath = relativePath,
        IsActive = isActive,
        MissingSinceUtc = missingSinceUtc,
        CreatedUtc = Utc,
        ModifiedUtc = Utc
    };

    /// <param name="dateTaken">Local-naive camera time; must have DateTimeKind.Unspecified.</param>
    /// <param name="contentHash">Defaults to a unique hash so tests never create accidental duplicates.</param>
    public static Image Image(
        Folder folder,
        string fileName,
        string extension = ".jpg",
        DateTime? dateTaken = null,
        DateTime? fileModified = null,
        string? contentHash = null,
        bool isFavorite = false,
        DateTime? missingSinceUtc = null) => new()
    {
        Folder = folder,
        FileName = fileName,
        Extension = extension,
        ContentHash = contentHash ?? Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
        FileSize = 1234,
        FileModified = fileModified ?? Utc,
        DateTaken = dateTaken,
        IsFavorite = isFavorite,
        IndexState = IndexState.Indexed,
        FirstSeenUtc = Utc,
        MissingSinceUtc = missingSinceUtc,
        CreatedAt = Utc,
        UpdatedAt = Utc
    };

    public static Album Album(string name, int ownerUserId = AppUser.SystemUserId) => new()
    {
        Name = name,
        OwnerUserId = ownerUserId,
        CreatedAt = Utc,
        UpdatedAt = Utc
    };

    public static AlbumImage AlbumImage(Album album, Image image, int sortOrder) => new()
    {
        Album = album,
        Image = image,
        SortOrder = sortOrder,
        AddedAt = Utc
    };
}
