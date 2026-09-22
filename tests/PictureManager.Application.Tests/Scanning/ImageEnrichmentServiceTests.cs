using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ImageEnrichmentServiceTests
{
    [Fact]
    public async Task EnrichAsync_ExistingFile_UpdatesHashExifAndIndexState()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3 });
            var tempDir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileNameWithoutExtension(tempFile);
            var extension = Path.GetExtension(tempFile);

            var image = new Image
            {
                Id = 1,
                FileName = fileName,
                Extension = extension,
                FileSize = 3,
                Folder = new Folder { Id = 5, RelativePath = string.Empty, Root = new ImageRoot { Id = 1, MountPath = tempDir } }
            };

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
            imageRepository.GetByContentHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Image?)null);

            var contentHasher = Substitute.For<IContentHasher>();
            contentHasher.ComputeAsync(tempFile, 3, Arg.Any<CancellationToken>()).Returns("hash-abc");

            var exifReader = Substitute.For<IExifReader>();
            exifReader.ReadAsync(tempFile, Arg.Any<CancellationToken>()).Returns(new ExifData(
                100, 200, 1, new DateTime(2026, 1, 1), "Canon", "EOS R5", "24-70mm", 1.0, 2.0, "{}"));

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc));

            var service = new ImageEnrichmentService(imageRepository, contentHasher, exifReader, clock);
            await service.EnrichAsync(1);

            await imageRepository.Received(1).UpdateAsync(
                Arg.Is<Image>(i => i.ContentHash == "hash-abc" && i.Width == 100 && i.CameraMake == "Canon"
                    && i.IndexState == IndexState.Indexed && i.MissingSinceUtc == null),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task EnrichAsync_FileNoLongerExists_MarksImageMissing()
    {
        var image = new Image
        {
            Id = 2,
            FileName = "gone",
            Extension = ".jpg",
            Folder = new Folder { Id = 5, RelativePath = string.Empty, Root = new ImageRoot { Id = 1, MountPath = Path.GetTempPath() } }
        };

        var imageRepository = Substitute.For<IImageRepository>();
        imageRepository.GetByIdWithFolderAsync(2, Arg.Any<CancellationToken>()).Returns(image);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc));

        var service = new ImageEnrichmentService(imageRepository, Substitute.For<IContentHasher>(), Substitute.For<IExifReader>(), clock);
        await service.EnrichAsync(2);

        await imageRepository.Received(1).UpdateAsync(
            Arg.Is<Image>(i => i.MissingSinceUtc == clock.UtcNow), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnrichAsync_HashMatchesAnExistingMissingImage_DeletesTheStaleRow()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3 });
            var tempDir = Path.GetDirectoryName(tempFile)!;
            var fileName = Path.GetFileNameWithoutExtension(tempFile);
            var extension = Path.GetExtension(tempFile);

            var image = new Image
            {
                Id = 1,
                FileName = fileName,
                Extension = extension,
                FileSize = 3,
                Folder = new Folder { Id = 5, RelativePath = string.Empty, Root = new ImageRoot { Id = 1, MountPath = tempDir } }
            };
            var staleMissingImage = new Image { Id = 99, MissingSinceUtc = DateTime.UtcNow };

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByIdWithFolderAsync(1, Arg.Any<CancellationToken>()).Returns(image);
            imageRepository.GetByContentHashAsync("hash-abc", Arg.Any<CancellationToken>()).Returns(staleMissingImage);

            var contentHasher = Substitute.For<IContentHasher>();
            contentHasher.ComputeAsync(tempFile, 3, Arg.Any<CancellationToken>()).Returns("hash-abc");

            var exifReader = Substitute.For<IExifReader>();
            exifReader.ReadAsync(tempFile, Arg.Any<CancellationToken>()).Returns(ExifData.Empty);

            var service = new ImageEnrichmentService(imageRepository, contentHasher, exifReader, Substitute.For<IClock>());
            await service.EnrichAsync(1);

            await imageRepository.Received(1).DeleteAsync(staleMissingImage, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
