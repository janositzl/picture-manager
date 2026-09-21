using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAlbumRepository
{
    Task<Album?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Album>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Album> AddAsync(Album album, CancellationToken cancellationToken = default);
}
