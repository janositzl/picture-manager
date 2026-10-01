using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Application.Faces;

/// <summary>
/// The face recognition job: DB-selected candidates only (never a NAS walk), analyzed in parallel with one DI
/// scope per image, so each image commits on its own and a cancelled or interrupted job resumes on re-run.
/// </summary>
public sealed class FaceRecognitionService : IFaceRecognitionService
{
    private readonly IFolderRepository _folderRepository;
    private readonly IImageRootRepository _imageRootRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IFaceRepository _faceRepository;
    private readonly IFaceAnalyzer _analyzer;
    private readonly IFaceClusterer _clusterer;
    private readonly IFaceRecognitionQueue _queue;
    private readonly IJobCancellationRegistry _cancellations;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FaceRecognitionOptions _options;
    private readonly IClock _clock;

    public FaceRecognitionService(
        IFolderRepository folderRepository,
        IImageRootRepository imageRootRepository,
        IJobRepository jobRepository,
        IFaceRepository faceRepository,
        IFaceAnalyzer analyzer,
        IFaceClusterer clusterer,
        IFaceRecognitionQueue queue,
        IJobCancellationRegistry cancellations,
        IServiceScopeFactory scopeFactory,
        FaceRecognitionOptions options,
        IClock clock)
    {
        _folderRepository = folderRepository;
        _imageRootRepository = imageRootRepository;
        _jobRepository = jobRepository;
        _faceRepository = faceRepository;
        _analyzer = analyzer;
        _clusterer = clusterer;
        _queue = queue;
        _cancellations = cancellations;
        _scopeFactory = scopeFactory;
        _options = options;
        _clock = clock;
    }

    public async Task<int> QueueAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        if (await _jobRepository.HasActiveJobAsync(cancellationToken))
            throw new FaceRecognitionAlreadyInProgressException();

        var jobFolderId = await ScanTargets.ResolveJobFolderIdAsync(_folderRepository, _imageRootRepository, rootId, folderId, cancellationToken);

        // Created as Enumerating (not Pending) so HasActiveJobAsync refuses any other job while this one waits.
        var job = await _jobRepository.AddAsync(new Job
        {
            Kind = JobKind.FaceRecognition,
            FolderId = jobFolderId,
            IsRecursive = isRecursive,
            Status = JobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        // Registered before enqueueing, so a cancel that arrives while the job still waits in the queue works.
        _cancellations.Register(job.Id);
        _queue.Enqueue(new QueuedFaceRecognition(job.Id, jobFolderId, isRecursive));
        return job.Id;
    }

    public async Task RunAsync(QueuedFaceRecognition job, CancellationToken cancellationToken = default)
    {
        // Declared outside the try so a failure doesn't zero the progress the UI already showed.
        var candidates = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var faceModelId = await _faceRepository.GetOrCreateModelIdAsync(_analyzer.Model, _clock.UtcNow, cancellationToken);

            var imageIds = await _faceRepository.GetCandidateImageIdsAsync(faceModelId, job.FolderId, job.IsRecursive, cancellationToken);
            candidates = imageIds.Count;
            await _jobRepository.SetEnumerationResultAsync(job.JobId, foldersScanned: 0, filesFound: candidates, cancellationToken);
            await _jobRepository.TryTransitionToEnrichingAsync(job.JobId, cancellationToken);

            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, _options.ReadConcurrency + _options.InferenceConcurrency),
                CancellationToken = cancellationToken
            };
            await Parallel.ForEachAsync(imageIds, parallel, async (imageId, token) =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IFaceImageProcessor>();
                var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();

                var result = await processor.ProcessAsync(imageId, faceModelId, token);
                await jobs.IncrementFaceProgressAsync(job.JobId, result.FacesFound, token);
            });

            // Global, current model only. Runs after every job so newly detected faces join people at once.
            await _clusterer.ClusterAsync(faceModelId, cancellationToken);

            await _jobRepository.TryMarkCompletedAsync(job.JobId, JobStatus.Enriching, _clock.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinalizeFailureAsync(job.JobId, ex, candidates);
            throw;
        }
    }

    public async Task<IReadOnlyList<FaceFailure>> GetPermanentFailuresAsync(CancellationToken cancellationToken = default)
    {
        FaceModelDescriptor model;
        try
        {
            model = _analyzer.Model;
        }
        catch (FaceModelUnavailableException)
        {
            return Array.Empty<FaceFailure>();
        }

        var faceModelId = await _faceRepository.GetOrCreateModelIdAsync(model, _clock.UtcNow, cancellationToken);
        return await _faceRepository.GetPermanentFailuresAsync(faceModelId, cancellationToken);
    }

    private async Task FinalizeFailureAsync(int jobId, Exception ex, int candidates)
    {
        var isCancellation = ex is OperationCanceledException;
        var status = isCancellation ? JobStatus.Cancelled : JobStatus.Failed;
        var errorMessage = isCancellation ? null : ScanTargets.TruncateErrorMessage(ex.Message);

        // CancellationToken.None: the job's own token may be the one that just fired.
        await _jobRepository.SetFailureResultAsync(
            jobId, foldersScanned: 0, filesFound: candidates, errorMessage, status, _clock.UtcNow, CancellationToken.None);
    }
}
