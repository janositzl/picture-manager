using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Infrastructure.Scanning;
using PictureManager.Infrastructure.Thumbnails;

namespace PictureManager.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PictureManagerDb")
            ?? throw new InvalidOperationException(
                "Connection string 'PictureManagerDb' is not configured.");

        services.AddDbContext<PictureManagerDbContext>(options => options.UseNpgsql(connectionString, o => o.UseVector()));

        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();
        services.AddScoped<IAlbumRepository, AlbumRepository>();
        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<IImageRootRepository, ImageRootRepository>();
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();
        services.AddScoped<IImageQueryRepository, ImageQueryRepository>();

        services.AddSingleton<IContentHasher, XxHashContentHasher>();
        services.AddSingleton<IPerceptualHasher, SkiaDHashPerceptualHasher>();
        services.AddSingleton<IExifReader, MetadataExtractorExifReader>();
        services.AddSingleton<IImageValidator, SkiaImageValidator>();
        services.AddSingleton<IThumbnailService, SkiaSharpThumbnailService>();

        return services;
    }
}
