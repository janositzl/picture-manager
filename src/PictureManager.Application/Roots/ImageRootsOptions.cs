using System.Collections.Generic;

namespace PictureManager.Application.Roots;

/// <summary>One entry of the "ImageRoots" configuration array.</summary>
public sealed class ImageRootConfigEntry
{
    public string? Name { get; set; }
    public string? MountPath { get; set; }
    public string? Alias { get; set; }
}

public sealed class ImageRootsOptions
{
    public List<ImageRootConfigEntry> Entries { get; set; } = new();
}
