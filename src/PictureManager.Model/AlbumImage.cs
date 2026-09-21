using System;

namespace PictureManager.Model;

public class AlbumImage
{
    public int AlbumId { get; set; }
    public int ImageId { get; set; }
    public int SortOrder { get; set; }
    public DateTime AddedAt { get; set; }

    public Album? Album { get; set; }
    public Image? Image { get; set; }
}
