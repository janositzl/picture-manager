using System;
using System.Collections.Generic;

namespace PictureManager.Model;

public class AppUser
{
    /// <summary>The account the initial migration seeds; it owns every album created before user accounts existed.</summary>
    public const int InitialAdminId = 1;

    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>Lower-cased <see cref="Username"/>; the unique lookup key, so "Bob" and "bob" are one account.</summary>
    public string NormalizedUsername { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>Null until a password is set; such an account cannot log in.</summary>
    public string? PasswordHash { get; set; }

    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }

    /// <summary>May start scans, discovery and face recognition and exclude/remove folders. Admins always may.</summary>
    public bool CanRunFolderActions { get; set; }

    /// <summary>Changes whenever the password, role, active flag or folder-actions permission changes; older cookies stop validating.</summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public ICollection<Album> Albums { get; set; } = new List<Album>();

    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();
}
