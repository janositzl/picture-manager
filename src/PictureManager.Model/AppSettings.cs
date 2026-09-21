using System.Collections.Generic;

namespace PictureManager.Model;

public class AppSettings
{
    public int Id { get; set; }
    public List<string> ExcludedFolderNames { get; set; } = new();
    public List<string> ExcludedExtensions { get; set; } = new();
    public List<string>? IncludedExtensions { get; set; }
}
