using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Configurations;
using PictureManager.Model;

namespace PictureManager.Infrastructure.Persistence.Repositories;

public sealed class AppSettingsRepository : IAppSettingsRepository
{
    private readonly PictureManagerDbContext _dbContext;

    public AppSettingsRepository(PictureManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Settings.SingleAsync(s => s.Id == AppSettingsConfiguration.SingletonId, cancellationToken);
    }
}
