using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Duplicates;
using PictureManager.Infrastructure.Scanning;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Scanning;

public sealed class SkiaDHashPerceptualHasherTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private readonly SkiaDHashPerceptualHasher _hasher = new();

    private string Save(SKBitmap bmp, string name, SKEncodedImageFormat fmt, int quality)
    {
        var path = Path.Combine(_dir, name);
        using var data = bmp.Encode(fmt, quality);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static SKBitmap Scene(int w, int h)
    {
        var bmp = new SKBitmap(w, h);
        using var c = new SKCanvas(bmp);
        using var shader = SKShader.CreateLinearGradient(new(0, 0), new(w, h),
            [SKColors.Navy, SKColors.Orange], SKShaderTileMode.Clamp);
        using var gradientPaint = new SKPaint { Shader = shader };
        using var whitePaint = new SKPaint { Color = SKColors.White };
        using var blackPaint = new SKPaint { Color = SKColors.Black };
        c.DrawRect(0, 0, w, h, gradientPaint);
        c.DrawCircle(w * 0.3f, h * 0.4f, h * 0.2f, whitePaint);
        c.DrawRect(w * 0.6f, h * 0.5f, w * 0.25f, h * 0.3f, blackPaint);
        return bmp;
    }

    [Fact]
    public async Task ComputeAsync_TransparentImage_HashesLikeItsCompositionOnWhite()
    {
        // Transparent pixels must composite on a fixed white background, not on whatever the canvas holds.
        using var transparent = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        transparent.Erase(SKColors.Transparent);
        using var white = new SKBitmap(40, 40);
        white.Erase(SKColors.White);

        var a = await _hasher.ComputeAsync(Save(transparent, "t.png", SKEncodedImageFormat.Png, 100), null);
        var b = await _hasher.ComputeAsync(Save(white, "w.png", SKEncodedImageFormat.Png, 100), null);

        a.Should().Be(b);
    }

    private static SKBitmap Resize(SKBitmap src, int w, int h) =>
        src.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

    private static SKBitmap Rotate90(SKBitmap src)
    {
        var dst = new SKBitmap(src.Height, src.Width);
        using var c = new SKCanvas(dst);
        c.Translate(dst.Width, 0); c.RotateDegrees(90); c.DrawBitmap(src, 0, 0);
        return dst;
    }

    [Fact]
    public async Task DownscaledJpegCopy_IsWithinThreshold()
    {
        using var original = Scene(2000, 1500);
        using var small = Resize(original, 400, 300);
        var a = await _hasher.ComputeAsync(Save(original, "a.png", SKEncodedImageFormat.Png, 100), 1);
        var b = await _hasher.ComputeAsync(Save(small, "b.jpg", SKEncodedImageFormat.Jpeg, 60), 1);
        a.Should().MatchRegex("^[0-9a-f]{16}$");
        PerceptualHash.Distance(PerceptualHash.Parse(a!)!.Value, PerceptualHash.Parse(b!)!.Value).Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public async Task ExifRotatedOriginal_MatchesPhysicallyRotatedCopy()
    {
        using var landscape = Scene(1600, 1200);
        using var rotated = Rotate90(landscape);
        var tagged = await _hasher.ComputeAsync(Save(landscape, "t.jpg", SKEncodedImageFormat.Jpeg, 90), 6); // EXIF 6 = rotate 90 CW
        var physical = await _hasher.ComputeAsync(Save(rotated, "p.jpg", SKEncodedImageFormat.Jpeg, 90), 1);
        PerceptualHash.Distance(PerceptualHash.Parse(tagged!)!.Value, PerceptualHash.Parse(physical!)!.Value).Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public async Task DifferentScene_IsFarApart()
    {
        using var a = Scene(800, 600);
        using var b = Rotate90(Scene(600, 800)); // different composition
        var ha = await _hasher.ComputeAsync(Save(a, "x.png", SKEncodedImageFormat.Png, 100), 1);
        var hb = await _hasher.ComputeAsync(Save(b, "y.png", SKEncodedImageFormat.Png, 100), 1);
        PerceptualHash.Distance(PerceptualHash.Parse(ha!)!.Value, PerceptualHash.Parse(hb!)!.Value).Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task UndecodableFile_ReturnsNull()
    {
        var path = Path.Combine(_dir, "bad.jpg");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        (await _hasher.ComputeAsync(path, null)).Should().BeNull();
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
