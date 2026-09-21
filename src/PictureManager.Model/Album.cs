using System;
using System.Collections.Generic;

namespace PictureManager.Model;

public class Album
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int OwnerUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public AppUser? OwnerUser { get; set; }
    public ICollection<AlbumImage> AlbumImages { get; set; } = new List<AlbumImage>();
}
