using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Albums;

public sealed class AlbumService : IAlbumService
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    private readonly IAlbumRepository _albums;
    private readonly IImageQueryRepository _images;
    private readonly IFolderRepository _folders;
    private readonly IAppUserRepository _users;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public AlbumService(IAlbumRepository albums, IImageQueryRepository images, IFolderRepository folders, IAppUserRepository users, ICurrentUser currentUser, IClock clock)
    {
        _albums = albums;
        _images = images;
        _folders = folders;
        _users = users;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IReadOnlyList<AlbumSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _albums.GetSummariesAsync(_currentUser.UserId, cancellationToken);
        return rows.Select(r =>
        {
            var access = AlbumAccessRules.From(r.IsOwner, r.IsEditor);
            return new AlbumSummary(
                r.Id, r.Name, r.Description, r.ImageCount,
                r.CoverImageId is int coverId && r.CoverContentHash is { } hash ? ImageUrls.Thumbnail(coverId, hash) : null,
                r.UpdatedAt, access.ToString(), r.OwnerDisplayName, access == AlbumAccess.Owner ? r.ShareCount : 0);
        }).ToList();
    }

    public async Task<Result<AlbumDetail>> CreateAsync(AlbumCreate input, CancellationToken cancellationToken = default)
    {
        var name = input.Name?.Trim();
        if (ValidateName(name) is { } nameError)
            return Result.Invalid("name", nameError);

        var description = NormalizeDescription(input.Description);
        if (description is { Length: > DescriptionMaxLength })
            return Result.Invalid("description", $"Must be at most {DescriptionMaxLength} characters.");

        if (await _albums.NameExistsAsync(_currentUser.UserId, name!, null, cancellationToken))
            return Result.Conflict($"An album named '{name}' already exists.");

        var now = _clock.UtcNow;
        var album = await _albums.AddAsync(new Album
        {
            Name = name!,
            Description = description,
            OwnerUserId = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now
        }, cancellationToken);

        var ownerName = (await _users.GetByIdAsync(_currentUser.UserId, cancellationToken))?.DisplayName ?? string.Empty;
        return Result<AlbumDetail>.Ok(ToDetail(album, 0, AlbumAccess.Owner, ownerName));
    }

    public async Task<Result<AlbumDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var (accessible, failure) = await RequireAsync(id, AlbumAccess.Viewer, cancellationToken);
        if (failure is not null)
            return failure;

        return Result<AlbumDetail>.Ok(ToDetail(
            accessible!.Album, await _albums.CountImagesAsync(id, cancellationToken), accessible.Access, accessible.OwnerDisplayName));
    }

    public async Task<Result<AlbumDetail>> UpdateAsync(int id, AlbumUpdate input, CancellationToken cancellationToken = default)
    {
        var (accessible, failure) = await RequireAsync(id, AlbumAccess.Owner, cancellationToken);
        if (failure is not null)
            return failure;
        var album = accessible!.Album;

        if (input.Name is not null)
        {
            var name = input.Name.Trim();
            if (ValidateName(name) is { } nameError)
                return Result.Invalid("name", nameError);
            if (await _albums.NameExistsAsync(_currentUser.UserId, name, id, cancellationToken))
                return Result.Conflict($"An album named '{name}' already exists.");
            album.Name = name;
        }

        if (input.DescriptionSpecified)
        {
            var description = NormalizeDescription(input.Description);
            if (description is { Length: > DescriptionMaxLength })
                return Result.Invalid("description", $"Must be at most {DescriptionMaxLength} characters.");
            album.Description = description;
        }

        album.UpdatedAt = _clock.UtcNow;
        await _albums.UpdateAsync(album, cancellationToken);
        return Result<AlbumDetail>.Ok(ToDetail(album, await _albums.CountImagesAsync(id, cancellationToken), AlbumAccess.Owner, accessible.OwnerDisplayName));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var (accessible, failure) = await RequireAsync(id, AlbumAccess.Owner, cancellationToken);
        if (failure is not null)
            return failure;

        await _albums.DeleteAsync(accessible!.Album, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<PagedResult<AlbumImageItem>>> ListImagesAsync(int id, string? cursor, int? limit, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Viewer, cancellationToken);
        if (failure is not null)
            return failure;

        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
            return Result.Invalid("limit", $"Must be between 1 and {MaxLimit}.");

        AlbumImageCursor? after = null;
        if (cursor is not null && !CursorCodec.TryDecode(cursor, out after))
            return Result.Invalid("cursor", "The cursor is malformed.");

        var rows = await _albums.ListImagesAsync(id, _currentUser.UserId, after?.SortOrder, after?.ImageId, take + 1, cancellationToken);
        var page = rows.Take(take).ToList();
        string? nextCursor = null;
        if (rows.Count > take)
        {
            var last = page[^1];
            nextCursor = CursorCodec.Encode(new AlbumImageCursor(last.SortOrder, last.Image.Id));
        }

        return Result<PagedResult<AlbumImageItem>>.Ok(
            new PagedResult<AlbumImageItem>(page.Select(AlbumImageItem.From).ToList(), nextCursor));
    }

    public async Task<Result<AlbumAddResult>> AddImagesAsync(int id, AlbumAddImages input, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Editor, cancellationToken);
        if (failure is not null)
            return failure;

        if ((input.ImageIds is null) == (input.FolderId is null))
            return Result.Invalid("body", "Provide either imageIds or folderId, not both.");

        List<int> candidates;
        if (input.ImageIds is not null)
        {
            candidates = input.ImageIds.Distinct().ToList();
            if (candidates.Count == 0)
                return Result.Invalid("imageIds", "Must contain at least one id.");

            var visible = (await _images.GetVisibleIdsAsync(candidates, cancellationToken)).ToHashSet();
            var unknown = candidates.Where(c => !visible.Contains(c)).ToList();
            if (unknown.Count > 0)
                return Result.Invalid("imageIds", $"Unknown or unavailable image ids: {string.Join(", ", unknown)}.");
        }
        else
        {
            if (!await _folders.IsVisibleAsync(input.FolderId!.Value, cancellationToken))
                return Result.NotFound();
            candidates = (await _images.GetVisibleIdsInFolderAsync(input.FolderId.Value, cancellationToken)).ToList();
        }

        var existing = (await _albums.GetOrderedImageIdsAsync(id, cancellationToken)).ToHashSet();
        var toAdd = candidates.Where(c => !existing.Contains(c)).ToList();
        if (toAdd.Count > 0)
        {
            var now = _clock.UtcNow;
            await _albums.AppendImagesAsync(id, toAdd, now, cancellationToken);
            await _albums.TouchAsync(id, now, cancellationToken);
        }

        return Result<AlbumAddResult>.Ok(new AlbumAddResult(toAdd.Count, candidates.Count - toAdd.Count));
    }

    public async Task<Result> RemoveImagesAsync(int id, IReadOnlyList<int>? imageIds, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Editor, cancellationToken);
        if (failure is not null)
            return failure;
        if (imageIds is null || imageIds.Count == 0)
            return Result.Invalid("imageIds", "Must contain at least one id.");

        await _albums.RemoveImagesAsync(id, imageIds.Distinct().ToList(), cancellationToken);
        await _albums.TouchAsync(id, _clock.UtcNow, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> MoveImageAsync(int id, int imageId, int? afterImageId, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Editor, cancellationToken);
        if (failure is not null)
            return failure;

        var order = (await _albums.GetOrderedImageIdsAsync(id, cancellationToken)).ToList();
        if (!order.Contains(imageId))
            return Result.Invalid("imageId", "The image is not in this album.");
        if (afterImageId == imageId)
            return Result.Invalid("afterImageId", "An image cannot be moved after itself.");
        if (afterImageId is int anchor && !order.Contains(anchor))
            return Result.Invalid("afterImageId", "The anchor image is not in this album.");

        order.Remove(imageId);
        var insertAt = afterImageId is int after ? order.IndexOf(after) + 1 : 0;
        order.Insert(insertAt, imageId);

        await _albums.ReorderAsync(id, order, cancellationToken);
        await _albums.TouchAsync(id, _clock.UtcNow, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> SortAsync(int id, string? by, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Editor, cancellationToken);
        if (failure is not null)
            return failure;

        AlbumSortKey? key = by switch
        {
            "dateAsc" => AlbumSortKey.DateAscending,
            "dateDesc" => AlbumSortKey.DateDescending,
            "name" => AlbumSortKey.Name,
            _ => null,
        };
        if (key is not { } sortKey)
            return Result.Invalid("by", "Must be one of: dateAsc, dateDesc, name.");

        var order = await _albums.GetImageIdsSortedAsync(id, sortKey, cancellationToken);
        await _albums.ReorderAsync(id, order, cancellationToken);
        await _albums.TouchAsync(id, _clock.UtcNow, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> SetCoverAsync(int id, int? imageId, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Editor, cancellationToken);
        if (failure is not null)
            return failure;

        if (imageId is not int coverId || !(await _albums.GetOrderedImageIdsAsync(id, cancellationToken)).Contains(coverId))
            return Result.Invalid("imageId", "Choose a photo from this album.");

        await _albums.SetCoverAsync(id, coverId, cancellationToken);
        await _albums.TouchAsync(id, _clock.UtcNow, cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<AlbumExport>> ExportAsync(int id, string? prefix, CancellationToken cancellationToken = default)
    {
        var (accessible, failure) = await RequireAsync(id, AlbumAccess.Viewer, cancellationToken);
        if (failure is not null)
            return failure;

        var rows = await _albums.GetExportRowsAsync(id, cancellationToken);
        return Result<AlbumExport>.Ok(new AlbumExport(
            AlbumExportFormatter.FileName(accessible!.Album.Name), AlbumExportFormatter.Format(prefix, rows)));
    }

    public async Task<Result<IReadOnlyList<AlbumShareDto>>> GetSharesAsync(int id, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Owner, cancellationToken);
        if (failure is not null)
            return failure;

        var rows = await _albums.GetSharesAsync(id, cancellationToken);
        return Result<IReadOnlyList<AlbumShareDto>>.Ok(
            rows.Select(r => new AlbumShareDto(r.UserId, r.DisplayName, r.Permission.ToString())).ToList());
    }

    public async Task<Result<AlbumShareDto>> SetShareAsync(int id, int userId, string? permission, CancellationToken cancellationToken = default)
    {
        var (_, failure) = await RequireAsync(id, AlbumAccess.Owner, cancellationToken);
        if (failure is not null)
            return failure;
        if (ParsePermission(permission) is not { } parsed)
            return Result.Invalid("permission", "Must be Viewer or Editor.");
        if (userId == _currentUser.UserId)
            return Result.Invalid("userId", "You can't share an album with yourself.");
        var target = await _users.GetByIdAsync(userId, cancellationToken);
        if (target is not { IsActive: true })
            return Result.Invalid("userId", "Choose an active user.");

        await _albums.SetShareAsync(id, userId, parsed, _clock.UtcNow, cancellationToken);
        return Result<AlbumShareDto>.Ok(new AlbumShareDto(target.Id, target.DisplayName, parsed.ToString()));
    }

    public async Task<Result> RemoveShareAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var (accessible, failure) = await RequireAsync(id, AlbumAccess.Viewer, cancellationToken);
        if (failure is not null)
            return failure;
        if (accessible!.Access != AlbumAccess.Owner && userId != _currentUser.UserId)
            return Result.Forbidden("Only the album's owner can do this.");

        return await _albums.RemoveShareAsync(id, userId, cancellationToken) ? Result.Ok() : Result.NotFound();
    }

    /// <summary>The album when the caller has at least <paramref name="needed"/>; otherwise NotFound (no access at all) or Forbidden.</summary>
    private async Task<(AccessibleAlbum? Album, Result? Failure)> RequireAsync(int id, AlbumAccess needed, CancellationToken cancellationToken)
    {
        var accessible = await _albums.GetAccessibleAsync(id, _currentUser.UserId, cancellationToken);
        if (accessible is null)
            return (null, Result.NotFound());
        if (accessible.Access < needed)
            return (null, Result.Forbidden(needed == AlbumAccess.Owner
                ? "Only the album's owner can do this."
                : "You can only view this album."));
        return (accessible, null);
    }

    private static SharePermission? ParsePermission(string? raw) =>
        // Enum.TryParse also accepts numbers ("1"); only the names are valid here.
        raw is not null && !int.TryParse(raw, out _) && Enum.TryParse(raw, ignoreCase: true, out SharePermission parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Must not be blank.";
        if (name.Length > NameMaxLength)
            return $"Must be at most {NameMaxLength} characters.";
        return null;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static AlbumDetail ToDetail(Album album, int imageCount, AlbumAccess access, string ownerDisplayName) =>
        new(album.Id, album.Name, album.Description, imageCount, album.CreatedAt, album.UpdatedAt, access.ToString(), ownerDisplayName);
}
