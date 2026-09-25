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

public sealed class ScanServiceFolderScopeTests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("pm-scan-folder-");
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRepository _images = Substitute.For<IImageRepository>();
    private readonly IAppSettingsRepository _settings = Substitute.For<IAppSettingsRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IScanQueue _scanQueue = Substitute.For<IScanQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ImageRoot _root;
    private readonly Folder _top = new() { Id = 10, RootId = 1, Name = "dev", RelativePath = string.Empty };
    private readonly Folder _trips = new() { Id = 20, RootId = 1, ParentId = 10, Name = "Trips", RelativePath = "Trips" };
    private readonly Folder _madeira = new() { Id = 21, RootId = 1, ParentId = 20, Name = "Madeira", RelativePath = "Trips/Madeira" };
    private int _nextImageId = 100;

    // dev/
    //   Trips/a.jpg
    //   Trips/Madeira/b.jpg
    //   Other/c.jpg        <- a sibling the folder scan must not touch
    public ScanServiceFolderScopeTests()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Trips", "Madeira"));
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "Other"));
        File.WriteAllBytes(Path.Combine(_tempRoot.FullName, "Trips", "a.jpg"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_tempRoot.FullName, "Trips", "Madeira", "b.jpg"), new byte[] { 2 });
        File.WriteAllBytes(Path.Combine(_tempRoot.FullName, "Other", "c.jpg"), new byte[] { 3 });

        _root = new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true };
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_root);
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(_trips);
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(_top);
        _folders.GetByRootAndRelativePathAsync(1, "Trips/Madeira", Arg.Any<CancellationToken>()).Returns(_madeira);
        _folders.GetChildrenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new List<Folder>());
        _folders.GetChildrenAsync(20, Arg.Any<CancellationToken>()).Returns(new List<Folder> { _madeira });
        _images.GetByFolderIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new List<Image>());
        _images.AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var image = call.Arg<Image>();
            image.Id = _nextImageId++;
            return image;
        });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _jobs.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<Job>();
            job.Id = 999;
            return job;
        });
        _jobs.TryTransitionToEnrichingAsync(999, Arg.Any<CancellationToken>()).Returns(true);
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private ScanService CreateService() =>
        new(_roots, _folders, _images, _settings, _jobs, Substitute.For<IEnrichmentQueue>(), _scanQueue, _clock, new ScanningOptions());

    [Fact]
    public async Task FolderScan_Recursive_ReconcilesOnlyThatSubtree()
    {
        await CreateService().ScanFolderNowAsync(folderId: 20, isRecursive: true);

        await _images.Received(1).AddAsync(Arg.Is<Image>(i => i.FolderId == 20 && i.FileName == "a"), Arg.Any<CancellationToken>());
        await _images.Received(1).AddAsync(Arg.Is<Image>(i => i.FolderId == 21 && i.FileName == "b"), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().AddAsync(Arg.Is<Image>(i => i.FileName == "c"), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().GetByFolderIdAsync(10, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 2, filesFound: 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FolderScan_NonRecursive_ProcessesOnlyItsOwnFiles()
    {
        await CreateService().ScanFolderNowAsync(folderId: 20, isRecursive: false);

        await _images.Received(1).AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        await _images.Received(1).AddAsync(Arg.Is<Image>(i => i.FolderId == 20 && i.FileName == "a"), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 1, filesFound: 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueScanAsync_Folder_StoresItAsTheJobScope()
    {
        await CreateService().QueueScanAsync(rootId: null, folderId: 20, isRecursive: true);

        await _jobs.Received(1).AddAsync(
            Arg.Is<Job>(j => j.Kind == JobKind.Scan && j.FolderId == 20 && j.IsRecursive && j.Status == JobStatus.Enumerating),
            Arg.Any<CancellationToken>());
        _scanQueue.Received(1).Enqueue(new QueuedScan(999, null, 20, true));
    }

    [Fact]
    public async Task QueueScanAsync_Root_StoresItsTopFolderAsTheJobScope()
    {
        await CreateService().QueueScanAsync(rootId: 1, folderId: null, isRecursive: true);

        await _jobs.Received(1).AddAsync(Arg.Is<Job>(j => j.Kind == JobKind.Scan && j.FolderId == 10), Arg.Any<CancellationToken>());
        _scanQueue.Received(1).Enqueue(new QueuedScan(999, 1, null, true));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("removed")]
    [InlineData("inactive root")]
    public async Task QueueScanAsync_FolderNotVisible_Throws_AndCreatesNoJob(string reason)
    {
        switch (reason)
        {
            case "unknown":
                _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns((Folder?)null);
                break;
            case "removed":
                _trips.IsActive = false;
                break;
            case "inactive root":
                _root.IsActive = false;
                break;
        }

        var act = () => CreateService().QueueScanAsync(rootId: null, folderId: 20, isRecursive: true);

        (await act.Should().ThrowAsync<FolderUnavailableException>()).Which.FolderId.Should().Be(20);
        await _jobs.DidNotReceive().AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueScanAsync_FolderMarkedMissing_Throws()
    {
        _trips.MissingSinceUtc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var act = () => CreateService().QueueScanAsync(rootId: null, folderId: 20, isRecursive: true);

        (await act.Should().ThrowAsync<FolderUnavailableException>())
            .WithMessage("Folder 20 is missing on disk. Scan or discover its parent folder instead.");
    }

    [Fact]
    public async Task RunScanAsync_FolderRemovedWhileQueued_FailsTheJob()
    {
        _trips.IsActive = false;

        var act = () => CreateService().RunScanAsync(new QueuedScan(999, null, 20, true));

        await act.Should().ThrowAsync<FolderUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0,
            "Folder 20 does not exist, was removed from the collection, or its root is inactive.",
            JobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunScanAsync_FolderRootUnavailable_FailsTheJobAndChangesNothing()
    {
        _root.MountPath = Path.Combine(_tempRoot.FullName, "not-mounted");

        var act = () => CreateService().RunScanAsync(new QueuedScan(999, null, 20, true));

        await act.Should().ThrowAsync<ScanRootsUnavailableException>();
        await _images.DidNotReceive().AddAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, Arg.Is<string>(m => m.Contains("Root 'dev' is unavailable")),
            JobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunScanAsync_FolderGoneFromDisk_FailsWithoutMarkingAnything()
    {
        Directory.Delete(Path.Combine(_tempRoot.FullName, "Trips"), recursive: true);

        var act = () => CreateService().RunScanAsync(new QueuedScan(999, null, 20, true));

        await act.Should().ThrowAsync<FolderNotOnDiskException>().WithMessage("Folder is no longer on disk: dev/Trips");
        await _folders.DidNotReceive().MarkSubtreeMissingAsync(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _images.DidNotReceive().UpdateAsync(Arg.Any<Image>(), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, "Folder is no longer on disk: dev/Trips",
            JobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }
}
