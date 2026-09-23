using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Duplicates;

public interface IDuplicateService
{
    Task<Result<PagedResult<DuplicateGroup>>> ListAsync(string? cursor, int? limit, CancellationToken cancellationToken = default);
}
