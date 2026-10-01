using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Duplicates;

public interface IDuplicateService
{
    Task<Result<PagedResult<DuplicateGroup>>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SimilarGroup>>> ListSimilarAsync(int? threshold, string? cursor, int? limit, CancellationToken cancellationToken = default);
}
