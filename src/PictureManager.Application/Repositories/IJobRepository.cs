using System;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Job> AddAsync(Job scanJob, CancellationToken cancellationToken = default);
    Task UpdateAsync(Job scanJob, CancellationToken cancellationToken = default);

    // Re-queries the store and overwrites the given (already-tracked) entity's CURRENT VALUES in
    // place. Kept as a general-purpose "refresh a tracked instance from the DB" primitive and
    // still exercised by its own test documenting the EF Core identity-resolution behavior below
    // -- but note ScanService no longer uses this for Job finalization (see
    // SetEnumerationResultAsync / TryTransitionToEnrichingAsync / TryMarkCompletedIfEnrichedAsync
    // / SetFailureResultAsync below), because holding one tracked instance across a scan's whole
    // duration and later doing a whole-row write from it is exactly the pattern that raced against
    // EnrichmentBackgroundService's concurrent writes to the same row.
    Task ReloadAsync(Job scanJob, CancellationToken cancellationToken = default);

    /// <summary>True if any Job is currently Enumerating or Enriching.</summary>
    Task<bool> HasActiveJobAsync(CancellationToken cancellationToken = default);

    /// <summary>The Job currently Enumerating or Enriching, if any.</summary>
    Task<Job?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets every Enumerating or Enriching job to Failed with the message and CompletedUtc. Returns how many changed.</summary>
    Task<int> FailActiveJobsAsync(string errorMessage, DateTime completedUtc, CancellationToken cancellationToken = default);

    /// <summary>Atomically sets FoldersProcessed and FilesFound only. Never touches Status.</summary>
    Task SetEnumerationResultAsync(int scanJobId, int foldersScanned, int filesFound, CancellationToken cancellationToken = default);

    /// <summary>Atomically flips Status from Enumerating to Enriching. Returns whether a row was changed.</summary>
    Task<bool> TryTransitionToEnrichingAsync(int scanJobId, CancellationToken cancellationToken = default);

    /// <summary>Atomically flips Status from Enumerating straight to Completed (setting CompletedUtc). Discovery only: it has no enrichment phase.</summary>
    Task TryMarkCompletedFromEnumeratingAsync(int discoveryJobId, DateTime completedUtc, CancellationToken cancellationToken = default);

    /// <summary>Atomically flips Status from fromStatus to Completed (setting CompletedUtc). Returns whether a row changed.</summary>
    Task<bool> TryMarkCompletedAsync(int jobId, JobStatus fromStatus, DateTime completedUtc, CancellationToken cancellationToken = default);

    /// <summary>Atomically increments FilesEnriched by 1.</summary>
    Task IncrementFilesEnrichedAsync(int scanJobId, CancellationToken cancellationToken = default);

    /// <summary>Face recognition: atomically FilesEnriched += 1 and FacesFound += facesFound.</summary>
    Task IncrementFaceProgressAsync(int jobId, int facesFound, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically flips Status from Enriching to Completed (setting CompletedUtc) IF FilesEnriched
    /// is already &gt;= FilesFound. Returns whether a row was changed. Safe to call speculatively
    /// after every FilesEnriched increment AND after every Enumerating-&gt;Enriching transition --
    /// whichever of those two events happens last is the one whose call here actually flips it.
    /// </summary>
    Task<bool> TryMarkCompletedIfEnrichedAsync(int scanJobId, DateTime completedUtc, CancellationToken cancellationToken = default);

    /// <summary>Atomically sets FoldersProcessed, FilesFound, Status (Failed or Cancelled), ErrorMessage, CompletedUtc.</summary>
    Task SetFailureResultAsync(int scanJobId, int foldersScanned, int filesFound, string? errorMessage, JobStatus status, DateTime completedUtc, CancellationToken cancellationToken = default);
}
