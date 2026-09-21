using System;

namespace PictureManager.Model;

public class ImageRoot
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
}
