using System;
using System.Collections.Generic;

namespace PictureManager.Model;

public class Image
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string? PerceptualHash { get; set; }
    public long FileSize { get; set; }
    public DateTime FileModified { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Orientation { get; set; }

    // Local-naive: EXIF DateTimeOriginal carries no timezone. See Global Constraints.
    public DateTime? DateTaken { get; set; }

    // Database-computed (stored generated column): COALESCE(DateTaken read as UTC wall-clock,
    // FileModified). The keyset key for date sorting. Never assign it from application code.
    public DateTime SortDate { get; set; }

    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? RawMetadata { get; set; }
    public bool IsFavorite { get; set; }

    /// <summary>User-hidden: out of every view and out of face recognition; only the folder view's "Show hidden" lists it.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Clockwise degrees (0, 90, 180 or 270) applied to the thumbnail on top of the EXIF orientation, for photos whose EXIF is wrong. The original file is never touched.</summary>
    public int ThumbnailRotation { get; set; }

    public IndexState IndexState { get; set; } = IndexState.Pending;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime? MissingSinceUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Folder? Folder { get; set; }
    public ICollection<AlbumImage> AlbumImages { get; set; } = new List<AlbumImage>();
}
