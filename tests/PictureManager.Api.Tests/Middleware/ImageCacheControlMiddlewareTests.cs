using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using PictureManager.Api.Middleware;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Api.Tests.Middleware;

public class ImageCacheControlMiddlewareTests
{
    // DefaultHttpContext's response feature never fires OnStarting callbacks; this one does, on demand.
    private sealed class StartableResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = new();

        public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public async Task StartAsync()
        {
            for (var i = _onStarting.Count - 1; i >= 0; i--)
                await _onStarting[i].Callback(_onStarting[i].State);
        }
    }

    private static async Task<DefaultHttpContext> InvokeAsync(string path, bool previewEnabled, int statusCode = StatusCodes.Status200OK)
    {
        var responseFeature = new StartableResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        context.Request.Path = path;
        var options = new ThumbnailCacheOptions { PreviewEnabled = previewEnabled };
        var middleware = new ImageCacheControlMiddleware(ctx =>
        {
            ctx.Response.StatusCode = statusCode;
            return responseFeature.StartAsync();
        }, options);

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
    public async Task ThumbnailPath_PartialContent_GetsImmutableLongCache()
    {
        var context = await InvokeAsync("/api/images/5/thumbnail", previewEnabled: true, StatusCodes.Status206PartialContent);

        context.Response.Headers.CacheControl.ToString().Should().Be("private, max-age=31536000, immutable");
    }

    [Theory]
    [InlineData("/api/images/5/thumbnail", StatusCodes.Status404NotFound)]
    [InlineData("/api/images/5/preview", StatusCodes.Status404NotFound)]
    [InlineData("/api/images/5/thumbnail", StatusCodes.Status500InternalServerError)]
    public async Task ImagePath_NonSuccessResponse_NoCacheControlHeaderAdded(string path, int statusCode)
    {
        var context = await InvokeAsync(path, previewEnabled: true, statusCode);

        context.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task UnrelatedPath_NoCacheControlHeaderAdded()
    {
        var context = await InvokeAsync("/api/scans", previewEnabled: false);

        context.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }
}
