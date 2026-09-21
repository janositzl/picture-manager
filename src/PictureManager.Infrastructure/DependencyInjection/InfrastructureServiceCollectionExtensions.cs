using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Infrastructure.Persistence;

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

        services.AddDbContext<PictureManagerDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }
}
