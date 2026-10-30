using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Images;

public interface IImageQueryService
{
    Task<Result<PagedResult<ImageListItem>>> ListAsync(ImageListRequest request, CancellationToken cancellationToken = default);

    /// <summary>Ids of all photos matching the request's filters (sort applies; cursor and limit are ignored), capped at 5000.</summary>
    Task<Result<ImageIdsResult>> ListIdsAsync(ImageListRequest request, CancellationToken cancellationToken = default);

    /// <summary>The days the matching photos fall on, with counts, in the request's order (cursor and limit are ignored).</summary>
    Task<Result<IReadOnlyList<ImageDay>>> ListDaysAsync(ImageListRequest request, CancellationToken cancellationToken = default);

    Task<Result<ImageDetail>> GetDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<HiddenResult>> SetHiddenAsync(IReadOnlyCollection<int> imageIds, bool isHidden, CancellationToken cancellationToken = default);

    /// <summary>Sets the photo's note; null or whitespace clears it.</summary>
    Task<Result> SetNoteAsync(int id, string? note, CancellationToken cancellationToken = default);

    /// <summary>Turns the thumbnails of the photos clockwise by `degrees` (90, 180 or 270), on top of any rotation they already have.</summary>
    Task<Result<HiddenResult>> RotateThumbnailsAsync(IReadOnlyCollection<int> imageIds, int degrees, CancellationToken cancellationToken = default);

    Task<Result> SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default);
}
