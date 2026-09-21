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
}
