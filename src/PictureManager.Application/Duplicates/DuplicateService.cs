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

    private static DuplicateImageItem ToItem(DuplicateMemberRow row)
    {
        var image = row.Image;
        return new DuplicateImageItem(
            image.Id, image.FolderId, image.FileName, image.Extension, image.Width, image.Height, image.DateTaken,
            image.IsFavorite, ImageUrls.Thumbnail(image.Id, image.ContentHash), ImageUrls.Preview(image.Id, image.ContentHash),
            FolderDisplayPath.For(row.RootName, row.RelativePath));
    }
}
