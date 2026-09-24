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
    private const int MaxScanDepth = 50;
    private const int MaxErrorMessageLength = 4000;
    private const int ProgressInterval = 50;
    private const string InterruptedMessage = "Interrupted by an application restart.";

    private readonly IImageRootRepository _imageRootRepository;
    private readonly IFolderRepository _folderRepository;
    private readonly IImageRepository _imageRepository;
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly IScanJobRepository _scanJobRepository;
    private readonly IEnrichmentQueue _enrichmentQueue;
    private readonly IScanQueue _scanQueue;
    private readonly IClock _clock;
    private readonly ScanningOptions _scanningOptions;

    public ScanService(
        IImageRootRepository imageRootRepository,
        IFolderRepository folderRepository,
        IImageRepository imageRepository,
        IAppSettingsRepository appSettingsRepository,
        IScanJobRepository scanJobRepository,
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

    public async Task<int> QueueScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        if (await _scanJobRepository.HasActiveJobAsync(cancellationToken))
            throw new ScanAlreadyInProgressException();

        if (rootId.HasValue)
            await GetActiveRootAsync(rootId.Value, cancellationToken);

        // Created as Enumerating (not Pending) so HasActiveJobAsync refuses a second scan while this one waits
        // in the queue.
        var scanJob = await _scanJobRepository.AddAsync(new ScanJob
        {
            IsRecursive = isRecursive,
            Status = ScanJobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        _scanQueue.Enqueue(new QueuedScan(scanJob.Id, rootId, isRecursive));
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
            // Resolved again here: an explicit root can be deactivated or deleted while the scan waits in the queue.
            var roots = scan.RootId.HasValue
                ? new[] { await GetActiveRootAsync(scan.RootId.Value, cancellationToken) }
                : (await _imageRootRepository.GetAllAsync(cancellationToken)).Where(r => r.IsActive).ToArray();

            var settings = await _appSettingsRepository.GetAsync(cancellationToken);
            var excludeRules = new ScanExcludeRules(settings, _scanningOptions.SupportedExtensions);
            var unavailableRoots = new List<string>();

            foreach (var root in roots)
            {
                // A missing or empty mount folder is a mount problem, not a deletion: change nothing under it.
                if (!IsRootAvailable(root.MountPath))
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

            await FinalizeSuccessAsync(scan.ScanJobId, foldersScanned, filesFound, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinalizeFailureAsync(scan.ScanJobId, ex, foldersScanned, filesFound);
            throw;
        }
    }

    public Task<int> FailInterruptedJobsAsync(CancellationToken cancellationToken = default) =>
        _scanJobRepository.FailActiveJobsAsync(InterruptedMessage, _clock.UtcNow, cancellationToken);

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

    private async Task FinalizeFailureAsync(int scanJobId, Exception ex, int foldersScanned, int filesFound)
    {
        var isCancellation = ex is OperationCanceledException;
        var status = isCancellation ? ScanJobStatus.Cancelled : ScanJobStatus.Failed;
        var errorMessage = isCancellation ? null : TruncateErrorMessage(ex.Message);

        // CancellationToken.None: if the scan failed because its own token was cancelled, reusing
        // that (now-cancelled) token for this write would itself throw immediately, leaving the job
        // stuck instead of ever recording its terminal status.
        await _scanJobRepository.SetFailureResultAsync(
            scanJobId, foldersScanned, filesFound, errorMessage, status, _clock.UtcNow, CancellationToken.None);
    }

    private static string? TruncateErrorMessage(string? message) =>
        message is { Length: > MaxErrorMessageLength } ? message[..MaxErrorMessageLength] : message;

    private async Task<ImageRoot> GetActiveRootAsync(int rootId, CancellationToken cancellationToken)
    {
        var root = await _imageRootRepository.GetByIdAsync(rootId, cancellationToken);
        return root is { IsActive: true } ? root : throw new ScanRootUnavailableException(rootId);
    }

    private async Task<(int FoldersScanned, int FilesFound)> ScanRootAsync(
        ImageRoot root, bool isRecursive, ScanExcludeRules excludeRules, int scanJobId,
        int foldersBefore, int filesBefore, CancellationToken cancellationToken)
    {
        var foldersScanned = 0;
        var filesFound = 0;

        var rootFolder = await GetOrCreateFolderAsync(root.Id, parentId: null, relativePath: string.Empty, name: root.Name, cancellationToken);
        var pending = new Queue<(Folder Folder, string PhysicalPath, int Depth)>();
        pending.Enqueue((rootFolder, root.MountPath, 0));

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
                    observedFolders.Add(FolderNameKey(name));

                    // Excluded names are skipped here; rows that already exist for them are pruned
                    // after this folder's pass (below).
                    if (excludeRules.IsFolderExcluded(name))
                        continue;

                    var childRelativePath = PathNormalizer.Combine(folder.RelativePath, name);
                    var childFolder = await GetOrCreateFolderAsync(root.Id, folder.Id, childRelativePath, name, cancellationToken);

                    // Removed from the collection (tombstone): never descend into it or re-index it.
                    if (!childFolder.IsActive)
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
                    if (isRecursive && depth < MaxScanDepth)
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
                    var fileModifiedUtc = TruncateToMicroseconds(fileInfo.LastWriteTimeUtc);
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
                            // Not enqueued for enrichment, so it must not count toward FilesFound:
                            // FilesFound drives the background service's FilesEnriched >= FilesFound
                            // completion check, and must equal the number of items actually enqueued.
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
                if (child.IsActive && child.MissingSinceUtc is null && !observedFolders.Contains(FolderNameKey(child.Name)))
                    await _folderRepository.MarkSubtreeMissingAsync(child.Id, _clock.UtcNow, cancellationToken);
            }
        }

        return (foldersScanned, filesFound);
    }

    // A root whose folder is missing or has no entries at all is treated as an unmounted share.
    private static bool IsRootAvailable(string mountPath) =>
        Directory.Exists(mountPath) && Directory.EnumerateFileSystemEntries(mountPath).Any();

    // Same folding as the scanner's path comparisons: NFC, case-insensitive.
    private static string FolderNameKey(string name) => PathNormalizer.Normalize(name).ToLowerInvariant();

    private async Task<Folder> GetOrCreateFolderAsync(int rootId, int? parentId, string relativePath, string name, CancellationToken cancellationToken)
    {
        var existing = await _folderRepository.GetByRootAndRelativePathAsync(rootId, relativePath, cancellationToken);
        if (existing is not null)
            return existing;

        return await _folderRepository.AddAsync(new Folder
        {
            RootId = rootId,
            ParentId = parentId,
            Name = name,
            RelativePath = relativePath,
            CreatedUtc = _clock.UtcNow,
            ModifiedUtc = _clock.UtcNow
        }, cancellationToken);
    }

    // Case- and normalization-insensitive key so the missing-file diff agrees with
    // IImageRepository.GetByFolderAndFileNameAsync's case-insensitive lookup: otherwise a file
    // that's genuinely present (but differs only in case/Unicode normalization from the stored
    // FileName) gets reconciled correctly yet still stamped MissingSinceUtc in the same pass.
    private static (string FileName, string Extension) NormalizeKey(string fileName, string extension) =>
        (PathNormalizer.Normalize(fileName).ToLowerInvariant(), PathNormalizer.Normalize(extension).ToLowerInvariant());

    // Postgres timestamptz stores microsecond precision; FileInfo.LastWriteTimeUtc carries NTFS's
    // 100ns tick resolution. Truncate before comparing/storing so a round-tripped value compares
    // equal to itself on the next scan instead of looking "Modified" forever.
    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % 10), value.Kind);
}
