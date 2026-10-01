using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

/// <summary>Resolving and checking what a scan or discovery walks. Shared by both job kinds.</summary>
public static class ScanTargets
{
    /// <summary>Walks stop descending this many levels below their starting folder (guards a symlink/junction cycle).</summary>
    public const int MaxFolderDepth = 50;

    private const int MaxErrorMessageLength = 4000;

    /// <summary>The folder and its root, if the folder exists, isn't removed (tombstoned) and its root is active.</summary>
    public static async Task<(ImageRoot Root, Folder Folder)> GetVisibleFolderAsync(
        IFolderRepository folders, IImageRootRepository roots, int folderId, CancellationToken cancellationToken)
    {
        var folder = await folders.GetByIdAsync(folderId, cancellationToken);
        var root = folder is null ? null : await roots.GetByIdAsync(folder.RootId, cancellationToken);
        if (folder is not { IsActive: true } || root is not { IsActive: true })
            throw FolderUnavailableException.NotFound(folderId);

        return (root, folder);
    }

    // A root whose folder is missing or has no entries at all is treated as an unmounted share.
    public static bool IsRootAvailable(string mountPath) =>
        Directory.Exists(mountPath) && Directory.EnumerateFileSystemEntries(mountPath).Any();

    public static async Task<ImageRoot> GetActiveRootAsync(IImageRootRepository roots, int rootId, CancellationToken cancellationToken)
    {
        var root = await roots.GetByIdAsync(rootId, cancellationToken);
        return root is { IsActive: true } ? root : throw new ScanRootUnavailableException(rootId);
    }

    /// <summary>
    /// The folder a scan, discovery or face-recognition job is scoped to: the given folder (which must be visible,
    /// not missing and not excluded), a root's top folder, or null when neither is given (all active roots).
    /// </summary>
    public static async Task<int?> ResolveJobFolderIdAsync(
        IFolderRepository folders, IImageRootRepository roots, int? rootId, int? folderId, CancellationToken cancellationToken)
    {
        if (folderId.HasValue)
        {
            var (_, folder) = await GetVisibleFolderAsync(folders, roots, folderId.Value, cancellationToken);

            // The walk couldn't reach it; scanning or discovering its parent is what notices it's back.
            if (folder.MissingSinceUtc is not null)
                throw FolderUnavailableException.Missing(folderId.Value);

            if (folder.IsExcluded || await folders.HasExcludedAncestorAsync(folder.Id, cancellationToken))
                throw FolderUnavailableException.Excluded(folderId.Value);

            return folder.Id;
        }

        if (rootId.HasValue)
        {
            await GetActiveRootAsync(roots, rootId.Value, cancellationToken);

            // Every root has a top folder (ImageRootSeeder); it's the job's recorded scope.
            return (await folders.GetByRootAndRelativePathAsync(rootId.Value, string.Empty, cancellationToken))?.Id;
        }

        return null;
    }

    public static async Task<Folder> GetOrCreateFolderAsync(
        IFolderRepository folders, IClock clock, int rootId, int? parentId, string relativePath, string name, CancellationToken cancellationToken)
    {
        var existing = await folders.GetByRootAndRelativePathAsync(rootId, relativePath, cancellationToken);
        if (existing is not null)
            return existing;

        return await folders.AddAsync(new Folder
        {
            RootId = rootId,
            ParentId = parentId,
            Name = name,
            RelativePath = relativePath,
            CreatedUtc = clock.UtcNow,
            ModifiedUtc = clock.UtcNow
        }, cancellationToken);
    }

    public static string? TruncateErrorMessage(string? message) =>
        message is { Length: > MaxErrorMessageLength } ? message[..MaxErrorMessageLength] : message;
}
