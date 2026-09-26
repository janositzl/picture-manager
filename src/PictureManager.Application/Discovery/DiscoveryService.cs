using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Application.Discovery;

/// <summary>
/// Cheap, directory-names-only walk that builds/refreshes the folder tree, separate from (and independent of)
/// image scanning. Shares the Job row and the "only one job at a time" rule with ScanService.
/// </summary>
public sealed class DiscoveryService : IDiscoveryService
{
    private const int ProgressInterval = 50;

    private readonly IImageRootRepository _imageRootRepository;
    private readonly IFolderRepository _folderRepository;
    private readonly IAppSettingsRepository _appSettingsRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IDiscoveryQueue _discoveryQueue;
    private readonly IClock _clock;

    public DiscoveryService(
        IImageRootRepository imageRootRepository,
        IFolderRepository folderRepository,
        IAppSettingsRepository appSettingsRepository,
        IJobRepository jobRepository,
        IDiscoveryQueue discoveryQueue,
        IClock clock)
    {
        _imageRootRepository = imageRootRepository;
        _folderRepository = folderRepository;
        _appSettingsRepository = appSettingsRepository;
        _jobRepository = jobRepository;
        _discoveryQueue = discoveryQueue;
        _clock = clock;
    }

    public async Task<int> QueueDiscoveryAsync(int? rootId, int? folderId, CancellationToken cancellationToken = default)
    {
        if (await _jobRepository.HasActiveJobAsync(cancellationToken))
            throw new DiscoveryAlreadyInProgressException();

        int? jobFolderId;
        if (folderId.HasValue)
        {
            var (_, folder) = await ScanTargets.GetVisibleFolderAsync(_folderRepository, _imageRootRepository, folderId.Value, cancellationToken);

            // The walk couldn't reach it; scanning or discovering its parent is what notices it's back.
            if (folder.MissingSinceUtc is not null)
                throw FolderUnavailableException.Missing(folderId.Value);

            if (folder.IsExcluded || await _folderRepository.HasExcludedAncestorAsync(folder.Id, cancellationToken))
                throw FolderUnavailableException.Excluded(folderId.Value);

            jobFolderId = folder.Id;
        }
        else if (rootId.HasValue)
        {
            await ScanTargets.GetActiveRootAsync(_imageRootRepository, rootId.Value, cancellationToken);

            // Every root has a top folder (ImageRootSeeder); it's the job's recorded scope.
            jobFolderId = (await _folderRepository.GetByRootAndRelativePathAsync(rootId.Value, string.Empty, cancellationToken))?.Id;
        }
        else
        {
            throw new ArgumentException("Either rootId or folderId must be specified.");
        }

        // Created as Enumerating (not Pending) so HasActiveJobAsync refuses any other job while this one waits
        // in the queue.
        var discoveryJob = await _jobRepository.AddAsync(new Job
        {
            Kind = JobKind.Discovery,
            FolderId = jobFolderId,
            IsRecursive = true,
            Status = JobStatus.Enumerating,
            StartedUtc = _clock.UtcNow
        }, cancellationToken);

        _discoveryQueue.Enqueue(new QueuedDiscovery(discoveryJob.Id, folderId.HasValue ? null : rootId, folderId));
        return discoveryJob.Id;
    }

    public async Task RunDiscoveryAsync(QueuedDiscovery discovery, CancellationToken cancellationToken = default)
    {
        // Declared outside the try so the catch block can report how far the walk got before failing.
        var foldersDiscovered = 0;

        try
        {
            ImageRoot root;
            Folder startFolder;
            string startPath;

            if (discovery.FolderId.HasValue)
            {
                // Resolved again here: the folder can be removed, or its root deactivated, while it waits in the queue.
                (root, startFolder) = await ScanTargets.GetVisibleFolderAsync(_folderRepository, _imageRootRepository, discovery.FolderId.Value, cancellationToken);

                if (!ScanTargets.IsRootAvailable(root.MountPath))
                    throw new ScanRootsUnavailableException(new[] { root.Name });

                startPath = ImagePathResolver.ResolveFolderPath(root.MountPath, startFolder.RelativePath);
                if (!Directory.Exists(startPath))
                    throw new FolderNotOnDiskException(root.Name, startFolder.RelativePath);
            }
            else
            {
                // Resolved again here: an explicit root can be deactivated or deleted while it waits in the queue.
                root = await ScanTargets.GetActiveRootAsync(_imageRootRepository, discovery.RootId!.Value, cancellationToken);

                if (!ScanTargets.IsRootAvailable(root.MountPath))
                    throw new ScanRootsUnavailableException(new[] { root.Name });

                startFolder = await ScanTargets.GetOrCreateFolderAsync(_folderRepository, _clock, root.Id, parentId: null, relativePath: string.Empty, name: root.Name, cancellationToken);
                startPath = root.MountPath;
            }

            var settings = await _appSettingsRepository.GetAsync(cancellationToken);
            var excludeRules = new ScanExcludeRules(settings, Array.Empty<string>());

            foldersDiscovered = await DiscoverTreeAsync(root, startFolder, startPath, excludeRules, discovery.DiscoveryJobId, cancellationToken);

            await FinalizeSuccessAsync(discovery.DiscoveryJobId, foldersDiscovered, cancellationToken);
        }
        catch (Exception ex)
        {
            await FinalizeFailureAsync(discovery.DiscoveryJobId, ex, foldersDiscovered);
            throw;
        }
    }

    private async Task<int> DiscoverTreeAsync(
        ImageRoot root, Folder startFolder, string startPath, ScanExcludeRules excludeRules, int discoveryJobId, CancellationToken cancellationToken)
    {
        var foldersDiscovered = 0;

        var pending = new Queue<(Folder Folder, string PhysicalPath, int Depth)>();
        pending.Enqueue((startFolder, startPath, 0));

        while (pending.Count > 0)
        {
            var (folder, physicalPath, depth) = pending.Dequeue();
            foldersDiscovered++;

            // Live progress for the UI.
            if (foldersDiscovered % ProgressInterval == 0)
                await _jobRepository.SetEnumerationResultAsync(discoveryJobId, foldersDiscovered, filesFound: 0, cancellationToken);

            if (!Directory.Exists(physicalPath))
                continue;

            folder.LastWriteTimeUtc = PostgresTimestamps.TruncateToMicroseconds(Directory.GetLastWriteTimeUtc(physicalPath));

            var observedFolders = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entryPath in Directory.EnumerateDirectories(physicalPath))
            {
                var name = Path.GetFileName(entryPath);

                // Every directory on disk counts as seen, excluded or not: excluded ones are pruned below,
                // never marked missing.
                observedFolders.Add(PathNormalizer.FolderNameKey(name));

                // Excluded names are skipped here; rows that already exist for them are pruned after this
                // folder's pass (below).
                if (excludeRules.IsFolderExcluded(name))
                    continue;

                var childRelativePath = PathNormalizer.Combine(folder.RelativePath, name);
                var childFolder = await ScanTargets.GetOrCreateFolderAsync(_folderRepository, _clock, root.Id, folder.Id, childRelativePath, name, cancellationToken);

                // Removed from the collection (tombstone): never descend into it or re-index it.
                if (!childFolder.IsActive)
                    continue;

                // Excluded: never descend into it. Its subtree is left exactly as it was, and it still
                // counts as observed above, so it's never marked missing either.
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

                // Skip reparse points (protects against a symlink/junction loop) and stop past the depth cap;
                // the row is still created above for tree visibility.
                var isReparsePoint = new DirectoryInfo(entryPath).Attributes.HasFlag(FileAttributes.ReparsePoint);
                if (!isReparsePoint && depth < ScanTargets.MaxFolderDepth)
                    pending.Enqueue((childFolder, entryPath, depth + 1));
            }

            folder.ChildrenDiscoveredAt = _clock.UtcNow;
            folder.ModifiedUtc = _clock.UtcNow;
            await _folderRepository.UpdateAsync(folder, cancellationToken);

            foreach (var child in await _folderRepository.GetChildrenAsync(folder.Id, cancellationToken))
            {
                // Prune on discovery: child folders whose name is now excluded go with their whole subtree.
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

        return foldersDiscovered;
    }

    private async Task FinalizeSuccessAsync(int discoveryJobId, int foldersDiscovered, CancellationToken cancellationToken)
    {
        // A discovery job never finds files and has no enrichment phase, so it goes straight from
        // Enumerating to Completed (see JobStatus).
        await _jobRepository.SetEnumerationResultAsync(discoveryJobId, foldersDiscovered, filesFound: 0, cancellationToken);
        await _jobRepository.TryMarkCompletedFromEnumeratingAsync(discoveryJobId, _clock.UtcNow, cancellationToken);
    }

    private async Task FinalizeFailureAsync(int discoveryJobId, Exception ex, int foldersDiscovered)
    {
        var isCancellation = ex is OperationCanceledException;
        var status = isCancellation ? JobStatus.Cancelled : JobStatus.Failed;
        var errorMessage = isCancellation ? null : ScanTargets.TruncateErrorMessage(ex.Message);

        // CancellationToken.None: see ScanService.FinalizeFailureAsync for why.
        await _jobRepository.SetFailureResultAsync(
            discoveryJobId, foldersDiscovered, filesFound: 0, errorMessage, status, _clock.UtcNow, CancellationToken.None);
    }
}
