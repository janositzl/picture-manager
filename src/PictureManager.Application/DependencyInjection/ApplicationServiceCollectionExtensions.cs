using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Albums;
using PictureManager.Application.Common;
using PictureManager.Application.Discovery;
using PictureManager.Application.Duplicates;
using PictureManager.Application.Folders;
using PictureManager.Application.Images;
using PictureManager.Application.Roots;
using PictureManager.Application.Scanning;
using PictureManager.Application.Settings;

namespace PictureManager.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICurrentUser, SystemCurrentUser>();
        services.AddScoped<IImageEnrichmentService, ImageEnrichmentService>();
        services.AddScoped<IScanService, ScanService>();
        services.AddScoped<IDiscoveryService, DiscoveryService>();
        services.AddScoped<IImageRootSeeder, ImageRootSeeder>();
        services.AddScoped<IRootService, RootService>();
        services.AddScoped<IImageQueryService, ImageQueryService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IAlbumService, AlbumService>();
        services.AddScoped<IDuplicateService, DuplicateService>();
        services.AddScoped<ISettingsService, SettingsService>();

        return services;
    }
}
