using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IImageRepository
{
    Task<Image?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Image>> GetByFolderIdAsync(int folderId, CancellationToken cancellationToken = default);
    Task<Image> AddAsync(Image image, CancellationToken cancellationToken = default);
}
