using System;
using System.IO;

namespace PictureManager.Application.Thumbnails;

public static class ThumbnailCachePathResolver
{
    public static string GetPath(string rootPath, string contentHash, DerivativeSize size)
    {
        var (shard1, shard2) = GetShards(contentHash);
        var fileName = $"{contentHash}-{(int)size}.webp";
        return Path.Combine(rootPath, shard1, shard2, fileName);
    }

    public static string GetTempPath(string rootPath, string contentHash, DerivativeSize size)
    {
        var (shard1, shard2) = GetShards(contentHash);
        var fileName = $"{contentHash}-{(int)size}-{Guid.NewGuid():N}.tmp";
        return Path.Combine(rootPath, shard1, shard2, fileName);
    }

    private static (string Shard1, string Shard2) GetShards(string contentHash)
    {
        if (contentHash is null || contentHash.Length < 4)
            throw new ArgumentException("Content hash must be at least 4 characters long.", nameof(contentHash));

        return (contentHash[..2], contentHash[2..4]);
    }
}
