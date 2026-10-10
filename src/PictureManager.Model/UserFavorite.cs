using System;

namespace PictureManager.Model;

/// <summary>One user's favorite photo. Favorites are per user; the image itself carries no flag.</summary>
public class UserFavorite
{
    public int UserId { get; set; }
    public int ImageId { get; set; }
    public DateTime CreatedAt { get; set; }

    public AppUser? User { get; set; }
    public Image? Image { get; set; }
}
