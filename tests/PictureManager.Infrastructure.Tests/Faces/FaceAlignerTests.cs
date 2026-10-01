using System;
using System.Linq;
using FluentAssertions;
using PictureManager.Infrastructure.Faces;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

public class FaceAlignerTests
{
    private static readonly (float X, float Y)[] Points = { (10, 20), (50, 22), (30, 40), (15, 60), (45, 61) };

    [Fact]
    public void EstimateSimilarity_SamePoints_IsIdentity()
    {
        var m = FaceAligner.EstimateSimilarity(Points, Points);

        m.ScaleX.Should().BeApproximately(1, 1e-4f);
        m.SkewX.Should().BeApproximately(0, 1e-4f);
        m.TransX.Should().BeApproximately(0, 1e-3f);
        m.TransY.Should().BeApproximately(0, 1e-3f);
    }

    [Fact]
    public void EstimateSimilarity_RecoversScaleRotationAndTranslation()
    {
        const double angle = Math.PI / 6;
        const float scale = 2f, tx = 7f, ty = -3f;
        var target = Points.Select(p => (
            X: (float)(scale * (Math.Cos(angle) * p.X - Math.Sin(angle) * p.Y) + tx),
            Y: (float)(scale * (Math.Sin(angle) * p.X + Math.Cos(angle) * p.Y) + ty))).ToArray();

        var m = FaceAligner.EstimateSimilarity(Points, target);

        foreach (var (p, expected) in Points.Zip(target))
        {
            var mapped = m.MapPoint(p.X, p.Y);
            mapped.X.Should().BeApproximately(expected.X, 1e-3f);
            mapped.Y.Should().BeApproximately(expected.Y, 1e-3f);
        }
    }

    [Fact]
    public void Warp_Returns112SquareRgba()
    {
        using var source = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        source.Erase(SKColors.White);

        using var aligned = FaceAligner.Warp(source, Points);

        aligned.Width.Should().Be(112);
        aligned.Height.Should().Be(112);
        aligned.ColorType.Should().Be(SKColorType.Rgba8888);
    }
}
