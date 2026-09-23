using System;
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
    // place. Kept as a general-purpose "refresh a tracked instance from the DB" primitive and
    // still exercised by its own test documenting the EF Core identity-resolution behavior below
    // -- but note ScanService no longer uses this for ScanJob finalization (see
    // SetEnumerationResultAsync / TryTransitionToEnrichingAsync / TryMarkCompletedIfEnrichedAsync
    // / SetFailureResultAsync below), because holding one tracked instance across a scan's whole
    // duration and later doing a whole-row write from it is exactly the pattern that raced against
    // EnrichmentBackgroundService's concurrent writes to the same row.
    Task ReloadAsync(ScanJob scanJob, CancellationToken cancellationToken = default);

    /// <summary>True if any ScanJob is currently Enumerating or Enriching.</summary>
    Task<bool> HasActiveJobAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically sets FoldersScanned and FilesFound only. Never touches Status.</summary>
    Task SetEnumerationResultAsync(int scanJobId, int foldersScanned, int filesFound, CancellationToken cancellationToken = default);

    /// <summary>Atomically flips Status from Enumerating to Enriching. Returns whether a row was changed.</summary>
    Task<bool> TryTransitionToEnrichingAsync(int scanJobId, CancellationToken cancellationToken = default);

    /// <summary>Atomically increments FilesEnriched by 1.</summary>
    Task IncrementFilesEnrichedAsync(int scanJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically flips Status from Enriching to Completed (setting CompletedUtc) IF FilesEnriched
    /// is already &gt;= FilesFound. Returns whether a row was changed. Safe to call speculatively
    /// after every FilesEnriched increment AND after every Enumerating-&gt;Enriching transition --
    /// whichever of those two events happens last is the one whose call here actually flips it.
    /// </summary>
    Task<bool> TryMarkCompletedIfEnrichedAsync(int scanJobId, DateTime completedUtc, CancellationToken cancellationToken = default);

    /// <summary>Atomically sets FoldersScanned, FilesFound, Status (Failed or Cancelled), ErrorMessage, CompletedUtc.</summary>
    Task SetFailureResultAsync(int scanJobId, int foldersScanned, int filesFound, string? errorMessage, ScanJobStatus status, DateTime completedUtc, CancellationToken cancellationToken = default);
}
