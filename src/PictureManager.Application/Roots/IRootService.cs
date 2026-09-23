using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Roots;

public interface IRootService
{
    Task<IReadOnlyList<RootSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<RootSummary>> UpdateAsync(int id, RootUpdate update, CancellationToken cancellationToken = default);
}
