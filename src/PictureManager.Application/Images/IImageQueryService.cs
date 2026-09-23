using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Images;

public interface IImageQueryService
{
    Task<Result<PagedResult<ImageListItem>>> ListAsync(ImageListRequest request, CancellationToken cancellationToken = default);

    Task<Result<ImageDetail>> GetDetailAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> SetFavoriteAsync(int id, bool isFavorite, CancellationToken cancellationToken = default);
}
