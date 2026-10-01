using System.Threading;

namespace PictureManager.Application.Common;

/// <summary>
/// In-memory, per-process cancellation for user-cancellable jobs. A job registers when it's queued (so a cancel
/// while it still waits in the queue works too) and is released when its runner finishes. Lost on restart, which
/// is fine: FailInterruptedJobsAsync fails any job a previous process left active.
/// </summary>
public interface IJobCancellationRegistry
{
    /// <summary>The job's token, created on first call. Idempotent: the same job id always gets the same token.</summary>
    CancellationToken Register(int jobId);

    /// <summary>Requests cancellation. False when the job isn't registered (unknown, finished, or not cancellable).</summary>
    bool Cancel(int jobId);

    void Release(int jobId);
}
