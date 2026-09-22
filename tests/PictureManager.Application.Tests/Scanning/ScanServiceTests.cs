using System;
using System.Collections.Generic;
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

public class ScanServiceTests
{
    [Fact]
    public async Task StartScanAsync_NewFileInRoot_CreatesPendingImage_AndEnqueuesForEnrichment()
    {
        var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(tempRoot.FullName, "photo.jpg"), new byte[] { 1, 2, 3 });

            var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = tempRoot.FullName, IsActive = true };
            var rootFolder = new Folder { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };

            var imageRootRepository = Substitute.For<IImageRootRepository>();
            imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

            var folderRepository = Substitute.For<IFolderRepository>();
            folderRepository.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(rootFolder);

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByFolderAndFileNameAsync(10, "photo", ".jpg", Arg.Any<CancellationToken>()).Returns((Image?)null);
            imageRepository.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image>());
            imageRepository.AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var image = callInfo.Arg<Image>();
                image.Id = 100;
                return image;
            });

            var appSettingsRepository = Substitute.For<IAppSettingsRepository>();
            appSettingsRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());

            var scanJobRepository = Substitute.For<IScanJobRepository>();
            scanJobRepository.AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var job = callInfo.Arg<ScanJob>();
                job.Id = 999;
                return job;
            });

            var enrichmentQueue = Substitute.For<IEnrichmentQueue>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var scanService = new ScanService(
                imageRootRepository, folderRepository, imageRepository,
                appSettingsRepository, scanJobRepository, enrichmentQueue, clock);

            var scanJobId = await scanService.StartScanAsync(rootId: 1, isRecursive: true);

            scanJobId.Should().Be(999);
            await imageRepository.Received(1).AddAsync(
                Arg.Is<Image>(i => i.FolderId == 10 && i.FileName == "photo" && i.Extension == ".jpg" && i.IndexState == IndexState.Pending),
                Arg.Any<CancellationToken>());
            enrichmentQueue.Received(1).Enqueue(999, 100);
            await scanJobRepository.Received(1).UpdateAsync(
                Arg.Is<ScanJob>(j => j.FilesFound == 1 && j.Status == ScanJobStatus.Enriching),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task StartScanAsync_FileNoLongerPresent_MarksExistingImageMissing()
    {
        var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");
        try
        {
            var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = tempRoot.FullName, IsActive = true };
            var rootFolder = new Folder { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };
            var missingImage = new Image { Id = 5, FolderId = 10, FileName = "gone", Extension = ".jpg", MissingSinceUtc = null };

            var imageRootRepository = Substitute.For<IImageRootRepository>();
            imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

            var folderRepository = Substitute.For<IFolderRepository>();
            folderRepository.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(rootFolder);

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image> { missingImage });

            var appSettingsRepository = Substitute.For<IAppSettingsRepository>();
            appSettingsRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());

            var scanJobRepository = Substitute.For<IScanJobRepository>();
            scanJobRepository.AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var job = callInfo.Arg<ScanJob>();
                job.Id = 999;
                return job;
            });

            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var scanService = new ScanService(
                imageRootRepository, folderRepository, imageRepository,
                appSettingsRepository, scanJobRepository, Substitute.For<IEnrichmentQueue>(), clock);

            await scanService.StartScanAsync(rootId: 1, isRecursive: true);

            await imageRepository.Received(1).UpdateAsync(
                Arg.Is<Image>(i => i.Id == 5 && i.MissingSinceUtc == clock.UtcNow),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }
}
