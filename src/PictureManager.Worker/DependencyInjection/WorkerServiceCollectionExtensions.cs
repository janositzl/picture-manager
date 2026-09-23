using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PictureManager.Application.Scanning;
using PictureManager.Worker.Scanning;

namespace PictureManager.Worker.DependencyInjection;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddWorker(this IServiceCollection services)
    {
        services.AddSingleton<IEnrichmentQueue, ChannelEnrichmentQueue>();
        services.AddHostedService<EnrichmentBackgroundService>();
        services.AddSingleton<IScanQueue, ChannelScanQueue>();
        services.AddHostedService<ScanBackgroundService>();

        return services;
    }
}
