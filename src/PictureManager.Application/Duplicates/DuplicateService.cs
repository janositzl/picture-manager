using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Duplicates;

public sealed class DuplicateService : IDuplicateService
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;
    public const int DefaultSimilarThreshold = 6;

    private readonly IImageQueryRepository _images;

    public DuplicateService(IImageQueryRepository images)
    {
        _images = images;
    }

    public async Task<Result<PagedResult<DuplicateGroup>>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken = default)
    {
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
            return Result.Invalid("limit", $"Must be between 1 and {MaxLimit}.");

        DuplicateGroupKey? after = null;
        if (cursor is not null)
        {
            if (!CursorCodec.TryDecode<DuplicateCursor>(cursor, out var decoded) || decoded.ContentHash is null)
                return Result.Invalid("cursor", "The cursor is malformed.");
            after = new DuplicateGroupKey(decoded.ContentHash, decoded.Count);
        }

        var keys = await _images.GetDuplicateGroupsAsync(after, take + 1, cancellationToken);
        var pageKeys = keys.Take(take).ToList();

        var members = pageKeys.Count == 0
            ? Array.Empty<DuplicateMemberRow>()
            : await _images.GetDuplicateMembersAsync(pageKeys.Select(k => k.ContentHash).ToList(), cancellationToken);

        var byHash = members
            .GroupBy(m => m.Image.ContentHash)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DuplicateImageItem>)g.Select(ToItem)
                    .OrderBy(i => i.FolderPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList());

        var groups = pageKeys
            .Select(k => new DuplicateGroup(k.ContentHash, k.Count,
                byHash.TryGetValue(k.ContentHash, out var images) ? images : Array.Empty<DuplicateImageItem>()))
            .ToList();

        var nextCursor = keys.Count > take
            ? CursorCodec.Encode(new DuplicateCursor(pageKeys[^1].Count, pageKeys[^1].ContentHash))
            : null;

        return Result<PagedResult<DuplicateGroup>>.Ok(new PagedResult<DuplicateGroup>(groups, nextCursor));
    }

    public async Task<Result<PagedResult<SimilarGroup>>> ListSimilarAsync(int? threshold, string? cursor, int? limit, CancellationToken cancellationToken = default)
    {
        var maxDistance = threshold ?? DefaultSimilarThreshold;
        if (maxDistance < 0 || maxDistance > SimilarityClusterer.MaxThreshold)
            return Result.Invalid("threshold", $"Must be between 0 and {SimilarityClusterer.MaxThreshold}.");

        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
            return Result.Invalid("limit", $"Must be between 1 and {MaxLimit}.");

        var offset = 0;
        if (cursor is not null)
        {
            if (!CursorCodec.TryDecode<SimilarCursor>(cursor, out var decoded) || decoded.Offset < 0)
                return Result.Invalid("cursor", "The cursor is malformed.");
            offset = decoded.Offset;
        }

        var rows = await _images.GetPerceptualHashesAsync(cancellationToken);
        var hashes = new Dictionary<int, ulong>(rows.Count);
        var parsed = new List<(int Id, ulong Hash)>(rows.Count);
        foreach (var row in rows)
        {
            if (PerceptualHash.Parse(row.PerceptualHash) is not { } hash)
                continue;
            hashes[row.Id] = hash;
            parsed.Add((row.Id, hash));
        }

        var clusters = SimilarityClusterer.Cluster(parsed, maxDistance);
        var pageClusters = clusters.Skip(offset).Take(take).ToList();

        var members = pageClusters.Count == 0
            ? Array.Empty<DuplicateMemberRow>()
            : await _images.GetMembersByIdsAsync(pageClusters.SelectMany(c => c).ToList(), cancellationToken);
        var byId = members.ToDictionary(m => m.Image.Id);

        var groups = new List<SimilarGroup>(pageClusters.Count);
        foreach (var cluster in pageClusters)
        {
            var images = cluster
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .OrderByDescending(m => (long)(m.Image.Width ?? 0) * (m.Image.Height ?? 0))
                .ThenByDescending(m => m.FileSize)
                .ThenBy(m => m.Image.Id)
                .Select(ToItem)
                .ToList();
            // Member rows can vanish between the hash query and this one; a group left with fewer than two
            // images is no longer a group, so the page may come back shorter than the limit.
            if (images.Count < 2)
                continue;
            groups.Add(new SimilarGroup("s" + cluster.Min(), images.Count, MaxPairwiseDistance(cluster, hashes), images));
        }

        var nextCursor = offset + take < clusters.Count
            ? CursorCodec.Encode(new SimilarCursor(offset + take))
            : null;

        return Result<PagedResult<SimilarGroup>>.Ok(new PagedResult<SimilarGroup>(groups, nextCursor));
    }

    private static int MaxPairwiseDistance(IReadOnlyList<int> ids, Dictionary<int, ulong> hashes)
    {
        var max = 0;
        for (var a = 0; a < ids.Count; a++)
            for (var b = a + 1; b < ids.Count; b++)
                max = Math.Max(max, PerceptualHash.Distance(hashes[ids[a]], hashes[ids[b]]));
        return max;
    }

    private static DuplicateImageItem ToItem(DuplicateMemberRow row)
    {
        var image = row.Image;
        return new DuplicateImageItem(
            image.Id, image.FolderId, image.FileName, image.Extension, image.Width, image.Height, image.DateTaken,
            image.IsFavorite, ImageUrls.Thumbnail(image.Id, image.ContentHash, image.ThumbnailRotation), ImageUrls.Preview(image.Id, image.ContentHash),
            FolderDisplayPath.For(row.RootName, row.RelativePath), row.FileSize);
    }
}
