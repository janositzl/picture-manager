using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Scanning;
using PictureManager.Infrastructure.Scanning;
using SkiaSharp;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Scanning;

public class MetadataExtractorExifReaderTests
{
    // Smallest known-valid 1x1 white-pixel JPEG, no EXIF segment.
    private const string MinimalJpegBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=";

    [Fact]
    public async Task ReadAsync_ValidJpegWithoutExif_ReturnsDimensions_AndNullExifFields()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(MinimalJpegBase64));

            var reader = new MetadataExtractorExifReader();
            var result = await reader.ReadAsync(path);

            result.Width.Should().Be(1);
            result.Height.Should().Be(1);
            result.CameraMake.Should().BeNull();
            result.DateTaken.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("bmp")]
    public async Task ReadAsync_NonJpegImage_ReturnsDimensions(string format)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.{format}");
        try
        {
            using (var bitmap = new SKBitmap(7, 3))
            {
                bitmap.Erase(SKColors.Red);
                using var image = SKImage.FromBitmap(bitmap);
                var skFormat = format switch { "png" => SKEncodedImageFormat.Png, "webp" => SKEncodedImageFormat.Webp, _ => SKEncodedImageFormat.Bmp };
                using var data = image.Encode(skFormat, 100);
                if (data is null) return; // encoder unavailable on this platform
                await File.WriteAllBytesAsync(path, data.ToArray());
            }

            var result = await new MetadataExtractorExifReader().ReadAsync(path);

            result.Width.Should().Be(7);
            result.Height.Should().Be(3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_MetadataReaderFails_StillFallsBackToCodecDimensions()
    {
        // A PNG whose header MetadataExtractor can read but with a wrong extension still decodes via Skia.
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dat");
        try
        {
            using (var bitmap = new SKBitmap(5, 4))
            {
                bitmap.Erase(SKColors.Blue);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                await File.WriteAllBytesAsync(path, data.ToArray());
            }

            var result = await new MetadataExtractorExifReader().ReadAsync(path);

            result.Width.Should().Be(5);
            result.Height.Should().Be(4);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("sRGB IEC61966-2.1\0", "sRGB IEC61966-2.1")]
    [InlineData("a\0b\0c", "abc")]
    [InlineData(null, null)]
    public void StripNulChars_RemovesEmbeddedNulBytes(string? input, string? expected)
    {
        MetadataExtractorExifReader.StripNulChars(input).Should().Be(expected);
    }

    [Fact]
    public async Task ReadAsync_UnreadableFile_ReturnsEmptyExifData_DoesNotThrow()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0x00, 0x01, 0x02, 0x03 });

            var reader = new MetadataExtractorExifReader();
            var result = await reader.ReadAsync(path);

            result.Should().Be(ExifData.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
