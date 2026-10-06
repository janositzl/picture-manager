using FluentAssertions;
using PictureManager.Application.Faces;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceDetectionPresetTests
{
    [Theory]
    [InlineData(null, FaceDetectionPreset.Fast)]
    [InlineData("  ", FaceDetectionPreset.Fast)]
    [InlineData("fast", FaceDetectionPreset.Fast)]
    [InlineData("Detailed", FaceDetectionPreset.Detailed)]
    [InlineData(" DETAILED ", FaceDetectionPreset.Detailed)]
    public void TryParse_AcceptsBlankAndEitherNameInAnyCase(string? value, FaceDetectionPreset expected)
    {
        FaceDetectionPresets.TryParse(value, FaceDetectionPreset.Fast, out var preset).Should().BeTrue();
        preset.Should().Be(expected);
    }

    [Theory]
    [InlineData("slow")]
    [InlineData("7")]
    public void TryParse_RejectsAnythingElse(string value) =>
        FaceDetectionPresets.TryParse(value, FaceDetectionPreset.Fast, out _).Should().BeFalse();

    [Fact]
    public void Blank_UsesTheCallersFallback()
    {
        FaceDetectionPresets.TryParse(null, FaceDetectionPreset.Detailed, out var preset).Should().BeTrue();
        preset.Should().Be(FaceDetectionPreset.Detailed);
    }

    [Fact]
    public void Options_FastIsTheOriginalSettings_DetailedLooksAtSmallerFaces()
    {
        var options = new FaceRecognitionOptions();

        options.For(FaceDetectionPreset.Fast).Should().Be(new FaceDetectionSettings(640, 40));
        options.For(FaceDetectionPreset.Detailed).Should().Be(new FaceDetectionSettings(1280, 24));
    }
}
