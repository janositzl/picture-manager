using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Scanning;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Scanning;

public class SkiaImageValidatorTests
{
    [Fact]
    public async Task IsValidAsync_DecodableImage_ReturnsTrue()
    {
        var path = Path.GetTempFileName();
        try
        {
            using (var bitmap = new SKBitmap(4, 4))
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
            using (var stream = File.OpenWrite(path))
            {
                data.SaveTo(stream);
            }

            var validator = new SkiaImageValidator();
            var result = await validator.IsValidAsync(path);

            result.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task IsValidAsync_NonImageBytes_ReturnsFalse()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4, 5 });

            var validator = new SkiaImageValidator();
            var result = await validator.IsValidAsync(path);

            result.Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
