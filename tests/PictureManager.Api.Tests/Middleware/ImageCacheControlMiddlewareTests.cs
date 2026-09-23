using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PictureManager.Api.Middleware;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Api.Tests.Middleware;

public class ImageCacheControlMiddlewareTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(string path, bool previewEnabled)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var options = new ThumbnailCacheOptions { PreviewEnabled = previewEnabled };
        var middleware = new ImageCacheControlMiddleware(_ => Task.CompletedTask, options);

        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task ThumbnailPath_AlwaysGetsImmutableLongCache()
    {
        var context = await InvokeAsync("/api/images/5/thumbnail", previewEnabled: false);

        context.Response.Headers.CacheControl.ToString().Should().Be("private, max-age=31536000, immutable");
    }

    [Fact]
    public async Task PreviewPath_WhenEnabled_GetsImmutableLongCache()
    {
        var context = await InvokeAsync("/api/images/5/preview", previewEnabled: true);

        context.Response.Headers.CacheControl.ToString().Should().Be("private, max-age=31536000, immutable");
    }

    [Fact]
    public async Task PreviewPath_WhenDisabled_GetsShortRevalidatingCache()
    {
        var context = await InvokeAsync("/api/images/5/preview", previewEnabled: false);

        context.Response.Headers.CacheControl.ToString().Should().Be("private, max-age=300");
    }

    [Fact]
    public async Task UnrelatedPath_NoCacheControlHeaderAdded()
    {
        var context = await InvokeAsync("/api/scans", previewEnabled: false);

        context.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }
}
