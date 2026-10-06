using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;

namespace PictureManager.Application.Faces;

public interface IFaceRecognitionService
{
    /// <summary>
    /// Validates the scope, creates the job (Enumerating, so any other job is refused), registers it for
    /// cancellation and queues it. folderId = that folder; rootId = that root; neither = all active roots.
    /// reanalyze = forget that the photos in scope were analysed first, so every one of them is analysed again.
    /// Throws FaceRecognitionAlreadyInProgressException, ScanRootUnavailableException or FolderUnavailableException.
    /// </summary>
    Task<int> QueueAsync(int? rootId, int? folderId, bool isRecursive, bool reanalyze = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects candidates, analyzes them, clusters, and records the outcome (Completed, Failed or Cancelled).
    /// Rethrows the failure after recording it.
    /// </summary>
    Task RunAsync(QueuedFaceRecognition job, CancellationToken cancellationToken = default);

    /// <summary>Images the current model gave up on. Empty when the models are unavailable.</summary>
    Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(CancellationToken cancellationToken = default);

    /// <summary>Face coverage of every folder subtree that has photos, for the current model. Empty when the models are unavailable.</summary>
    Task<IReadOnlyList<FolderFaceCoverage>> GetFolderCoverageAsync(CancellationToken cancellationToken = default);
}
