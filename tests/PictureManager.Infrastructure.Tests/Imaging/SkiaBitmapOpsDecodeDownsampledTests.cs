using System;
using System.IO;
using FluentAssertions;
using PictureManager.Infrastructure.Imaging;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Imaging;

public sealed class SkiaBitmapOpsDecodeDownsampledTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pm-decode-" + Guid.NewGuid().ToString("N"));

    public SkiaBitmapOpsDecodeDownsampledTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void DecodeDownsampled_MissingFile_ThrowsSoTheCallerCanRetry()
    {
        var act = () => SkiaBitmapOps.DecodeDownsampled(Path.Combine(_dir, "missing.jpg"), 1600);

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void DecodeDownsampled_GarbageContent_ReturnsNull()
    {
        var path = Path.Combine(_dir, "garbage.jpg");
        File.WriteAllText(path, "not an image");

        SkiaBitmapOps.DecodeDownsampled(path, 1600).Should().BeNull();
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    public void DecodeDownsampled_SmallImage_DecodesAtFullSizeAsRgba8888(SKEncodedImageFormat format)
    {
        var path = WriteImage(format, 64, 48);

        using var bitmap = SkiaBitmapOps.DecodeDownsampled(path, 1600);

        bitmap.Should().NotBeNull();
        bitmap!.ColorType.Should().Be(SKColorType.Rgba8888);
        bitmap.Width.Should().Be(64);
        bitmap.Height.Should().Be(48);
    }

    [Fact]
    public void DecodeDownsampled_LargeJpeg_DecodesScaledDown()
    {
        var path = WriteImage(SKEncodedImageFormat.Jpeg, 2000, 1000);

        using var bitmap = SkiaBitmapOps.DecodeDownsampled(path, 500);

        bitmap.Should().NotBeNull();
        bitmap!.ColorType.Should().Be(SKColorType.Rgba8888);
        bitmap.Width.Should().BeLessThan(2000).And.BeGreaterThanOrEqualTo(500);
        bitmap.Height.Should().BeLessThan(1000);
    }

    private string WriteImage(SKEncodedImageFormat format, int width, int height)
    {
        using var source = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        source.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(format, 90);
        var path = Path.Combine(_dir, $"image-{width}x{height}.{format.ToString().ToLowerInvariant()}");
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }
}
