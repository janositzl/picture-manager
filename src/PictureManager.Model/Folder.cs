using System;
using System.Collections.Generic;

namespace PictureManager.Model;

public class Folder
{
    public int Id { get; set; }
    public int RootId { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Set when the scanner finds the folder gone from disk (with its whole subtree); cleared when it's back.</summary>
    public DateTime? MissingSinceUtc { get; set; }

    /// <summary>When a discovery last diffed this folder's subfolders; null = never discovered.</summary>
    public DateTime? ChildrenDiscoveredAt { get; set; }

    /// <summary>The directory's last-write time as of the last discovery; for a later "possibly outdated" hint.</summary>
    public DateTime? LastWriteTimeUtc { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public ImageRoot? Root { get; set; }
    public Folder? Parent { get; set; }
    public ICollection<Folder> Children { get; set; } = new List<Folder>();
    public ICollection<Image> Images { get; set; } = new List<Image>();
}
