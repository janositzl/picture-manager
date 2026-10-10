using PictureManager.Model;

namespace PictureManager.Application.Albums;

/// <summary>What the caller may do with an album. Ordered: each level includes everything before it.</summary>
public enum AlbumAccess
{
    Viewer,
    Editor,
    Owner
}

public static class AlbumAccessRules
{
    public static AlbumAccess From(bool isOwner, bool isEditor) =>
        isOwner ? AlbumAccess.Owner : isEditor ? AlbumAccess.Editor : AlbumAccess.Viewer;
}

/// <summary>An album the caller can reach, how, and whose it is. Album is tracked.</summary>
public sealed record AccessibleAlbum(Album Album, AlbumAccess Access, string OwnerDisplayName);

public sealed record AlbumShareRow(int UserId, string DisplayName, SharePermission Permission);
