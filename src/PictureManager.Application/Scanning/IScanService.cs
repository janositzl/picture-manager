using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IScanService
{
    /// <summary>
    /// Validates the request, creates the job (Enumerating, so any other job is refused while this one waits) and
    /// queues the walk. Returns the job id at once. folderId set = scan that folder; otherwise rootId (one root)
    /// or neither (all active roots). Throws ScanAlreadyInProgressException, ScanRootUnavailableException or
    /// FolderUnavailableException.
    /// </summary>
    Task<int> QueueScanAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks a queued scan and records the outcome on its job (Enriching/Completed, Failed with a message, or
    /// Cancelled). Rethrows the failure after recording it.
    /// </summary>
    Task RunScanAsync(QueuedScan scan, CancellationToken cancellationToken = default);

    /// <summary>Fails jobs a previous process left Enumerating/Enriching. Call once at startup. Returns how many.</summary>
    Task<int> FailInterruptedJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enqueues images left Pending (unenriched) by a scan whose enrichment queue was lost -- e.g. an
    /// app restart mid-Enriching, since the queue is in-memory only. Creates a new Job (FolderId null:
    /// spans whatever roots/folders the orphaned images belong to) so the existing per-job completion
    /// tracking (and the UI's progress banner) covers the catch-up too. No-op if a job is already active
    /// or nothing is pending. Call once at startup, after FailInterruptedJobsAsync. Returns how many were
    /// re-enqueued.
    /// </summary>
    Task<int> RequeueStalledEnrichmentAsync(CancellationToken cancellationToken = default);
}
