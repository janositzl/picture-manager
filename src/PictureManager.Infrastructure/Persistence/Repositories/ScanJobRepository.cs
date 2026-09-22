using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class ScanJobRepository : IScanJobRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public ScanJobRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ScanJob?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScanJobs.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<ScanJob> AddAsync(ScanJob scanJob, CancellationToken cancellationToken = default)
    {
        _dbContext.ScanJobs.Add(scanJob);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return scanJob;
    }

    public async Task UpdateAsync(ScanJob scanJob, CancellationToken cancellationToken = default)
    {
        _dbContext.ScanJobs.Update(scanJob);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
