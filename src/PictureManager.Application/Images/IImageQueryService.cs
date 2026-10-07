using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Images;

public interface IImageQueryService
{
    Task<Result<PagedResult<ImageListItem>>> ListAsync(ImageListRequest request, CancellationToken cancellationToken = default);

    Task<Result<ImageDetail>> GetDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<HiddenResult>> SetHiddenAsync(IReadOnlyCollection<int> imageIds, bool isHidden, CancellationToken cancellationToken = default);

    /// <summary>Turns the thumbnails of the photos clockwise by `degrees` (90, 180 or 270), on top of any rotation they already have.</summary>
    Task<Result<HiddenResult>> RotateThumbnailsAsync(IReadOnlyCollection<int> imageIds, int degrees, CancellationToken cancellationToken = default);

    Task<Result> SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default);
}
