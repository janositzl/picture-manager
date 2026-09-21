using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Common;

namespace PictureManager.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
