using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IImageRootRepository
{
    Task<ImageRoot?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImageRoot>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ImageRoot> AddAsync(ImageRoot imageRoot, CancellationToken cancellationToken = default);
    Task UpdateAsync(ImageRoot imageRoot, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
