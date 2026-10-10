using System;

namespace PictureManager.Model;

public enum SharePermission
{
    Viewer,
    Editor
}

/// <summary>An album its owner shared with another user. Viewers browse and export; editors also change the photos.</summary>
public class AlbumShare
{
    public int AlbumId { get; set; }
    public int UserId { get; set; }
    public SharePermission Permission { get; set; }
    public DateTime CreatedAt { get; set; }

    public Album? Album { get; set; }
    public AppUser? User { get; set; }
}
