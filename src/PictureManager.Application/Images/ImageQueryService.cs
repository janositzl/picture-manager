using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Images;

public sealed class ImageQueryService : IImageQueryService
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;

    private readonly IImageQueryRepository _images;
    private readonly IFolderRepository _folders;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public ImageQueryService(IImageQueryRepository images, IFolderRepository folders, ICurrentUser currentUser, IClock clock)
    {
        _images = images;
        _folders = folders;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<PagedResult<ImageListItem>>> ListAsync(ImageListRequest request, CancellationToken cancellationToken = default)
    {
        var sortToken = (request.Sort ?? "date").ToLowerInvariant();
        ImageSort sort;
        switch (sortToken)
        {
            case "date": sort = ImageSort.Date; break;
            case "name": sort = ImageSort.Name; break;
            default: return Result.Invalid("sort", "Must be 'date' or 'name'.");
        }

        var orderToken = (request.Order ?? (sort == ImageSort.Date ? "desc" : "asc")).ToLowerInvariant();
        SortDirection direction;
        switch (orderToken)
        {
            case "asc": direction = SortDirection.Asc; break;
            case "desc": direction = SortDirection.Desc; break;
            default: return Result.Invalid("order", "Must be 'asc' or 'desc'.");
        }

        var limit = request.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
            return Result.Invalid("limit", $"Must be between 1 and {MaxLimit}.");

        ImageKeyset? after = null;
        if (request.Cursor is not null)
        {
            if (!CursorCodec.TryDecode<ImageCursor>(request.Cursor, out var cursor))
                return Result.Invalid("cursor", "The cursor is malformed.");
            if (cursor.Sort != sortToken || cursor.Order != orderToken)
                return Result.Invalid("cursor", "The cursor belongs to a different sort or order.");
            if (!TryToKeyset(cursor, sort, out after))
                return Result.Invalid("cursor", "The cursor is malformed.");
        }

        if (request.FolderId is int folderId && !await _folders.IsVisibleAsync(folderId, cancellationToken))
            return Result.NotFound();

        PersonFaceState? personState = null;
        if (request.PersonState is not null)
        {
            if (request.PersonId is null)
                return Result.Invalid("personState", "Needs a personId.");
            switch (request.PersonState.ToLowerInvariant())
            {
                case "confirmed": personState = PersonFaceState.Confirmed; break;
                case "suggested": personState = PersonFaceState.Suggested; break;
                default: return Result.Invalid("personState", "Must be 'confirmed' or 'suggested'.");
            }
        }

        bool? hasFaces = null;
        if (request.Faces is not null)
        {
            switch (request.Faces.ToLowerInvariant())
            {
                case "with": hasFaces = true; break;
                case "without": hasFaces = false; break;
                default: return Result.Invalid("faces", "Must be 'with' or 'without'.");
            }
        }

        var filter = new ImageListFilter(request.FolderId, request.Folder, request.FileName, request.FavoritesOnly, request.PersonId, personState, hasFaces, request.IncludeHidden);
        var rows = await _images.ListAsync(filter, sort, direction, after, limit + 1, cancellationToken);

        var page = rows.Take(limit).ToList();
        string? nextCursor = null;
        if (rows.Count > limit)
        {
            var last = page[^1];
            var key = sort == ImageSort.Date
                ? last.SortDate.Ticks.ToString(CultureInfo.InvariantCulture)
                : last.SortName;
            nextCursor = CursorCodec.Encode(new ImageCursor(sortToken, orderToken, key, last.Id));
        }

        return Result<PagedResult<ImageListItem>>.Ok(
            new PagedResult<ImageListItem>(page.Select(ImageListItem.From).ToList(), nextCursor));
    }

    public async Task<Result<ImageDetail>> GetDetailAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await _images.GetVisibleDetailAsync(id, cancellationToken);
        if (row is null)
            return Result.NotFound();

        var albums = await _images.GetAlbumsContainingAsync(id, _currentUser.UserId, cancellationToken);
        var image = row.Image;
        return Result<ImageDetail>.Ok(new ImageDetail(
            image.Id, image.FolderId, image.FileName, image.Extension, image.Width, image.Height, image.DateTaken,
            image.IsFavorite, ImageUrls.Thumbnail(image.Id, image.ContentHash, image.ThumbnailRotation), ImageUrls.Preview(image.Id, image.ContentHash),
            row.FileSize, row.FileModified, row.Orientation, row.CameraMake, row.CameraModel, row.LensModel,
            row.Latitude, row.Longitude, ParseJson(row.RawMetadata), FolderDisplayPath.For(row.RootName, row.RelativePath),
            albums, image.IndexState == IndexState.Invalid, image.IsHidden));
    }

    private const int MaxHideBatch = 5000;

    public async Task<Result<HiddenResult>> SetHiddenAsync(IReadOnlyCollection<int> imageIds, bool isHidden, CancellationToken cancellationToken = default)
    {
        if (imageIds.Count == 0)
            return Result.Invalid("imageIds", "Must not be empty.");
        if (imageIds.Count > MaxHideBatch)
            return Result.Invalid("imageIds", $"Must not contain more than {MaxHideBatch} ids.");

        var affected = await _images.SetHiddenAsync(imageIds, isHidden, _clock.UtcNow, cancellationToken);
        return Result<HiddenResult>.Ok(new HiddenResult(affected));
    }

    public async Task<Result<HiddenResult>> RotateThumbnailsAsync(IReadOnlyCollection<int> imageIds, int degrees, CancellationToken cancellationToken = default)
    {
        if (imageIds.Count == 0)
            return Result.Invalid("imageIds", "Must not be empty.");
        if (imageIds.Count > MaxHideBatch)
            return Result.Invalid("imageIds", $"Must not contain more than {MaxHideBatch} ids.");
        if (degrees is not (90 or 180 or 270))
            return Result.Invalid("degrees", "Must be 90, 180 or 270.");

        var affected = await _images.RotateThumbnailsAsync(imageIds, degrees, _clock.UtcNow, cancellationToken);
        return Result<HiddenResult>.Ok(new HiddenResult(affected));
    }

    public async Task<Result> SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default)
    {
        return await _images.SetFavoriteAsync(id, isFavorite, _clock.UtcNow, cancellationToken)
            ? Result.Ok()
            : Result.NotFound();
    }

    private static bool TryToKeyset(ImageCursor cursor, ImageSort sort, out ImageKeyset? keyset)
    {
        keyset = null;
        if (cursor.Key is null)
            return false;

        if (sort == ImageSort.Name)
        {
            keyset = new ImageKeyset(null, cursor.Key, cursor.Id);
            return true;
        }

        if (!long.TryParse(cursor.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks)
            return false;

        keyset = new ImageKeyset(new DateTime(ticks, DateTimeKind.Utc), null, cursor.Id);
        return true;
    }

    private static JsonElement? ParseJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
