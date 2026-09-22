using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Scanning;
using PictureManager.Infrastructure.Scanning;
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
