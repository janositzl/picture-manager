using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Thumbnails;

namespace PictureManager.Api.Middleware;

public sealed class ImageCacheControlMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ThumbnailCacheOptions _options;

    public ImageCacheControlMiddleware(RequestDelegate next, ThumbnailCacheOptions options)
    {
        _next = next;
        _options = options;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isThumbnail = path.EndsWith("/thumbnail", StringComparison.OrdinalIgnoreCase);
        var isPreview = path.EndsWith("/preview", StringComparison.OrdinalIgnoreCase);

        string? cacheControl = null;
        if (isThumbnail || (isPreview && _options.PreviewEnabled))
            cacheControl = "private, max-age=31536000, immutable";
        else if (isPreview)
            cacheControl = "private, max-age=300";

        if (cacheControl is not null)
        {
            // Decided at response start so a 404/500 never carries a long-lived cache header -- a browser
            // would otherwise keep showing the broken tile long after the image becomes servable again.
            context.Response.OnStarting(() =>
            {
                if (context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status206PartialContent)
                    context.Response.Headers.CacheControl = cacheControl;
                return Task.CompletedTask;
            });
        }

        return _next(context);
    }
}
