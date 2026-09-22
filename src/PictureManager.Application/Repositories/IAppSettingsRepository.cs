using System.Threading;
using System.Threading.Tasks;
using PictureManager.Model;

namespace PictureManager.Application.Repositories;

public interface IAppSettingsRepository
{
    Task<AppSettings> GetAsync(CancellationToken cancellationToken = default);
}
