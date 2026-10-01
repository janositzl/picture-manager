using FluentAssertions;
using PictureManager.Infrastructure.Faces;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

public class FaceQualityTests
{
    [Fact]
    public void Compute_IsClampedToZeroToOne_AndRisesWithSizeAndSharpness()
    {
        var small = FaceQuality.Compute(0.9f, 30, 300);
        var large = FaceQuality.Compute(0.9f, 200, 300);
        var blurry = FaceQuality.Compute(0.9f, 200, 5);

        large.Should().BeInRange(0f, 1f);
        large.Should().BeGreaterThan(small);
        large.Should().BeGreaterThan(blurry);
        FaceQuality.Compute(1f, 10_000, 1e9).Should().Be(1f);
    }

    [Fact]
    public void LaplacianVariance_FlatImageIsZero_CheckerboardIsHigh()
    {
        using var flat = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul));
        flat.Erase(SKColors.Gray);
        using var checker = new SKBitmap(new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                checker.SetPixel(x, y, (x + y) % 2 == 0 ? SKColors.White : SKColors.Black);

        FaceQuality.LaplacianVariance(flat).Should().Be(0);
        FaceQuality.LaplacianVariance(checker).Should().BeGreaterThan(1000);
    }
}
