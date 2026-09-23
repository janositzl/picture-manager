using System;
using System.Linq;
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
        // AsNoTracking: this is polled repeatedly against the same DbContext instance by
        // ScanEndpoints.StreamScanEventsAsync's SSE loop. A tracking query would hit EF Core's
        // identity resolution on the second+ call and silently return the first call's
        // already-tracked (now stale) instance instead of re-reading the database.
        return await _dbContext.ScanJobs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
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

    public async Task ReloadAsync(ScanJob scanJob, CancellationToken cancellationToken = default)
    {
        await _dbContext.Entry(scanJob).ReloadAsync(cancellationToken);
    }

    public async Task<bool> HasActiveJobAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.ScanJobs.AnyAsync(
            j => j.Status == ScanJobStatus.Enumerating || j.Status == ScanJobStatus.Enriching,
            cancellationToken);
    }

    public async Task SetEnumerationResultAsync(int scanJobId, int foldersScanned, int filesFound, CancellationToken cancellationToken = default)
    {
        await _dbContext.ScanJobs
            .Where(j => j.Id == scanJobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.FoldersScanned, foldersScanned)
                .SetProperty(j => j.FilesFound, filesFound),
                cancellationToken);
    }

    public async Task<bool> TryTransitionToEnrichingAsync(int scanJobId, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.ScanJobs
            .Where(j => j.Id == scanJobId && j.Status == ScanJobStatus.Enumerating)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, ScanJobStatus.Enriching), cancellationToken);
        return rows > 0;
    }

    public async Task IncrementFilesEnrichedAsync(int scanJobId, CancellationToken cancellationToken = default)
    {
        await _dbContext.ScanJobs
            .Where(j => j.Id == scanJobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.FilesEnriched, j => j.FilesEnriched + 1), cancellationToken);
    }

    public async Task<bool> TryMarkCompletedIfEnrichedAsync(int scanJobId, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.ScanJobs
            .Where(j => j.Id == scanJobId && j.Status == ScanJobStatus.Enriching && j.FilesEnriched >= j.FilesFound)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, ScanJobStatus.Completed)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
        return rows > 0;
    }

    public async Task SetFailureResultAsync(int scanJobId, int foldersScanned, int filesFound, string? errorMessage, ScanJobStatus status, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        await _dbContext.ScanJobs
            .Where(j => j.Id == scanJobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.FoldersScanned, foldersScanned)
                .SetProperty(j => j.FilesFound, filesFound)
                .SetProperty(j => j.Status, status)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
    }
}
