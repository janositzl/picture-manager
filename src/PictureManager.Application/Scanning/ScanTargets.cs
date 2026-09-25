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
