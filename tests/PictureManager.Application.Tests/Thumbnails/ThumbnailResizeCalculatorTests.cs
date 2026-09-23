using FluentAssertions;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Application.Tests.Thumbnails;

public class ThumbnailResizeCalculatorTests
{
    [Fact]
    public void CalculateTargetDimensions_LandscapeLargerThanTarget_ScalesByWidth()
    {
        var (width, height) = ThumbnailResizeCalculator.CalculateTargetDimensions(4000, 2000, 300);

        width.Should().Be(300);
        height.Should().Be(150);
    }

    [Fact]
    public void CalculateTargetDimensions_PortraitLargerThanTarget_ScalesByHeight()
    {
        var (width, height) = ThumbnailResizeCalculator.CalculateTargetDimensions(2000, 4000, 300);

        width.Should().Be(150);
        height.Should().Be(300);
    }

    [Fact]
    public void CalculateTargetDimensions_SourceSmallerThanTarget_NeverUpscales()
    {
        var (width, height) = ThumbnailResizeCalculator.CalculateTargetDimensions(200, 100, 300);

        width.Should().Be(200);
        height.Should().Be(100);
    }

    [Fact]
    public void CalculateTargetDimensions_LongestEdgeExactlyAtTarget_ReturnsOriginalDimensions()
    {
        var (width, height) = ThumbnailResizeCalculator.CalculateTargetDimensions(300, 200, 300);

        width.Should().Be(300);
        height.Should().Be(200);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(1, 1)]
    [InlineData(6, 6)]
    [InlineData(8, 8)]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    [InlineData(-3, 1)]
    public void NormalizeOrientation_OutOfRangeOrNull_FallsBackToOne(int? input, int expected)
    {
        ThumbnailResizeCalculator.NormalizeOrientation(input).Should().Be(expected);
    }
}
