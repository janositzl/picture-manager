using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Discovery;

public interface IDiscoveryService
{
    /// <summary>
    /// Validates the request, creates the job (Enumerating, so any other job is refused while this one waits) and
    /// queues the walk. Returns the job id at once. folderId set = discover (refresh) that folder's subtree;
    /// otherwise rootId discovers the whole root from its top folder. isRecursive = false diffs only the target's
    /// direct children, without descending into them. Throws DiscoveryAlreadyInProgressException,
    /// ScanRootUnavailableException or FolderUnavailableException.
    /// </summary>
    Task<int> QueueDiscoveryAsync(int? rootId, int? folderId, bool isRecursive = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks a queued discovery (directory names only, no file processing) and records the outcome on its job
    /// (Completed, Failed with a message, or Cancelled). Rethrows the failure after recording it.
    /// </summary>
    Task RunDiscoveryAsync(QueuedDiscovery discovery, CancellationToken cancellationToken = default);
}
