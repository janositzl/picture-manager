using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Roots;

public interface IRootService
{
    Task<IReadOnlyList<RootSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Result<RootSummary>> UpdateAsync(int id, RootUpdate update, CancellationToken cancellationToken = default);

    Task<Result<RootSummary>> CreateAsync(RootCreate input, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the root, its whole folder subtree and their images (no tombstone; irreversible).</summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
