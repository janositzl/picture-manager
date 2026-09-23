using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Scanning;

public interface IScanService
{
    /// <summary>
    /// Validates the request, creates the job (Enumerating, so a second scan is refused while this one waits) and
    /// queues the walk. Returns the job id at once. Throws ScanAlreadyInProgressException or
    /// ScanRootUnavailableException.
    /// </summary>
    Task<int> QueueScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks a queued scan and records the outcome on its job (Enriching/Completed, Failed with a message, or
    /// Cancelled). Rethrows the failure after recording it.
    /// </summary>
    Task RunScanAsync(QueuedScan scan, CancellationToken cancellationToken = default);

    /// <summary>Fails jobs a previous process left Enumerating/Enriching. Call once at startup. Returns how many.</summary>
    Task<int> FailInterruptedJobsAsync(CancellationToken cancellationToken = default);
}
