namespace PictureManager.Application.Common;

/// <summary>
/// Who is calling. The v2 auth seam: v1 always answers the seeded system user; v2 swaps only this
/// implementation to read the authenticated principal.
/// </summary>
public interface ICurrentUser
{
    int UserId { get; }
}
