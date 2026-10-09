using Microsoft.Extensions.Caching.Memory;
using PictureManager.Application.Users;

namespace PictureManager.Api.Auth;

/// <summary>
/// Per-user snapshot the cookie is re-validated against, so a grid of thumbnails doesn't cost a query each.
/// Changes become visible within <see cref="Lifetime"/>; anything that changes a user calls <see cref="Evict"/>.
/// </summary>
public sealed class UserSessionCache(IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async Task<AuthenticatedUser?> GetAsync(int userId, Func<Task<AuthenticatedUser?>> load)
    {
        if (cache.TryGetValue(Key(userId), out AuthenticatedUser? cached))
            return cached;

        var user = await load();
        cache.Set(Key(userId), user, Lifetime);
        return user;
    }

    public void Evict(int userId) => cache.Remove(Key(userId));

    private static string Key(int userId) => "pm:session:" + userId;
}
