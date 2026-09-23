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
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private ScanService CreateService() => new(_roots, _folders, _images, _settings, _jobs, _queue, _clock);

    [Fact]
    public async Task StartScanAsync_ExplicitInactiveRoot_Throws_AndCreatesNoJob()
    {
        _roots.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new ImageRoot { Id = 2, Name = "off", MountPath = "/x", IsActive = false });

        var act = () => CreateService().StartScanAsync(rootId: 2, isRecursive: true);

        (await act.Should().ThrowAsync<ScanRootUnavailableException>()).Which.RootId.Should().Be(2);
        await _jobs.DidNotReceive().AddAsync(Arg.Any<ScanJob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_ExplicitUnknownRoot_Throws()
    {
        _roots.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns((ImageRoot?)null);

        var act = () => CreateService().StartScanAsync(rootId: 3, isRecursive: true);

        await act.Should().ThrowAsync<ScanRootUnavailableException>();
    }

    [Fact]
    public async Task StartScanAsync_TombstonedChildFolder_IsNotDescendedIntoOrReindexed()
    {
        var removedDir = Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "removed"));
        await File.WriteAllBytesAsync(Path.Combine(removedDir.FullName, "a.jpg"), new byte[] { 1 });
        _folders.GetByRootAndRelativePathAsync(1, "removed", Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 20, RootId = 1, ParentId = 10, RelativePath = "removed", Name = "removed", IsActive = false });

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

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

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

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

        await CreateService().StartScanAsync(rootId: 1, isRecursive: true);

        await _folders.Received(1).DeleteSubtreeAsync(30, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().DeleteSubtreeAsync(31, Arg.Any<CancellationToken>());
    }
}
