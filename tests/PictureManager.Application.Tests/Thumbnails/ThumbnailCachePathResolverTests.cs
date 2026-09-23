using FluentAssertions;
using PictureManager.Application.Thumbnails;
using Xunit;

namespace PictureManager.Application.Tests.Thumbnails;

public class ThumbnailCachePathResolverTests
{
    [Fact]
    public void GetPath_ShardsByFirstFourHashCharacters_AndAppendsSizeSuffix()
    {
        var path = ThumbnailCachePathResolver.GetPath("/cache", "a3f9c2abcdef", DerivativeSize.Thumbnail);

        path.Should().Be(System.IO.Path.Combine("/cache", "a3", "f9", "a3f9c2abcdef-300.webp"));
    }

    [Fact]
    public void GetPath_PreviewSize_UsesPreviewSuffix()
    {
        var path = ThumbnailCachePathResolver.GetPath("/cache", "a3f9c2abcdef", DerivativeSize.Preview);

        path.Should().EndWith("a3f9c2abcdef-1800.webp");
    }

    [Fact]
    public void GetTempPath_IsInTheSameShardDirectory_ButNotEqualToTheFinalPath()
    {
        var finalPath = ThumbnailCachePathResolver.GetPath("/cache", "a3f9c2abcdef", DerivativeSize.Thumbnail);
        var tempPath = ThumbnailCachePathResolver.GetTempPath("/cache", "a3f9c2abcdef", DerivativeSize.Thumbnail);

        System.IO.Path.GetDirectoryName(tempPath).Should().Be(System.IO.Path.GetDirectoryName(finalPath));
        tempPath.Should().NotBe(finalPath);
    }

    [Fact]
    public void GetPath_HashShorterThanFourCharacters_ThrowsArgumentException()
    {
        var act = () => ThumbnailCachePathResolver.GetPath("/cache", "ab", DerivativeSize.Thumbnail);

        act.Should().Throw<System.ArgumentException>();
    }
}
