using System;

namespace PictureManager.Application.Thumbnails;

public sealed class ThumbnailCacheOptions
{
    public string? RootPath { get; set; }
    public bool PreviewEnabled { get; set; } = true;
    /// <summary>How many thumbnails/previews may be decoded at once, across all requests.</summary>
    public int MaxConcurrentGenerations { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
}
