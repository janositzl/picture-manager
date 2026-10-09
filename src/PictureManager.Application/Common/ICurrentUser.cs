namespace PictureManager.Application.Common;

/// <summary>Who is calling: the signed-in user of the current HTTP request (HttpCurrentUser in the Api project).</summary>
public interface ICurrentUser
{
    int UserId { get; }
    bool IsAdmin { get; }

    /// <summary>True for admins, and for users an admin allowed to run folder actions.</summary>
    bool CanRunFolderActions { get; }
}
