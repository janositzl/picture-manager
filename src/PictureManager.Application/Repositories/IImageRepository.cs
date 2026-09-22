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
    Task<Image?> GetByFolderAndFileNameAsync(int folderId, string fileName, string extension, CancellationToken cancellationToken = default);
    Task<Image?> GetByIdWithFolderAsync(int id, CancellationToken cancellationToken = default);
    Task<Image?> GetByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);
    Task UpdateAsync(Image image, CancellationToken cancellationToken = default);
    Task DeleteAsync(Image image, CancellationToken cancellationToken = default);
}
