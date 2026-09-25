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
}
