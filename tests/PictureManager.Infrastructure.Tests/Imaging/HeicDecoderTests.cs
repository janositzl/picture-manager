using FluentAssertions;
using PictureManager.Infrastructure.Imaging;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Imaging;

public sealed class HeicDecoderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("heic-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void DecodeSafely_CorruptHeic_ReturnsNull()
    {
        var path = Path.Combine(_dir, "bad.heic");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);

        SkiaBitmapOps.DecodeSafely(path).Should().BeNull();
    }

    [Fact]
    public void EffectiveOrientation_Heic_IsNeutralised_OthersPassThrough()
    {
        HeicDecoder.EffectiveOrientation("a.HEIC", 6).Should().Be(1);
        HeicDecoder.EffectiveOrientation("a.jpg", 6).Should().Be(6);
    }
}
