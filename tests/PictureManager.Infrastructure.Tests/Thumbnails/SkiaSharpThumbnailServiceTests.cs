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

    // Distinguishable-quadrant source used to verify EXIF orientation transforms pixel-by-pixel, not just
    // by dimension swap. Index order matches the ORIGINAL (pre-transform) source layout: 0=top-left,
    // 1=top-right, 2=bottom-left, 3=bottom-right.
    private static readonly SKColor[] QuadrantColors = { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow };

    private static void WriteQuadrantImage(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            var halfWidth = width / 2f;
            var halfHeight = height / 2f;
            canvas.DrawRect(new SKRect(0, 0, halfWidth, halfHeight), new SKPaint { Color = QuadrantColors[0] });
            canvas.DrawRect(new SKRect(halfWidth, 0, width, halfHeight), new SKPaint { Color = QuadrantColors[1] });
            canvas.DrawRect(new SKRect(0, halfHeight, halfWidth, height), new SKPaint { Color = QuadrantColors[2] });
            canvas.DrawRect(new SKRect(halfWidth, halfHeight, width, height), new SKPaint { Color = QuadrantColors[3] });
        }
        using var image = SKImage.FromBitmap(bitmap);
        // PNG (lossless) so the quadrant boundaries stay crisp for the source; the generated derivative is
        // still WebP (the service always encodes WebP), so this also exercises decoding a non-JPEG source.
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    private static void AssertCornerColor(SKBitmap bitmap, int x, int y, SKColor expected, byte tolerance = 40)
    {
        var actual = bitmap.GetPixel(x, y);
        actual.Red.Should().BeInRange((byte)Math.Max(0, expected.Red - tolerance), (byte)Math.Min(255, expected.Red + tolerance));
        actual.Green.Should().BeInRange((byte)Math.Max(0, expected.Green - tolerance), (byte)Math.Min(255, expected.Green + tolerance));
        actual.Blue.Should().BeInRange((byte)Math.Max(0, expected.Blue - tolerance), (byte)Math.Min(255, expected.Blue + tolerance));
    }

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

    // Regression coverage for EXIF orientation *direction*, not just dimension swap. Orientation 6 (see the
    // dimension-swap test above) only proves width/height swapped -- it can't catch a mirrored or
    // wrong-direction rotation. This uses a 4-quadrant source (TL=Red, TR=Lime, BL=Blue, BR=Yellow) and
    // asserts which original quadrant color lands at each of the four output corners, for every
    // non-identity EXIF orientation value (2-8; orientation 1 is a plain copy and is already covered by the
    // other tests in this file). Expected mappings were derived analytically from SkiaSharp's canvas
    // transform composition rule and cross-checked against an independent code review.
    [Theory]
    [InlineData(2, /*TL*/1, /*TR*/0, /*BL*/3, /*BR*/2)] // mirror horizontal
    [InlineData(3, /*TL*/3, /*TR*/2, /*BL*/1, /*BR*/0)] // rotate 180
    [InlineData(4, /*TL*/2, /*TR*/3, /*BL*/0, /*BR*/1)] // mirror vertical
    [InlineData(5, /*TL*/0, /*TR*/2, /*BL*/1, /*BR*/3)] // transpose
    [InlineData(6, /*TL*/2, /*TR*/0, /*BL*/3, /*BR*/1)] // rotate 90 CW
    [InlineData(7, /*TL*/3, /*TR*/1, /*BL*/2, /*BR*/0)] // transverse
    [InlineData(8, /*TL*/1, /*TR*/3, /*BL*/0, /*BR*/2)] // rotate 270 CW (90 CCW)
    public async Task GetOrCreateDerivativePathAsync_AppliesOrientationCorrectly_AtEveryOutputCorner(
        int orientation, int expectedTopLeft, int expectedTopRight, int expectedBottomLeft, int expectedBottomRight)
    {
        var quadrantSourcePath = Path.Combine(_cacheRoot, "quadrant-source.png");
        WriteQuadrantImage(quadrantSourcePath, width: 80, height: 40);
        var service = CreateService();

        var path = await service.GetOrCreateDerivativePathAsync($"orient{orientation}hash", quadrantSourcePath, orientation, DerivativeSize.Thumbnail);

        path.Should().NotBeNull();
        using var stream = File.OpenRead(path!);
        using var resultBitmap = SKBitmap.Decode(stream);

        const int margin = 4;
        AssertCornerColor(resultBitmap, margin, margin, QuadrantColors[expectedTopLeft]);
        AssertCornerColor(resultBitmap, resultBitmap.Width - 1 - margin, margin, QuadrantColors[expectedTopRight]);
        AssertCornerColor(resultBitmap, margin, resultBitmap.Height - 1 - margin, QuadrantColors[expectedBottomLeft]);
        AssertCornerColor(resultBitmap, resultBitmap.Width - 1 - margin, resultBitmap.Height - 1 - margin, QuadrantColors[expectedBottomRight]);
    }
}
