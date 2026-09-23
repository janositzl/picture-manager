using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Repositories;
using PictureManager.Application.Thumbnails;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class ImageEndpointsTests
{
    private static Image BuildImage(int id, bool missing = false) => new()
    {
        Id = id,
        FolderId = 1,
        FileName = "photo",
        Extension = ".jpg",
        ContentHash = "abcd1234",
        MissingSinceUtc = missing ? System.DateTime.UtcNow : null,
        Folder = new Folder
        {
            Id = 1,
            RootId = 1,
            RelativePath = "vacation",
            Root = new ImageRoot { Id = 1, MountPath = "/images" }
        }
    };

    [Fact]
    public async Task GetThumbnailAsync_UnknownId_ReturnsNotFound()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns((Image?)null);
        var thumbnailService = Substitute.For<IThumbnailService>();

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task GetThumbnailAsync_MissingSinceUtcSet_ReturnsNotFound()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1, missing: true));
        var thumbnailService = Substitute.For<IThumbnailService>();

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task GetThumbnailAsync_ThumbnailServiceReturnsNull_ReturnsNotFound()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1));
        var thumbnailService = Substitute.For<IThumbnailService>();
        thumbnailService.GetOrCreateDerivativePathAsync("abcd1234", Arg.Any<string>(), Arg.Any<int?>(), DerivativeSize.Thumbnail, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task GetThumbnailAsync_Success_ReturnsPhysicalFileResult_WithImmutableCacheControl()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1));
        var thumbnailService = Substitute.For<IThumbnailService>();
        thumbnailService.GetOrCreateDerivativePathAsync("abcd1234", Arg.Any<string>(), Arg.Any<int?>(), DerivativeSize.Thumbnail, Arg.Any<CancellationToken>())
            .Returns("/cache/ab/cd/abcd1234-300.webp");

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        var fileResult = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
        fileResult.FileName.Should().Be("/cache/ab/cd/abcd1234-300.webp");
        fileResult.ContentType.Should().Be("image/webp");
        fileResult.EnableRangeProcessing.Should().BeTrue();
    }

    [Fact]
    public async Task GetPreviewAsync_PreviewEnabled_UsesThumbnailServiceForTheLargerSize()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1));
        var thumbnailService = Substitute.For<IThumbnailService>();
        thumbnailService.GetOrCreateDerivativePathAsync("abcd1234", Arg.Any<string>(), Arg.Any<int?>(), DerivativeSize.Preview, Arg.Any<CancellationToken>())
            .Returns("/cache/ab/cd/abcd1234-1800.webp");
        var options = new ThumbnailCacheOptions { PreviewEnabled = true };

        var result = await ImageEndpoints.GetPreviewAsync(1, imageRepository, thumbnailService, options, CancellationToken.None);

        var fileResult = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
        fileResult.FileName.Should().Be("/cache/ab/cd/abcd1234-1800.webp");
        fileResult.ContentType.Should().Be("image/webp");
    }

    [Fact]
    public async Task GetPreviewAsync_PreviewDisabled_ServesOriginalFile_WithShortCacheAndResolvedContentType()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pm-preview-fallback-" + System.Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tempDir, "vacation"));
        var originalPath = Path.Combine(tempDir, "vacation", "photo.jpg");
        await File.WriteAllBytesAsync(originalPath, new byte[] { 1, 2, 3 });
        try
        {
            var image = BuildImage(1);
            image.Folder!.Root!.MountPath = tempDir;
            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
            var thumbnailService = Substitute.For<IThumbnailService>();
            var options = new ThumbnailCacheOptions { PreviewEnabled = false };

            var result = await ImageEndpoints.GetPreviewAsync(1, imageRepository, thumbnailService, options, CancellationToken.None);

            var fileResult = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
            fileResult.FileName.Should().Be(originalPath);
            fileResult.ContentType.Should().Be("image/jpeg");
            await thumbnailService.DidNotReceive().GetOrCreateDerivativePathAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<DerivativeSize>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetThumbnailAsync_RelativeDerivativePath_ReturnsPhysicalFileResultWithFullPath()
    {
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(BuildImage(1));
        var thumbnailService = Substitute.For<IThumbnailService>();
        var relativePath = Path.Combine("..", "..", "thumbnail-cache", "ab", "cd", "abcd1234-300.webp");
        thumbnailService.GetOrCreateDerivativePathAsync("abcd1234", Arg.Any<string>(), Arg.Any<int?>(), DerivativeSize.Thumbnail, Arg.Any<CancellationToken>())
            .Returns(relativePath);

        var result = await ImageEndpoints.GetThumbnailAsync(1, imageRepository, thumbnailService, CancellationToken.None);

        var fileResult = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
        fileResult.FileName.Should().Be(Path.GetFullPath(relativePath));
    }

    [Fact]
    public async Task GetPreviewAsync_PreviewDisabled_RelativeMountPath_ReturnsPhysicalFileResultWithFullPath()
    {
        var relativeRoot = "pm-preview-relative-" + System.Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(relativeRoot, "vacation"));
        var relativeOriginalPath = Path.Combine(relativeRoot, "vacation", "photo.jpg");
        await File.WriteAllBytesAsync(relativeOriginalPath, new byte[] { 1, 2, 3 });
        try
        {
            var image = BuildImage(1);
            image.Folder!.Root!.MountPath = relativeRoot;
            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
            var thumbnailService = Substitute.For<IThumbnailService>();
            var options = new ThumbnailCacheOptions { PreviewEnabled = false };

            var result = await ImageEndpoints.GetPreviewAsync(1, imageRepository, thumbnailService, options, CancellationToken.None);

            var fileResult = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
            fileResult.FileName.Should().Be(Path.GetFullPath(relativeOriginalPath));
        }
        finally
        {
            Directory.Delete(relativeRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetPreviewAsync_PreviewDisabled_OriginalFileMissing_ReturnsNotFound()
    {
        var image = BuildImage(1);
        image.Folder!.Root!.MountPath = Path.Combine(Path.GetTempPath(), "pm-preview-missing-" + System.Guid.NewGuid());
        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
        var thumbnailService = Substitute.For<IThumbnailService>();
        var options = new ThumbnailCacheOptions { PreviewEnabled = false };

        var result = await ImageEndpoints.GetPreviewAsync(1, imageRepository, thumbnailService, options, CancellationToken.None);

        result.Should().BeOfType<NotFound>();
    }
}
