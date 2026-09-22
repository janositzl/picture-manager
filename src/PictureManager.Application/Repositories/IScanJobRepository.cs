using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IScanJobRepository
{
    Task<ScanJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ScanJob> AddAsync(ScanJob scanJob, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScanJob scanJob, CancellationToken cancellationToken = default);

    // Re-queries the store and overwrites the given (already-tracked) entity's CURRENT VALUES in
    // place. This is deliberately distinct from GetByIdAsync: when the caller already holds a
    // tracked instance for this id (as ScanService does for the lifetime of a scan), GetByIdAsync
    // hits EF Core's identity resolution and simply hands back that same tracked instance instead
    // of re-querying the database -- silently no-op'ing a "refresh". ReloadAsync is the correct
    // primitive for pulling in whatever another DbContext scope (e.g. EnrichmentBackgroundService)
    // has committed concurrently.
    Task ReloadAsync(ScanJob scanJob, CancellationToken cancellationToken = default);
}
