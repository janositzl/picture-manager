using PictureManager.Model;

namespace PictureManager.Application.Common;

public sealed class SystemCurrentUser : ICurrentUser
{
    public int UserId => AppUser.InitialAdminId;
}
