using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
}
