using System.Collections.Generic;

namespace PictureManager.Model;

public class AppUser
{
    // v1 placeholder owner seeded by the initial migration; every album belongs to it until v2 auth.
    public const int SystemUserId = 1;

    public int Id { get; set; }
    public string? ZitadelSubjectId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;

    public ICollection<Album> Albums { get; set; } = new List<Album>();
}
