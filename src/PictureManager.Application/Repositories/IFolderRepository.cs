using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Folders;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IFolderRepository
{
    Task<Folder?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Folder>> GetChildrenAsync(int? parentId, CancellationToken cancellationToken = default);
    Task<Folder> AddAsync(Folder folder, CancellationToken cancellationToken = default);
    Task<Folder?> GetByRootAndRelativePathAsync(int rootId, string relativePath, CancellationToken cancellationToken = default);
    Task UpdateAsync(Folder folder, CancellationToken cancellationToken = default);

    /// <summary>True when the folder exists, is active, and its root is active.</summary>
    Task<bool> IsVisibleAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Top folders (ParentId == null) of active roots, ordered by lower(Name).</summary>
    Task<IReadOnlyList<FolderNode>> GetVisibleRootFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Active children of the folder, ordered by lower(Name).</summary>
    Task<IReadOnlyList<FolderNode>> GetVisibleChildrenAsync(int parentId, CancellationToken cancellationToken = default);

    Task<FolderDetail?> GetVisibleDetailAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Tombstoned folders (IsActive == false).</summary>
    Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// In one transaction: hard-deletes the folder's images and every folder beneath it (with their images
    /// and album entries, via the database's cascades), then marks the folder itself IsActive = false.
    /// </summary>
    Task RemoveFromCollectionAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>Hard-deletes the folder and everything beneath it (no tombstone).</summary>
    Task DeleteSubtreeAsync(int folderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets MissingSinceUtc on the folder and every active folder beneath it that isn't already marked (earlier
    /// dates are kept). Tombstones (IsActive == false) are left alone. Nothing is deleted.
    /// </summary>
    Task MarkSubtreeMissingAsync(int folderId, DateTime missingSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Renames the root's top folder (the tree node that shows the root's name).</summary>
    Task RenameRootFolderAsync(int rootId, string name, CancellationToken cancellationToken = default);
}
