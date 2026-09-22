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

    private readonly IImageRootRepository _imageRootRepository;
    private readonly IFolderRepository _folderRepository;
    private readonly IImageRepository _imageRepository;
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly IScanJobRepository _scanJobRepository;
    private readonly IEnrichmentQueue _enrichmentQueue;
    private readonly IClock _clock;

    public ScanService(
        IImageRootRepository imageRootRepository,
        IFolderRepository folderRepository,
        IImageRepository imageRepository,
        IAppSettingsRepository appSettingsRepository,
        IScanJobRepository scanJobRepository,
        IEnrichmentQueue enrichmentQueue,
        IClock clock)
    {
        _imageRootRepository = imageRootRepository;
        _folderRepository = folderRepository;
        _imageRepository = imageRepository;
        _appSettingsRepository = appSettingsRepository;
        _scanJobRepository = scanJobRepository;
        _enrichmentQueue = enrichmentQueue;
        _clock = clock;
    }

    public async Task<int> StartScanAsync(int? rootId, bool isRecursive, CancellationToken cancellationToken = default)
    {
        var roots = rootId.HasValue
            ? new[] { await _imageRootRepository.GetByIdAsync(rootId.Value, cancellationToken)
                ?? throw new InvalidOperationException($"ImageRoot {rootId} not found.") }
            : (await _imageRootRepository.GetAllAsync(cancellationToken)).Where(r => r.IsActive).ToArray();

        var scanJob = await _scanJobRepository.AddAsync(new ScanJob
        {
            IsRecursive = isRecursive,
            Status = ScanJobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        try
        {
            var settings = await _appSettingsRepository.GetAsync(cancellationToken);
            var excludeRules = new ScanExcludeRules(settings);

            var foldersScanned = 0;
            var filesFound = 0;

            foreach (var root in roots)
            {
                var (rootFoldersScanned, rootFilesFound) = await ScanRootAsync(root, isRecursive, excludeRules, scanJob.Id, cancellationToken);
                foldersScanned += rootFoldersScanned;
                filesFound += rootFilesFound;
            }

            await FinalizeSuccessAsync(scanJob, foldersScanned, filesFound, cancellationToken);

            return scanJob.Id;
        }
        catch (Exception ex)
        {
            await FinalizeFailureAsync(scanJob, ex, cancellationToken);
            throw;
        }
    }

    private async Task FinalizeSuccessAsync(ScanJob originalScanJob, int foldersScanned, int filesFound, CancellationToken cancellationToken)
    {
        // Re-fetch the current row rather than writing through the stale in-memory snapshot:
        // EnrichmentBackgroundService may have concurrently advanced FilesEnriched/Status while
        // this scan was still walking the tree, and a blind full-row write here would clobber that.
        var job = await _scanJobRepository.GetByIdAsync(originalScanJob.Id, cancellationToken) ?? originalScanJob;

        job.FoldersScanned = foldersScanned;
        job.FilesFound = filesFound;

        if (job.Status != ScanJobStatus.Completed && job.Status != ScanJobStatus.Failed)
        {
            if (filesFound == 0)
            {
                job.Status = ScanJobStatus.Completed;
                job.CompletedUtc = _clock.UtcNow;
            }
            else
            {
                job.Status = ScanJobStatus.Enriching;

                // Self-correct: EnrichmentBackgroundService (guarded by its own Status ==
                // Enriching check, see MarkOneEnrichedAsync) may have already raced ahead and
                // finished enriching everything before this write set Status to Enriching in the
                // first place -- FilesFound was still 0 the whole time it was draining the queue,
                // so it never had a true count to complete against. Once we know the true
                // FilesFound here, if enrichment already caught up, finish the job now: nothing
                // else will trigger MarkOneEnrichedAsync again once the queue is drained.
                if (job.FilesEnriched >= filesFound)
                {
                    job.Status = ScanJobStatus.Completed;
                    job.CompletedUtc = _clock.UtcNow;
                }
            }
        }

        await _scanJobRepository.UpdateAsync(job, cancellationToken);
    }

    private async Task FinalizeFailureAsync(ScanJob originalScanJob, Exception ex, CancellationToken cancellationToken)
    {
        var job = await _scanJobRepository.GetByIdAsync(originalScanJob.Id, cancellationToken) ?? originalScanJob;

        job.Status = ScanJobStatus.Failed;
        job.ErrorMessage = TruncateErrorMessage(ex.Message);
        job.CompletedUtc = _clock.UtcNow;

        await _scanJobRepository.UpdateAsync(job, cancellationToken);
    }

    private static string? TruncateErrorMessage(string? message) =>
        message is { Length: > MaxErrorMessageLength } ? message[..MaxErrorMessageLength] : message;

    private async Task<(int FoldersScanned, int FilesFound)> ScanRootAsync(
        ImageRoot root, bool isRecursive, ScanExcludeRules excludeRules, int scanJobId, CancellationToken cancellationToken)
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

            if (!Directory.Exists(physicalPath))
                continue;

            var observedFiles = new HashSet<(string FileName, string Extension)>();

            foreach (var entryPath in Directory.EnumerateFileSystemEntries(physicalPath))
            {
                var name = Path.GetFileName(entryPath);

                if (Directory.Exists(entryPath))
                {
                    if (excludeRules.IsFolderExcluded(name))
                        continue;

                    var childRelativePath = PathNormalizer.Combine(folder.RelativePath, name);
                    var childFolder = await GetOrCreateFolderAsync(root.Id, folder.Id, childRelativePath, name, cancellationToken);

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
                if (image.MissingSinceUtc is null && !observedFiles.Contains(NormalizeKey(image.FileName, image.Extension)))
                {
                    image.MissingSinceUtc = _clock.UtcNow;
                    image.UpdatedAt = _clock.UtcNow;
                    await _imageRepository.UpdateAsync(image, cancellationToken);
                }
            }
        }

        return (foldersScanned, filesFound);
    }

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
