using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Thumbnails;
using PictureManager.Infrastructure.Thumbnails;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Thumbnails;

public class SkiaSharpThumbnailServiceTests : IDisposable
{
    private readonly string _cacheRoot = Path.Combine(Path.GetTempPath(), "pm-thumb-tests-" + Guid.NewGuid());
    private readonly string _sourcePath;

    public SkiaSharpThumbnailServiceTests()
    {
        Directory.CreateDirectory(_cacheRoot);
        _sourcePath = Path.Combine(_cacheRoot, "source.jpg");
        WriteTestJpeg(_sourcePath, width: 400, height: 200);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheRoot))
            Directory.Delete(_cacheRoot, recursive: true);
    }

    private static void WriteTestJpeg(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    private SkiaSharpThumbnailService CreateService() =>
        new(new ThumbnailCacheOptions { RootPath = _cacheRoot, PreviewEnabled = true });

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_CacheMiss_GeneratesFile_AtTheShardedPath()
    {
        var service = CreateService();

        var path = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        path.Should().Be(ThumbnailCachePathResolver.GetPath(_cacheRoot, "abcd1234", DerivativeSize.Thumbnail));
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_GeneratedFile_IsAValidWebpImage()
    {
        var service = CreateService();

        var path = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        var bytes = await File.ReadAllBytesAsync(path!);
        bytes.Length.Should().BeGreaterThan(12);
        // RIFF....WEBP header
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WEBP");
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_LandscapeSource_ResizedToLongestEdge()
    {
        var service = CreateService();

        var path = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        using var stream = File.OpenRead(path!);
        using var resultBitmap = SKBitmap.Decode(stream);
        resultBitmap.Width.Should().Be(300);
        resultBitmap.Height.Should().Be(150); // 400x200 source, longest edge (400) -> 300, height scales to 150
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_OrientationSixRotatesNinetyDegrees_SwappingDimensions()
    {
        var service = CreateService();

        // Source is 400x200 (landscape). Orientation 6 = rotate 90 CW, so the output should be portrait.
        var path = await service.GetOrCreateDerivativePathAsync("rot6hash", _sourcePath, orientation: 6, DerivativeSize.Thumbnail);

        using var stream = File.OpenRead(path!);
        using var resultBitmap = SKBitmap.Decode(stream);
        resultBitmap.Width.Should().BeLessThan(resultBitmap.Height);
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_CacheHit_DoesNotRewriteTheFile()
    {
        var service = CreateService();
        var firstPath = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);
        var firstWriteTimeUtc = File.GetLastWriteTimeUtc(firstPath!);

        await Task.Delay(50);
        var secondPath = await service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        secondPath.Should().Be(firstPath);
        File.GetLastWriteTimeUtc(secondPath!).Should().Be(firstWriteTimeUtc);
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_UndecodableSource_ReturnsNull_AndLeavesNoTempFileBehind()
    {
        var badPath = Path.Combine(_cacheRoot, "not-an-image.jpg");
        await File.WriteAllBytesAsync(badPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
        var service = CreateService();

        var result = await service.GetOrCreateDerivativePathAsync("badbadbad", badPath, orientation: 1, DerivativeSize.Thumbnail);

        result.Should().BeNull();
        var shardDir = Path.GetDirectoryName(ThumbnailCachePathResolver.GetPath(_cacheRoot, "badbadbad", DerivativeSize.Thumbnail))!;
        if (Directory.Exists(shardDir))
            Directory.GetFiles(shardDir).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_MissingSourceFile_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetOrCreateDerivativePathAsync("nofilehash", Path.Combine(_cacheRoot, "does-not-exist.jpg"), orientation: 1, DerivativeSize.Thumbnail);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_CacheRootUnwritable_PropagatesException_DoesNotReturnNull()
    {
        // Point the cache root at a path that is actually a FILE, not a directory, so Directory.CreateDirectory
        // throws while creating the shard subdirectory. This must NOT be swallowed into a null "decode failure"
        // result -- it's an operational fault (bad config / disk issue), not "this source isn't a photo".
        var blockingFilePath = Path.Combine(_cacheRoot, "blocked-root");
        await File.WriteAllTextAsync(blockingFilePath, "not a directory");
        var service = new SkiaSharpThumbnailService(new ThumbnailCacheOptions { RootPath = blockingFilePath, PreviewEnabled = true });

        var act = () => service.GetOrCreateDerivativePathAsync("abcd1234", _sourcePath, orientation: 1, DerivativeSize.Thumbnail);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task GetOrCreateDerivativePathAsync_ConcurrentCallsForSameKey_AllSucceed_WithIdenticalValidOutput()
    {
        var service = CreateService();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => service.GetOrCreateDerivativePathAsync("concurrenthash", _sourcePath, orientation: 1, DerivativeSize.Thumbnail))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(path => path.Should().Be(ThumbnailCachePathResolver.GetPath(_cacheRoot, "concurrenthash", DerivativeSize.Thumbnail)));
        File.Exists(results[0]).Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(results[0]!);
        System.Text.Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WEBP");
    }
}
