using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Faces;
using PictureManager.Infrastructure.Faces;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Faces;

/// <summary>
/// Smoke tests against the real models. Opt-in: set PICTUREMANAGER_FACE_MODELS (folder with det_10g.onnx and
/// w600k_r50.onnx) and PICTUREMANAGER_FACE_FIXTURES (folder with person-a-1.jpg and person-a-2.jpg = one person,
/// person-b.jpg = someone else, and no-face.jpg). Without them the tests pass without asserting.
/// </summary>
public class OnnxFaceAnalyzerTests
{
    private static readonly string? Models = Environment.GetEnvironmentVariable("PICTUREMANAGER_FACE_MODELS");
    private static readonly string? Fixtures = Environment.GetEnvironmentVariable("PICTUREMANAGER_FACE_FIXTURES");

    [Fact]
    public void Model_MissingFiles_ThrowsFaceModelUnavailable()
    {
        var analyzer = new OnnxFaceAnalyzer(new FaceRecognitionOptions { ModelDirectory = Path.Combine(Path.GetTempPath(), "no-models-here") });

        var act = () => analyzer.Model;

        act.Should().Throw<FaceModelUnavailableException>().WithMessage("*det_10g.onnx*");
    }

    [Fact]
    public async Task Analyze_SamePersonIsCloserThanDifferentPerson()
    {
        if (Models is null || Fixtures is null) return;
        using var analyzer = new OnnxFaceAnalyzer(new FaceRecognitionOptions { ModelDirectory = Models });

        var a1 = (await analyzer.AnalyzeAsync(Path.Combine(Fixtures, "person-a-1.jpg"), 1))!.Faces.OrderByDescending(f => f.Width).First();
        var a2 = (await analyzer.AnalyzeAsync(Path.Combine(Fixtures, "person-a-2.jpg"), 1))!.Faces.OrderByDescending(f => f.Width).First();
        var b = (await analyzer.AnalyzeAsync(Path.Combine(Fixtures, "person-b.jpg"), 1))!.Faces.OrderByDescending(f => f.Width).First();

        static double Distance(float[] x, float[] y) => 1 - x.Zip(y, (p, q) => (double)p * q).Sum();
        Distance(a1.Embedding, a2.Embedding).Should().BeLessThan(0.5);
        Distance(a1.Embedding, b.Embedding).Should().BeGreaterThan(Distance(a1.Embedding, a2.Embedding));
        a1.Embedding.Should().HaveCount(512);
        analyzer.Model.EmbeddingDimensions.Should().Be(512);
    }

    [Fact]
    public async Task Analyze_NoFace_ReturnsEmpty_AndUndecodableReturnsNull()
    {
        if (Models is null || Fixtures is null) return;
        using var analyzer = new OnnxFaceAnalyzer(new FaceRecognitionOptions { ModelDirectory = Models });
        var garbage = Path.GetTempFileName();
        await File.WriteAllTextAsync(garbage, "not an image");

        (await analyzer.AnalyzeAsync(Path.Combine(Fixtures, "no-face.jpg"), 1))!.Faces.Should().BeEmpty();
        (await analyzer.AnalyzeAsync(garbage, 1)).Should().BeNull();
    }
}
