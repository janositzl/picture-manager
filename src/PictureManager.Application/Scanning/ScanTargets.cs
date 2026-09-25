using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Application.Scanning;

/// <summary>Resolving and checking what a scan or discovery walks. Shared by both job kinds.</summary>
public static class ScanTargets
{
    /// <summary>Walks stop descending this many levels below their starting folder (guards a symlink/junction cycle).</summary>
    public const int MaxFolderDepth = 50;

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
}
