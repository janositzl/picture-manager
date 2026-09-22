using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IScanJobRepository
{
    Task<ScanJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ScanJob> AddAsync(ScanJob scanJob, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScanJob scanJob, CancellationToken cancellationToken = default);
}
