using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;
using PictureManager.Application.Scanning;

namespace PictureManager.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICurrentUser, SystemCurrentUser>();
        services.AddScoped<IImageEnrichmentService, ImageEnrichmentService>();
        services.AddScoped<IScanService, ScanService>();
        services.AddScoped<IDevImageRootSeeder, DevImageRootSeeder>();

        return services;
    }
}
