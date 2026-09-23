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
            scanJobRepository.TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>()).Returns(true);
            // Nothing enriched anything during this test (the enrichment queue is mocked, not
            // drained), so with a real DB the completion check would correctly see
            // FilesEnriched < FilesFound and leave the job at Enriching.
            scanJobRepository.TryMarkCompletedIfEnrichedAsync(999, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

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
            await scanJobRepository.Received(1).SetEnumerationResultAsync(999, foldersScanned: 1, filesFound: 1, Arg.Any<CancellationToken>());
            await scanJobRepository.Received(1).TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>());
            await scanJobRepository.Received(1).TryMarkCompletedIfEnrichedAsync(999, clock.UtcNow, Arg.Any<CancellationToken>());
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
            // An ignored file keeps the root non-empty (an empty root is treated as an unmounted share).
            await File.WriteAllBytesAsync(Path.Combine(tempRoot.FullName, "readme.txt"), new byte[] { 1 });

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
            appSettingsRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings { ExcludedExtensions = new List<string> { ".txt" } });

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

    [Fact]
    public async Task StartScanAsync_RecursiveWithNestedFolder_ReconcilesMixOfFileStatesAndExcludesExtension()
    {
        var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");
        try
        {
            var subDir = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "sub"));

            await File.WriteAllBytesAsync(Path.Combine(tempRoot.FullName, "keep.jpg"), new byte[] { 1, 2, 3 });
            await File.WriteAllBytesAsync(Path.Combine(tempRoot.FullName, "skip.txt"), new byte[] { 9 });

            var unchangedPath = Path.Combine(subDir.FullName, "unchanged.jpg");
            await File.WriteAllBytesAsync(unchangedPath, new byte[] { 1, 2, 3, 4 });
            var unchangedInfo = new FileInfo(unchangedPath);
            var unchangedModified = TruncateToMicroseconds(unchangedInfo.LastWriteTimeUtc);

            var changedPath = Path.Combine(subDir.FullName, "changed.jpg");
            await File.WriteAllBytesAsync(changedPath, new byte[] { 1, 2, 3, 4, 5 });

            var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = tempRoot.FullName, IsActive = true };
            var rootFolder = new Folder { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };
            var subFolder = new Folder { Id = 20, RootId = 1, ParentId = 10, RelativePath = "sub", Name = "sub" };

            var imageRootRepository = Substitute.For<IImageRootRepository>();
            imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

            var folderRepository = Substitute.For<IFolderRepository>();
            folderRepository.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(rootFolder);
            folderRepository.GetByRootAndRelativePathAsync(1, "sub", Arg.Any<CancellationToken>()).Returns(subFolder);

            var unchangedImage = new Image
            {
                Id = 30, FolderId = 20, FileName = "unchanged", Extension = ".jpg",
                FileSize = unchangedInfo.Length, FileModified = unchangedModified, MissingSinceUtc = null
            };
            var changedImage = new Image
            {
                Id = 31, FolderId = 20, FileName = "changed", Extension = ".jpg",
                FileSize = 1, FileModified = DateTime.UtcNow.AddDays(-10), MissingSinceUtc = null
            };

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByFolderAndFileNameAsync(10, "keep", ".jpg", Arg.Any<CancellationToken>()).Returns((Image?)null);
            imageRepository.GetByFolderAndFileNameAsync(20, "unchanged", ".jpg", Arg.Any<CancellationToken>()).Returns(unchangedImage);
            imageRepository.GetByFolderAndFileNameAsync(20, "changed", ".jpg", Arg.Any<CancellationToken>()).Returns(changedImage);
            imageRepository.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image>());
            imageRepository.GetByFolderIdAsync(20, Arg.Any<CancellationToken>()).Returns(new List<Image> { unchangedImage, changedImage });
            imageRepository.AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var image = callInfo.Arg<Image>();
                image.Id = 100;
                return image;
            });

            var appSettingsRepository = Substitute.For<IAppSettingsRepository>();
            appSettingsRepository.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings { ExcludedExtensions = new List<string> { ".txt" } });

            var scanJobRepository = Substitute.For<IScanJobRepository>();
            scanJobRepository.AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var job = callInfo.Arg<ScanJob>();
                job.Id = 999;
                return job;
            });
            scanJobRepository.TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>()).Returns(true);
            scanJobRepository.TryMarkCompletedIfEnrichedAsync(999, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

            var enrichmentQueue = Substitute.For<IEnrichmentQueue>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var scanService = new ScanService(
                imageRootRepository, folderRepository, imageRepository,
                appSettingsRepository, scanJobRepository, enrichmentQueue, clock);

            await scanService.StartScanAsync(rootId: 1, isRecursive: true);

            // keep.jpg: New -> created and enqueued
            await imageRepository.Received(1).AddAsync(
                Arg.Is<Image>(i => i.FolderId == 10 && i.FileName == "keep" && i.Extension == ".jpg"),
                Arg.Any<CancellationToken>());
            enrichmentQueue.Received(1).Enqueue(999, 100);

            // skip.txt: excluded extension -> never touches the repository at all
            await imageRepository.DidNotReceive().AddAsync(
                Arg.Is<Image>(i => i.FileName == "skip"), Arg.Any<CancellationToken>());

            // unchanged.jpg: Unchanged -> no update, no enqueue
            await imageRepository.DidNotReceive().UpdateAsync(
                Arg.Is<Image>(i => i.Id == 30), Arg.Any<CancellationToken>());

            // changed.jpg: Modified -> updated and enqueued
            await imageRepository.Received(1).UpdateAsync(
                Arg.Is<Image>(i => i.Id == 31 && i.IndexState == IndexState.Pending && i.MissingSinceUtc == null),
                Arg.Any<CancellationToken>());
            enrichmentQueue.Received(1).Enqueue(999, 31);

            // FilesFound counts only what was actually enqueued (keep + changed), not the unchanged file.
            await scanJobRepository.Received(1).SetEnumerationResultAsync(999, foldersScanned: 2, filesFound: 2, Arg.Any<CancellationToken>());
            await scanJobRepository.Received(1).TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task StartScanAsync_SameFileNameInTwoFolders_MissingDiffIsScopedPerFolderNotGlobal()
    {
        var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "sub"));

            var sharedPath = Path.Combine(tempRoot.FullName, "shared.jpg");
            await File.WriteAllBytesAsync(sharedPath, new byte[] { 1, 2, 3 });
            var sharedInfo = new FileInfo(sharedPath);
            var sharedModified = TruncateToMicroseconds(sharedInfo.LastWriteTimeUtc);
            // Note: "sub" contains no shared.jpg on disk.

            var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = tempRoot.FullName, IsActive = true };
            var rootFolder = new Folder { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };
            var subFolder = new Folder { Id = 20, RootId = 1, ParentId = 10, RelativePath = "sub", Name = "sub" };

            // Two distinct Image rows, same FileName+Extension, in two different folders.
            var imageInRoot = new Image
            {
                Id = 40, FolderId = 10, FileName = "shared", Extension = ".jpg",
                FileSize = sharedInfo.Length, FileModified = sharedModified, MissingSinceUtc = null
            };
            var imageInSub = new Image
            {
                Id = 41, FolderId = 20, FileName = "shared", Extension = ".jpg",
                FileSize = 999, FileModified = DateTime.UtcNow.AddDays(-30), MissingSinceUtc = null
            };

            var imageRootRepository = Substitute.For<IImageRootRepository>();
            imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

            var folderRepository = Substitute.For<IFolderRepository>();
            folderRepository.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(rootFolder);
            folderRepository.GetByRootAndRelativePathAsync(1, "sub", Arg.Any<CancellationToken>()).Returns(subFolder);

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByFolderAndFileNameAsync(10, "shared", ".jpg", Arg.Any<CancellationToken>()).Returns(imageInRoot);
            imageRepository.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image> { imageInRoot });
            imageRepository.GetByFolderIdAsync(20, Arg.Any<CancellationToken>()).Returns(new List<Image> { imageInSub });

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

            // Root's copy: file is present there and unchanged -> never updated.
            await imageRepository.DidNotReceive().UpdateAsync(
                Arg.Is<Image>(i => i.Id == 40), Arg.Any<CancellationToken>());

            // Sub's copy: file is absent in THAT folder -> marked missing, proving the diff
            // is computed per-folder rather than against a scan-wide observed-files set.
            await imageRepository.Received(1).UpdateAsync(
                Arg.Is<Image>(i => i.Id == 41 && i.MissingSinceUtc == clock.UtcNow),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task StartScanAsync_NonRecursive_CreatesChildFolderRowButDoesNotWalkIntoIt()
    {
        var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");
        try
        {
            var subDir = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "sub"));
            await File.WriteAllBytesAsync(Path.Combine(subDir.FullName, "nested.jpg"), new byte[] { 1, 2, 3 });

            var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = tempRoot.FullName, IsActive = true };
            var rootFolder = new Folder { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };

            var imageRootRepository = Substitute.For<IImageRootRepository>();
            imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

            var folderRepository = Substitute.For<IFolderRepository>();
            folderRepository.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(rootFolder);
            folderRepository.GetByRootAndRelativePathAsync(1, "sub", Arg.Any<CancellationToken>()).Returns((Folder?)null);
            folderRepository.AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            {
                var folder = callInfo.Arg<Folder>();
                folder.Id = 20;
                return folder;
            });

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image>());

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

            await scanService.StartScanAsync(rootId: 1, isRecursive: false);

            // Child Folder row is still created, for tree visibility.
            await folderRepository.Received(1).AddAsync(
                Arg.Is<Folder>(f => f.RootId == 1 && f.ParentId == 10 && f.RelativePath == "sub" && f.Name == "sub"),
                Arg.Any<CancellationToken>());

            // But it is never walked into: no per-file/per-folder lookups happen against it.
            await imageRepository.DidNotReceive().GetByFolderAndFileNameAsync(
                20, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            await imageRepository.DidNotReceive().GetByFolderIdAsync(20, Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task StartScanAsync_DiskFileNameDiffersOnlyByCaseFromDbFileName_DoesNotMarkExistingImageMissing()
    {
        var tempRoot = Directory.CreateTempSubdirectory("pm-scan-test-");
        try
        {
            var filePath = Path.Combine(tempRoot.FullName, "photo.jpg");
            await File.WriteAllBytesAsync(filePath, new byte[] { 1, 2, 3 });
            var diskInfo = new FileInfo(filePath);
            var truncatedModified = TruncateToMicroseconds(diskInfo.LastWriteTimeUtc);

            var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = tempRoot.FullName, IsActive = true };
            var rootFolder = new Folder { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };

            // DB has "Photo" (capitalized); disk has "photo" (lowercase). The repository's
            // case-insensitive lookup finds it (so reconciliation succeeds), but the missing-file
            // diff must ALSO treat these as the same key, or it stamps MissingSinceUtc right back on
            // a file that plainly exists.
            var existingImage = new Image
            {
                Id = 5, FolderId = 10, FileName = "Photo", Extension = ".jpg",
                FileSize = diskInfo.Length, FileModified = truncatedModified, MissingSinceUtc = null
            };

            var imageRootRepository = Substitute.For<IImageRootRepository>();
            imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

            var folderRepository = Substitute.For<IFolderRepository>();
            folderRepository.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(rootFolder);

            var imageRepository = Substitute.For<IImageRepository>();
            imageRepository.GetByFolderAndFileNameAsync(10, "photo", ".jpg", Arg.Any<CancellationToken>()).Returns(existingImage);
            imageRepository.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image> { existingImage });

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

            // Unchanged (matched by case-insensitive lookup) and not marked missing: no update at all.
            await imageRepository.DidNotReceive().UpdateAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task StartScanAsync_EnrichmentAlreadyFinishedBeforeFinalWrite_CompletesInsteadOfStayingAtEnriching()
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

            // Simulate EnrichmentBackgroundService racing ahead of ScanService's own bookkeeping:
            // by the time ScanService's final write runs, the one queued item has already been
            // enriched in a DIFFERENT scope/DbContext -- with a real DB, TryMarkCompletedIfEnrichedAsync
            // would then see FilesEnriched >= FilesFound and flip the job to Completed itself
            // (whichever of "transition to Enriching" or the last increment runs last is the one
            // whose completion check actually fires). Stubbing it to return true here proves
            // ScanService always attempts this call after transitioning to Enriching, regardless
            // of whether it "won" the race to be the one that actually flips the row.
            scanJobRepository.TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>()).Returns(true);
            scanJobRepository.TryMarkCompletedIfEnrichedAsync(999, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

            var enrichmentQueue = Substitute.For<IEnrichmentQueue>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var scanService = new ScanService(
                imageRootRepository, folderRepository, imageRepository,
                appSettingsRepository, scanJobRepository, enrichmentQueue, clock);

            await scanService.StartScanAsync(rootId: 1, isRecursive: true);

            await scanJobRepository.Received(1).SetEnumerationResultAsync(999, foldersScanned: 1, filesFound: 1, Arg.Any<CancellationToken>());
            await scanJobRepository.Received(1).TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>());
            await scanJobRepository.Received(1).TryMarkCompletedIfEnrichedAsync(999, clock.UtcNow, Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task StartScanAsync_GenericExceptionDuringScan_RecordsFailedStatus_AndRethrows()
    {
        var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = "/nonexistent", IsActive = true };

        var imageRootRepository = Substitute.For<IImageRootRepository>();
        imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

        var appSettingsRepository = Substitute.For<IAppSettingsRepository>();
        appSettingsRepository.GetAsync(Arg.Any<CancellationToken>())
            .Returns<Task<AppSettings>>(_ => throw new InvalidOperationException("boom"));

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
            imageRootRepository, Substitute.For<IFolderRepository>(), Substitute.For<IImageRepository>(),
            appSettingsRepository, scanJobRepository, Substitute.For<IEnrichmentQueue>(), clock);

        var act = () => scanService.StartScanAsync(rootId: 1, isRecursive: true);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await scanJobRepository.Received(1).SetFailureResultAsync(
            999, foldersScanned: 0, filesFound: 0, errorMessage: "boom", status: ScanJobStatus.Failed,
            completedUtc: clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_OperationCanceledExceptionDuringScan_RecordsCancelledStatus_WithNoErrorMessage()
    {
        var imageRoot = new ImageRoot { Id = 1, Name = "dev", MountPath = "/nonexistent", IsActive = true };

        var imageRootRepository = Substitute.For<IImageRootRepository>();
        imageRootRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(imageRoot);

        var appSettingsRepository = Substitute.For<IAppSettingsRepository>();
        appSettingsRepository.GetAsync(Arg.Any<CancellationToken>())
            .Returns<Task<AppSettings>>(_ => throw new OperationCanceledException());

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
            imageRootRepository, Substitute.For<IFolderRepository>(), Substitute.For<IImageRepository>(),
            appSettingsRepository, scanJobRepository, Substitute.For<IEnrichmentQueue>(), clock);

        var act = () => scanService.StartScanAsync(rootId: 1, isRecursive: true);

        await act.Should().ThrowAsync<OperationCanceledException>();

        await scanJobRepository.Received(1).SetFailureResultAsync(
            999, foldersScanned: 0, filesFound: 0, errorMessage: null, status: ScanJobStatus.Cancelled,
            completedUtc: clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_WhenAScanIsAlreadyActive_ThrowsAndNeverCreatesAJob()
    {
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        scanJobRepository.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        var scanService = new ScanService(
            Substitute.For<IImageRootRepository>(), Substitute.For<IFolderRepository>(), Substitute.For<IImageRepository>(),
            Substitute.For<IAppSettingsRepository>(), scanJobRepository, Substitute.For<IEnrichmentQueue>(), Substitute.For<IClock>());

        var act = () => scanService.StartScanAsync(rootId: 1, isRecursive: true);

        await act.Should().ThrowAsync<ScanAlreadyInProgressException>();

        await scanJobRepository.DidNotReceive().AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>());
    }

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - (value.Ticks % 10), value.Kind);
}
