using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

public sealed class ScanService : IScanService
{
    private const int ProgressInterval = 50;
    private const string InterruptedMessage = "Interrupted by an application restart.";

    private readonly IImageRootRepository _imageRootRepository;
    private readonly IFolderRepository _folderRepository;
    private readonly IImageRepository _imageRepository;
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly IJobRepository _scanJobRepository;
    private readonly IEnrichmentQueue _enrichmentQueue;
    private readonly IScanQueue _scanQueue;
    private readonly IClock _clock;
    private readonly ScanningOptions _scanningOptions;

    public ScanService(
        IImageRootRepository imageRootRepository,
        IFolderRepository folderRepository,
        IImageRepository imageRepository,
        IAppSettingsRepository appSettingsRepository,
        IJobRepository scanJobRepository,
        IEnrichmentQueue enrichmentQueue,
        IScanQueue scanQueue,
        IClock clock,
        ScanningOptions scanningOptions)
    {
        _imageRootRepository = imageRootRepository;
        _folderRepository = folderRepository;
        _imageRepository = imageRepository;
        _appSettingsRepository = appSettingsRepository;
        _scanJobRepository = scanJobRepository;
        _enrichmentQueue = enrichmentQueue;
        _scanQueue = scanQueue;
        _clock = clock;
        _scanningOptions = scanningOptions;
    }

    public async Task<int> QueueScanAsync(int? rootId, int? folderId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        if (await _scanJobRepository.HasActiveJobAsync(cancellationToken))
            throw new ScanAlreadyInProgressException();

        var jobFolderId = await ScanTargets.ResolveJobFolderIdAsync(_folderRepository, _imageRootRepository, rootId, folderId, cancellationToken);

        if (jobFolderId.HasValue)
            await _folderRepository.SetScanStatusAsync(jobFolderId.Value, FolderScanStatus.Scanning, cancellationToken);

        // Created as Enumerating (not Pending) so HasActiveJobAsync refuses any other job while this one waits
        // in the queue.
        var scanJob = await _scanJobRepository.AddAsync(new Job
        {
            Kind = JobKind.Scan,
            FolderId = jobFolderId,
            IsRecursive = isRecursive,
            Status = JobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        _scanQueue.Enqueue(new QueuedScan(scanJob.Id, folderId.HasValue ? null : rootId, folderId, isRecursive));
        return scanJob.Id;
    }

    public async Task RunScanAsync(QueuedScan scan, CancellationToken cancellationToken = default)
    {
        // Declared outside the try so the catch block can report how far the scan got before
        // failing (see FinalizeFailureAsync).
        var foldersScanned = 0;
        var filesFound = 0;

        try
        {
            if (scan.FolderId.HasValue)
            {
                (foldersScanned, filesFound) = await ScanFolderTargetAsync(scan.FolderId.Value, scan, cancellationToken);
            }
            else
            {
                // Resolved again here: an explicit root can be deactivated or deleted while the scan waits in the queue.
                var roots = scan.RootId.HasValue
                    ? new[] { await ScanTargets.GetActiveRootAsync(_imageRootRepository, scan.RootId.Value, cancellationToken) }
                    : (await _imageRootRepository.GetAllAsync(cancellationToken)).Where(r => r.IsActive).ToArray();

                var settings = await _appSettingsRepository.GetAsync(cancellationToken);
                var excludeRules = new ScanExcludeRules(settings, _scanningOptions.SupportedExtensions);
                var unavailableRoots = new List<string>();

                foreach (var root in roots)
                {
                    // A missing or empty mount folder is a mount problem, not a deletion: change nothing under it.
                    if (!ScanTargets.IsRootAvailable(root.MountPath))
                    {
                        unavailableRoots.Add(root.Name);
                        continue;
                    }

                    var (rootFoldersScanned, rootFilesFound) = await ScanRootAsync(
                        root, scan.IsRecursive, excludeRules, scan.ScanJobId, foldersScanned, filesFound, cancellationToken);
                    foldersScanned += rootFoldersScanned;
                    filesFound += rootFilesFound;
                }

                // The available roots were scanned in full; the job still fails so the user sees the warning.
                if (unavailableRoots.Count > 0)
                    throw new ScanRootsUnavailableException(unavailableRoots);
            }

            await FinalizeSuccessAsync(scan.ScanJobId, foldersScanned, filesFound, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinalizeFailureAsync(scan.ScanJobId, ex, foldersScanned, filesFound, scan.FolderId);
            throw;
        }
    }

    public Task<int> FailInterruptedJobsAsync(CancellationToken cancellationToken = default) =>
        _scanJobRepository.FailActiveJobsAsync(InterruptedMessage, _clock.UtcNow, cancellationToken);

    public Task<int> RequeueStalledEnrichmentAsync(CancellationToken cancellationToken = default) =>
        RequeueAsync(_imageRepository.GetPendingImageIdsAsync, cancellationToken);

    public Task<int> RequeueMissingPerceptualHashAsync(CancellationToken cancellationToken = default) =>
        RequeueAsync(_imageRepository.GetIdsMissingPerceptualHashAsync, cancellationToken);

    private async Task<int> RequeueAsync(Func<CancellationToken, Task<IReadOnlyList<int>>> getImageIds, CancellationToken cancellationToken)
    {
        if (await _scanJobRepository.HasActiveJobAsync(cancellationToken))
            return 0;

        var imageIds = await getImageIds(cancellationToken);
        if (imageIds.Count == 0)
            return 0;

        var job = await _scanJobRepository.AddAsync(new Job
        {
            Kind = JobKind.Scan,
            FolderId = null,
            IsRecursive = true,
            Status = JobStatus.Enriching,
            FilesFound = imageIds.Count,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        foreach (var imageId in imageIds)
            _enrichmentQueue.Enqueue(job.Id, imageId);

        return imageIds.Count;
    }

    private async Task<(int FoldersScanned, int FilesFound)> ScanFolderTargetAsync(int folderId, QueuedScan scan, CancellationToken cancellationToken)
    {
        // Resolved again here: the folder can be removed, or its root deactivated, while the scan waits in the queue.
        var (root, folder) = await ScanTargets.GetVisibleFolderAsync(_folderRepository, _imageRootRepository, folderId, cancellationToken);

        // Same rule as a whole-root scan: an unmounted share changes nothing.
        if (!ScanTargets.IsRootAvailable(root.MountPath))
            throw new ScanRootsUnavailableException(new[] { root.Name });

        // Gone from disk: fail without marking anything; a scan or discovery of its parent does that.
        var folderPath = ImagePathResolver.ResolveFolderPath(root.MountPath, folder.RelativePath);
        if (!Directory.Exists(folderPath))
            throw new FolderNotOnDiskException(root.Name, folder.RelativePath);

        var settings = await _appSettingsRepository.GetAsync(cancellationToken);
        var excludeRules = new ScanExcludeRules(settings, _scanningOptions.SupportedExtensions);
        var result = await ScanTreeAsync(root, folder, folderPath, scan.IsRecursive, excludeRules, scan.ScanJobId, 0, 0, cancellationToken);

        await _folderRepository.MarkSubtreeScannedAsync(folder.Id, _clock.UtcNow, cancellationToken);
        return result;
    }

    private async Task FinalizeSuccessAsync(int scanJobId, int foldersScanned, int filesFound, CancellationToken cancellationToken)
    {
        // Every write below is a targeted, atomic SQL UPDATE (ExecuteUpdateAsync), not a whole-row
        // read-modify-write: EnrichmentBackgroundService concurrently increments FilesEnriched (and
        // can itself flip Status to Completed) on this same row from a different DbContext scope, on
        // every scan with enough files that draining starts before the walk finishes. A whole-row
        // write here would silently overwrite whatever it just committed, and vice versa -- this was a
        // reachable production race, not a hypothetical one.
        await _scanJobRepository.SetEnumerationResultAsync(scanJobId, foldersScanned, filesFound, cancellationToken);
        await _scanJobRepository.TryTransitionToEnrichingAsync(scanJobId, cancellationToken);

        // Whichever of "transition to Enriching" (just above) or the background service's last
        // FilesEnriched increment happens last is the one that will see FilesEnriched >= FilesFound
        // and flip the job to Completed -- so this must be attempted here too, not just from
        // EnrichmentBackgroundService.MarkOneEnrichedAsync. When filesFound == 0 this also correctly
        // completes the job immediately (FilesEnriched 0 >= FilesFound 0).
        await _scanJobRepository.TryMarkCompletedIfEnrichedAsync(scanJobId, _clock.UtcNow, cancellationToken);
    }

    private async Task FinalizeFailureAsync(int scanJobId, Exception ex, int foldersScanned, int filesFound, int? folderId)
    {
        var isCancellation = ex is OperationCanceledException;
        var status = isCancellation ? JobStatus.Cancelled : JobStatus.Failed;
        var errorMessage = isCancellation ? null : ScanTargets.TruncateErrorMessage(ex.Message);

        // CancellationToken.None: if the scan failed because its own token was cancelled, reusing
        // that (now-cancelled) token for this write would itself throw immediately, leaving the job
        // stuck instead of ever recording its terminal status.
        await _scanJobRepository.SetFailureResultAsync(
            scanJobId, foldersScanned, filesFound, errorMessage, status, _clock.UtcNow, CancellationToken.None);

        // Cancellation isn't an error; the folder is left however the (interrupted) walk left it.
        if (folderId.HasValue && !isCancellation)
            await _folderRepository.SetScanStatusAsync(folderId.Value, FolderScanStatus.Error, CancellationToken.None);
    }

    private async Task<(int FoldersScanned, int FilesFound)> ScanRootAsync(
        ImageRoot root, bool isRecursive, ScanExcludeRules excludeRules, int scanJobId,
        int foldersBefore, int filesBefore, CancellationToken cancellationToken)
    {
        var rootFolder = await ScanTargets.GetOrCreateFolderAsync(_folderRepository, _clock, root.Id, parentId: null, relativePath: string.Empty, name: root.Name, cancellationToken);
        var result = await ScanTreeAsync(root, rootFolder, root.MountPath, isRecursive, excludeRules, scanJobId, foldersBefore, filesBefore, cancellationToken);

        await _folderRepository.MarkSubtreeScannedAsync(rootFolder.Id, _clock.UtcNow, cancellationToken);
        return result;
    }

    private async Task<(int FoldersScanned, int FilesFound)> ScanTreeAsync(
        ImageRoot root, Folder startFolder, string startPath, bool isRecursive, ScanExcludeRules excludeRules, int scanJobId,
        int foldersBefore, int filesBefore, CancellationToken cancellationToken)
    {
        var foldersScanned = 0;
        var filesFound = 0;

        var pending = new Queue<(Folder Folder, string PhysicalPath, int Depth)>();
        pending.Enqueue((startFolder, startPath, 0));

        while (pending.Count > 0)
        {
            var (folder, physicalPath, depth) = pending.Dequeue();
            foldersScanned++;

            // Live progress for the UI; totals include the roots scanned before this one.
            if (foldersScanned % ProgressInterval == 0)
                await _scanJobRepository.SetEnumerationResultAsync(scanJobId, foldersBefore + foldersScanned, filesBefore + filesFound, cancellationToken);

            if (!Directory.Exists(physicalPath))
                continue;

            var observedFiles = new HashSet<(string FileName, string Extension)>();
            var observedFolders = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entryPath in Directory.EnumerateFileSystemEntries(physicalPath))
            {
                var name = Path.GetFileName(entryPath);

                if (Directory.Exists(entryPath))
                {
                    // Every directory on disk counts as seen, excluded or not: excluded ones are pruned below,
                    // never marked missing.
                    observedFolders.Add(PathNormalizer.FolderNameKey(name));

                    // Excluded names are skipped here; rows that already exist for them are pruned
                    // after this folder's pass (below).
                    if (excludeRules.IsFolderExcluded(name))
                        continue;

                    var childRelativePath = PathNormalizer.Combine(folder.RelativePath, name);
                    var childFolder = await ScanTargets.GetOrCreateFolderAsync(_folderRepository, _clock, root.Id, folder.Id, childRelativePath, name, cancellationToken);

                    // Removed from the collection (tombstone): never descend into it or re-index it.
                    if (!childFolder.IsActive)
                        continue;

                    // Excluded: never descend into it or scan its files. Its subtree is left exactly as
                    // it was, and it still counts as observed above, so it's never marked missing either.
                    if (childFolder.IsExcluded)
                        continue;

                    // Back on disk (remounted, or renamed back): clear the mark. Its descendants are cleared as
                    // the walk reaches them.
                    if (childFolder.MissingSinceUtc is not null)
                    {
                        childFolder.MissingSinceUtc = null;
                        childFolder.ModifiedUtc = _clock.UtcNow;
                        await _folderRepository.UpdateAsync(childFolder, cancellationToken);
                    }

                    // Always create the child Folder row for tree visibility, but stop descending
                    // once isRecursive is false, or once a depth cap is hit (guards against unbounded
                    // growth from a symlink/junction cycle).
                    if (isRecursive && depth < ScanTargets.MaxFolderDepth)
                        pending.Enqueue((childFolder, entryPath, depth + 1));
                }
                else
                {
                    var extension = Path.GetExtension(name).ToLowerInvariant();
                    var fileNameWithoutExtension = PathNormalizer.Normalize(Path.GetFileNameWithoutExtension(name));

                    if (!excludeRules.IsExtensionAllowed(extension))
                        continue;

                    observedFiles.Add(NormalizeKey(fileNameWithoutExtension, extension));

                    var fileInfo = new FileInfo(entryPath);
                    var fileModifiedUtc = PostgresTimestamps.TruncateToMicroseconds(fileInfo.LastWriteTimeUtc);
                    var existingImage = await _imageRepository.GetByFolderAndFileNameAsync(folder.Id, fileNameWithoutExtension, extension, cancellationToken);
                    var decision = ImageReconciler.Decide(existingImage, fileInfo.Length, fileModifiedUtc);

                    switch (decision)
                    {
                        case ReconcileAction.New:
                            var newImage = await _imageRepository.AddAsync(new Image
                            {
                                FolderId = folder.Id,
                                FileName = fileNameWithoutExtension,
                                Extension = extension,
                                ContentHash = string.Empty,
                                FileSize = fileInfo.Length,
                                FileModified = fileModifiedUtc,
                                FirstSeenUtc = _clock.UtcNow,
                                CreatedAt = _clock.UtcNow,
                                UpdatedAt = _clock.UtcNow,
                                IndexState = IndexState.Pending
                            }, cancellationToken);
                            _enrichmentQueue.Enqueue(scanJobId, newImage.Id);
                            filesFound++;
                            break;

                        case ReconcileAction.Modified:
                            existingImage!.FileSize = fileInfo.Length;
                            existingImage.FileModified = fileModifiedUtc;
                            existingImage.IndexState = IndexState.Pending;
                            existingImage.MissingSinceUtc = null;
                            existingImage.UpdatedAt = _clock.UtcNow;
                            await _imageRepository.UpdateAsync(existingImage, cancellationToken);
                            _enrichmentQueue.Enqueue(scanJobId, existingImage.Id);
                            filesFound++;
                            break;

                        case ReconcileAction.Unchanged:
                            // Unchanged files are normally not re-enriched. Exception: indexed before perceptual
                            // hashing existed (PerceptualHash null; "" means tried-and-undecodable, don't retry).
                            // When enqueued it must count toward FilesFound (see completion check note below).
                            if (existingImage!.IndexState == IndexState.Indexed && existingImage.PerceptualHash is null)
                            {
                                _enrichmentQueue.Enqueue(scanJobId, existingImage.Id);
                                filesFound++;
                            }
                            // Otherwise not enqueued, so it must not count toward FilesFound: FilesFound drives the
                            // background service's FilesEnriched >= FilesFound completion check.
                            break;
                    }
                }
            }

            var existingImages = await _imageRepository.GetByFolderIdAsync(folder.Id, cancellationToken);
            foreach (var image in existingImages)
            {
                // Prune on scan: an extension the settings now exclude removes the row (and, by
                // cascade, its album entries) instead of leaving it marked missing forever.
                if (!excludeRules.IsExtensionAllowed(image.Extension))
                {
                    await _imageRepository.DeleteAsync(image, cancellationToken);
                    continue;
                }

                if (image.MissingSinceUtc is null && !observedFiles.Contains(NormalizeKey(image.FileName, image.Extension)))
                {
                    image.MissingSinceUtc = _clock.UtcNow;
                    image.UpdatedAt = _clock.UtcNow;
                    await _imageRepository.UpdateAsync(image, cancellationToken);
                }
            }

            foreach (var child in await _folderRepository.GetChildrenAsync(folder.Id, cancellationToken))
            {
                // Prune on scan: child folders whose name is now excluded go with their whole subtree.
                // No tombstone is left, because the exclusion rule itself keeps them out, and removing
                // the rule brings them back on the next scan.
                if (excludeRules.IsFolderExcluded(child.Name))
                {
                    await _folderRepository.DeleteSubtreeAsync(child.Id, cancellationToken);
                    continue;
                }

                // Gone from disk (renamed, moved or deleted): mark it and its subtree missing. Nothing is
                // deleted -- the user decides whether to remove it, and it's unmarked if it comes back.
                if (child.IsActive && child.MissingSinceUtc is null && !observedFolders.Contains(PathNormalizer.FolderNameKey(child.Name)))
                    await _folderRepository.MarkSubtreeMissingAsync(child.Id, _clock.UtcNow, cancellationToken);
            }
        }

        return (foldersScanned, filesFound);
    }

    // Case- and normalization-insensitive key so the missing-file diff agrees with
    // IImageRepository.GetByFolderAndFileNameAsync's case-insensitive lookup: otherwise a file
    // that's genuinely present (but differs only in case/Unicode normalization from the stored
    // FileName) gets reconciled correctly yet still stamped MissingSinceUtc in the same pass.
    private static (string FileName, string Extension) NormalizeKey(string fileName, string extension) =>
        (PathNormalizer.Normalize(fileName).ToLowerInvariant(), PathNormalizer.Normalize(extension).ToLowerInvariant());
}
