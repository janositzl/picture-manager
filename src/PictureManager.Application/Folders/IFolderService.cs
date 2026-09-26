using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Folders;

public interface IFolderService
{
    Task<IReadOnlyList<FolderNode>> GetRootsAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<FolderNode>>> GetChildrenAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<FolderDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemovedFolder>> GetRemovedAsync(CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> RestoreAsync(int id, CancellationToken cancellationToken = default);

    Task<Result> SetExcludedAsync(int id, bool isExcluded, CancellationToken cancellationToken = default);
}
