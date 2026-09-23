namespace PictureManager.Application.Thumbnails;

public sealed class ThumbnailCacheOptions
{
    public string? RootPath { get; set; }
    public bool PreviewEnabled { get; set; } = true;
}
