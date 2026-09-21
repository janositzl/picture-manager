using System.Collections.Generic;

namespace PictureManager.Model;

public class AppUser
{
    public int Id { get; set; }
    public string? ZitadelSubjectId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;

    public ICollection<Album> Albums { get; set; } = new List<Album>();
}
