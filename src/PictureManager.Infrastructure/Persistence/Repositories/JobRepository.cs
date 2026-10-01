using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class JobRepository : IJobRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public JobRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Job?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        // AsNoTracking: this is polled repeatedly against the same DbContext instance by
        // ScanEndpoints.StreamScanEventsAsync's SSE loop. A tracking query would hit EF Core's
        // identity resolution on the second+ call and silently return the first call's
        // already-tracked (now stale) instance instead of re-reading the database.
        return await _dbContext.Jobs.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Job> AddAsync(Job scanJob, CancellationToken cancellationToken = default)
    {
        _dbContext.Jobs.Add(scanJob);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return scanJob;
    }

    public async Task UpdateAsync(Job scanJob, CancellationToken cancellationToken = default)
    {
        _dbContext.Jobs.Update(scanJob);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReloadAsync(Job scanJob, CancellationToken cancellationToken = default)
    {
        await _dbContext.Entry(scanJob).ReloadAsync(cancellationToken);
    }

    public async Task<bool> HasActiveJobAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Jobs.AnyAsync(
            j => j.Status == JobStatus.Enumerating || j.Status == JobStatus.Enriching,
            cancellationToken);
    }

    public async Task<Job?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Jobs.AsNoTracking().FirstOrDefaultAsync(
            j => j.Status == JobStatus.Enumerating || j.Status == JobStatus.Enriching,
            cancellationToken);
    }

    public async Task<int> FailActiveJobsAsync(string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Jobs
            .Where(j => j.Status == JobStatus.Enumerating || j.Status == JobStatus.Enriching)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
    }

    public async Task SetEnumerationResultAsync(int scanJobId, int foldersScanned, int filesFound, CancellationToken cancellationToken = default)
    {
        await _dbContext.Jobs
            .Where(j => j.Id == scanJobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.FoldersProcessed, foldersScanned)
                .SetProperty(j => j.FilesFound, filesFound),
                cancellationToken);
    }

    public async Task<bool> TryTransitionToEnrichingAsync(int scanJobId, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Jobs
            .Where(j => j.Id == scanJobId && j.Status == JobStatus.Enumerating)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Enriching), cancellationToken);
        return rows > 0;
    }

    public async Task TryMarkCompletedFromEnumeratingAsync(int discoveryJobId, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        await _dbContext.Jobs
            .Where(j => j.Id == discoveryJobId && j.Status == JobStatus.Enumerating)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
    }

    public async Task<bool> TryMarkCompletedAsync(int jobId, JobStatus fromStatus, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Jobs
            .Where(j => j.Id == jobId && j.Status == fromStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
        return rows > 0;
    }

    public async Task IncrementFilesEnrichedAsync(int scanJobId, CancellationToken cancellationToken = default)
    {
        await _dbContext.Jobs
            .Where(j => j.Id == scanJobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.FilesEnriched, j => j.FilesEnriched + 1), cancellationToken);
    }

    public async Task<bool> TryMarkCompletedIfEnrichedAsync(int scanJobId, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Jobs
            .Where(j => j.Id == scanJobId && j.Status == JobStatus.Enriching && j.FilesEnriched >= j.FilesFound)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
        return rows > 0;
    }

    public async Task SetFailureResultAsync(int scanJobId, int foldersScanned, int filesFound, string? errorMessage, JobStatus status, DateTime completedUtc, CancellationToken cancellationToken = default)
    {
        await _dbContext.Jobs
            .Where(j => j.Id == scanJobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.FoldersProcessed, foldersScanned)
                .SetProperty(j => j.FilesFound, filesFound)
                .SetProperty(j => j.Status, status)
                .SetProperty(j => j.ErrorMessage, errorMessage)
                .SetProperty(j => j.CompletedUtc, completedUtc),
                cancellationToken);
    }
}
