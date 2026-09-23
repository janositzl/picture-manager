using FluentAssertions;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Application.Tests.Thumbnails;

public class ImageContentTypeResolverTests
{
    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".JPG", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".PNG", "image/png")]
    [InlineData(".heic", "image/heic")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".bmp", "image/bmp")]
    [InlineData(".tiff", "image/tiff")]
    [InlineData(".tif", "image/tiff")]
    public void Resolve_KnownExtension_ReturnsExpectedMimeType(string extension, string expected)
    {
        ImageContentTypeResolver.Resolve(extension).Should().Be(expected);
    }

    [Fact]
    public void Resolve_UnknownExtension_FallsBackToOctetStream()
    {
        ImageContentTypeResolver.Resolve(".avif").Should().Be("application/octet-stream");
    }
}
