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

public sealed class ScanServicePhase5Tests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("pm-scan-p5-");
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRepository _images = Substitute.For<IImageRepository>();
    private readonly IAppSettingsRepository _settings = Substitute.For<IAppSettingsRepository>();
    private readonly IScanJobRepository _jobs = Substitute.For<IScanJobRepository>();
    private readonly IEnrichmentQueue _queue = Substitute.For<IEnrichmentQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Folder _rootFolder = new() { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };

    public ScanServicePhase5Tests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true });
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(_rootFolder);
        _folders.GetChildrenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new List<Folder>());
        _images.GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<Image>());
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _jobs.AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<ScanJob>();
            job.Id = 999;
            return job;
        });
        _jobs.TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>()).Returns(true);
        _images.AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var image = call.Arg<Image>();
            image.Id = 70;
            return image;
        });
        _folders.AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var folder = call.Arg<Folder>();
            folder.Id = 60;
            return folder;
        });
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private ScanService CreateService() => new(_roots, _folders, _images, _settings, _jobs, _queue, Substitute.For<IScanQueue>(), _clock);

    [Fact]
    public async Task StartScanAsync_ExplicitInactiveRoot_Throws_AndCreatesNoJob()
    {
        _roots.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new ImageRoot { Id = 2, Name = "off", MountPath = "/x", IsActive = false });

        var act = () => CreateService().ScanNowAsync(rootId: 2, isRecursive: true);

        (await act.Should().ThrowAsync<ScanRootUnavailableException>()).Which.RootId.Should().Be(2);
        await _jobs.DidNotReceive().AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ExplicitUnknownRoot_Throws()
    {
        _roots.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns((ImageRoot?)null);

        var act = () => CreateService().ScanNowAsync(rootId: 3, isRecursive: true);

        await act.Should().ThrowAsync<ScanRootUnavailableException>();
    }

    [Fact]
    public async Task StartScanAsync_TombstonedChildFolder_IsNotDescendedIntoOrReindexed()
    {
        var removedDir = Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "removed"));
        await File.WriteAllBytesAsync(Path.Combine(removedDir.FullName, "a.jpg"), new byte[] { 1 });
        _folders.GetByRootAndRelativePathAsync(1, "removed", Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 20, RootId = 1, ParentId = 10, RelativePath = "removed", Name = "removed", IsActive = false });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await _images.DidNotReceive().GetByFolderAndFileNameAsync(20, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 1, filesFound: 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ExistingImageWithNowExcludedExtension_IsDeleted_NotMarkedMissing()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "x.heic"), new byte[] { 1 });
        var heic = new Image { Id = 5, FolderId = 10, FileName = "x", Extension = ".heic" };
        var goneJpg = new Image { Id = 6, FolderId = 10, FileName = "gone", Extension = ".jpg" };
        _images.GetByFolderIdAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Image> { heic, goneJpg });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings { ExcludedExtensions = new List<string> { ".HEIC" } });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await _images.Received(1).DeleteAsync(heic, Arg.Any<CancellationToken>());
        await _images.DidNotReceive().UpdateAsync(Arg.Is<Image>(i => i.Id == 5), Arg.Any<CancellationToken>());
        // A still-allowed image that vanished from disk keeps the old behaviour: marked missing, not deleted.
        await _images.Received(1).UpdateAsync(Arg.Is<Image>(i => i.Id == 6 && i.MissingSinceUtc != null), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().DeleteAsync(goneJpg, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ExistingChildFolderWithNowExcludedName_IsDeletedWithSubtree()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Raw"));
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 30, RootId = 1, ParentId = 10, Name = "Raw", RelativePath = "Raw" },
            new() { Id = 31, RootId = 1, ParentId = 10, Name = "keep", RelativePath = "keep" }
        });
        _folders.GetByRootAndRelativePathAsync(1, "keep", Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 31, RootId = 1, ParentId = 10, Name = "keep", RelativePath = "keep" });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings { ExcludedFolderNames = new List<string> { "raw" } });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).DeleteSubtreeAsync(30, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().DeleteSubtreeAsync(31, Arg.Any<CancellationToken>());
    }

    private const string DevUnavailable = "Root 'dev' is unavailable: its folder is missing or empty. Check that the share is mounted.";

    [Fact]
    public async Task StartScanAsync_RootFolderMissing_ChangesNothing_AndFailsWithMessage()
    {
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = Path.Combine(_tempRoot.FullName, "not-mounted"), IsActive = true });

        var act = () => CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        (await act.Should().ThrowAsync<ScanRootsUnavailableException>()).Which.RootNames.Should().Equal("dev");
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, DevUnavailable, ScanJobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_RootFolderEmpty_ChangesNothing_AndFailsWithMessage()
    {
        // The fixture's temp root starts empty: exactly what an unmounted share's mount point looks like.
        var act = () => CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await act.Should().ThrowAsync<ScanRootsUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, DevUnavailable, ScanJobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_AllRoots_OneUnavailable_ScansTheOthers_ThenFails()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot>
        {
            new() { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true },
            new() { Id = 2, Name = "nas", MountPath = Path.Combine(_tempRoot.FullName, "not-mounted"), IsActive = true }
        });

        var act = () => CreateService().ScanNowAsync(rootId: null, isRecursive: true);

        (await act.Should().ThrowAsync<ScanRootsUnavailableException>()).Which.RootNames.Should().Equal("nas");
        await _images.Received(1).AddAsync(Arg.Is<Image>(i => i.FileName == "a"), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(2, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetFailureResultAsync(999, 1, 1,
            "Root 'nas' is unavailable: its folder is missing or empty. Check that the share is mounted.",
            ScanJobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_VanishedChildFolder_IsMarkedMissingWithItsSubtree()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" }
        });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).MarkSubtreeMissingAsync(40, _clock.UtcNow, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().DeleteSubtreeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_RenamedChildFolder_OldIsMarkedMissing_NewIsIndexed()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Trip-Italy"));
        var renamed = new Folder { Id = 41, RootId = 1, ParentId = 10, Name = "Trip-Italy", RelativePath = "Trip-Italy" };
        _folders.GetByRootAndRelativePathAsync(1, "Trip-Italy", Arg.Any<CancellationToken>()).Returns(renamed);
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" },
            renamed
        });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).MarkSubtreeMissingAsync(40, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(41, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_MissingFolderBackOnDisk_IsUnmarked()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Trip"));
        var trip = new Folder { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip", MissingSinceUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) };
        _folders.GetByRootAndRelativePathAsync(1, "Trip", Arg.Any<CancellationToken>()).Returns(trip);
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder> { trip });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).UpdateAsync(Arg.Is<Folder>(f => f.Id == 40 && f.MissingSinceUtc == null), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_AlreadyMissingOrRemovedChildren_AreNotMarkedAgain()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 42, RootId = 1, ParentId = 10, Name = "Old", RelativePath = "Old", MissingSinceUtc = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { Id = 43, RootId = 1, ParentId = 10, Name = "Removed", RelativePath = "Removed", IsActive = false }
        });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ChildNameDiffersOnlyInCaseOrUnicodeNormalization_IsNotMarked()
    {
        // On disk: "trip" (case differs) and "Cafe" + combining acute (NFD, as macOS SMB clients write it).
        // Stored: "Trip" and precomposed "Caf\u00e9" (NFC). Escapes keep an editor from normalizing the test away.
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "trip"));
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Cafe\u0301"));
        var trip = new Folder { Id = 44, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" };
        var cafe = new Folder { Id = 45, RootId = 1, ParentId = 10, Name = "Caf\u00e9", RelativePath = "Caf\u00e9" };
        _folders.GetByRootAndRelativePathAsync(1, "trip", Arg.Any<CancellationToken>()).Returns(trip);
        _folders.GetByRootAndRelativePathAsync(1, "Caf\u00e9", Arg.Any<CancellationToken>()).Returns(cafe);
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder> { trip, cafe });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_NonRecursive_DoesNotCheckGrandchildren()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Trip"));
        var trip = new Folder { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" };
        _folders.GetByRootAndRelativePathAsync(1, "Trip", Arg.Any<CancellationToken>()).Returns(trip);
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder> { trip });

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: false);

        await _folders.DidNotReceive().GetChildrenAsync(40, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_WritesProgressEvery50Folders()
    {
        for (var i = 0; i < 50; i++)
            Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, $"f{i:D2}"));

        await CreateService().ScanNowAsync(rootId: 1, isRecursive: true);

        // 51 folders in total (root + 50): one progress write at 50, then the final result.
        await _jobs.Received(1).SetEnumerationResultAsync(999, 50, 0, Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, 51, 0, Arg.Any<CancellationToken>());
    }
}
