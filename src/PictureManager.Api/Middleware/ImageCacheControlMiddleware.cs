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

        if (isThumbnail || (isPreview && _options.PreviewEnabled))
        {
            context.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        }
        else if (isPreview)
        {
            context.Response.Headers.CacheControl = "private, max-age=300";
        }

        return _next(context);
    }
}
