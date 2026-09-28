using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Discovery;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Discovery;

public sealed class DiscoveryServiceTests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("pm-discovery-");
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IAppSettingsRepository _settings = Substitute.For<IAppSettingsRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IDiscoveryQueue _discoveryQueue = Substitute.For<IDiscoveryQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Folder _rootFolder = new() { Id = 10, RootId = 1, RelativePath = string.Empty, Name = "dev" };

    public DiscoveryServiceTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true });
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>()).Returns(_rootFolder);
        _folders.GetChildrenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(new List<Folder>());
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _jobs.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<Job>();
            job.Id = 999;
            return job;
        });
        _folders.AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var folder = call.Arg<Folder>();
            folder.Id = 60;
            return folder;
        });
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private DiscoveryService CreateService() => new(_roots, _folders, _settings, _jobs, _discoveryQueue, _clock);

    [Fact]
    public async Task QueueDiscoveryAsync_CreatesEnumeratingDiscoveryJob_EnqueuesIt_AndDoesNotWalk()
    {
        var discoveryJobId = await CreateService().QueueDiscoveryAsync(rootId: 1, folderId: null);

        discoveryJobId.Should().Be(999);
        await _jobs.Received(1).AddAsync(
            Arg.Is<Job>(j => j.Kind == JobKind.Discovery && j.Status == JobStatus.Enumerating && j.IsRecursive),
            Arg.Any<CancellationToken>());
        _discoveryQueue.Received(1).Enqueue(new QueuedDiscovery(999, 1, null));
        await _folders.DidNotReceive().GetChildrenAsync(Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueDiscoveryAsync_AnotherJobAlreadyActive_ThrowsAndEnqueuesNothing()
    {
        _jobs.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateService().QueueDiscoveryAsync(rootId: 1, folderId: null);

        await act.Should().ThrowAsync<DiscoveryAlreadyInProgressException>();
        _discoveryQueue.DidNotReceive().Enqueue(Arg.Any<QueuedDiscovery>());
    }

    [Fact]
    public async Task QueueDiscoveryAsync_NeitherRootNorFolderGiven_Throws()
    {
        var act = () => CreateService().QueueDiscoveryAsync(rootId: null, folderId: null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunDiscoveryAsync_WalksSubfoldersOnly_AndCompletesWithoutEnriching()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "2025"));
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });

        var discoveryJobId = await CreateService().DiscoverNowAsync(rootId: 1);

        discoveryJobId.Should().Be(999);
        await _folders.Received(1).AddAsync(
            Arg.Is<Folder>(f => f.RootId == 1 && f.ParentId == 10 && f.Name == "2025"), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 2, filesFound: 0, Arg.Any<CancellationToken>());
        await _jobs.Received(1).TryMarkCompletedFromEnumeratingAsync(999, _clock.UtcNow, Arg.Any<CancellationToken>());
        await _jobs.DidNotReceive().TryTransitionToEnrichingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_SetsChildrenDiscoveredAtAndLastWriteTimeUtc_OnWalkedFolder()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "2025"));

        await CreateService().DiscoverNowAsync(rootId: 1);

        _rootFolder.ChildrenDiscoveredAt.Should().Be(_clock.UtcNow);
        _rootFolder.LastWriteTimeUtc.Should().NotBeNull();
        await _folders.Received(1).UpdateAsync(_rootFolder, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_TombstonedChildFolder_IsNotDescendedIntoOrReindexed()
    {
        var removedDir = Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "removed"));
        Directory.CreateDirectory(Path.Combine(removedDir.FullName, "inner"));
        _folders.GetByRootAndRelativePathAsync(1, "removed", Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 20, RootId = 1, ParentId = 10, RelativePath = "removed", Name = "removed", IsActive = false });

        await CreateService().DiscoverNowAsync(rootId: 1);

        await _folders.DidNotReceive().AddAsync(Arg.Is<Folder>(f => f.Name == "inner"), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 1, filesFound: 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_ExcludedChildFolder_IsNotDescendedIntoOrReindexed()
    {
        var excludedDir = Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "excluded"));
        Directory.CreateDirectory(Path.Combine(excludedDir.FullName, "inner"));
        _folders.GetByRootAndRelativePathAsync(1, "excluded", Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 25, RootId = 1, ParentId = 10, RelativePath = "excluded", Name = "excluded", IsExcluded = true });

        await CreateService().DiscoverNowAsync(rootId: 1);

        await _folders.DidNotReceive().AddAsync(Arg.Is<Folder>(f => f.Name == "inner"), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(999, foldersScanned: 1, filesFound: 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueDiscoveryAsync_ExcludedFolder_Throws()
    {
        var folder = new Folder { Id = 15, RootId = 1, ParentId = 10, RelativePath = "sub", Name = "sub", IsExcluded = true };
        _folders.GetByIdAsync(15, Arg.Any<CancellationToken>()).Returns(folder);

        var act = () => CreateService().QueueDiscoveryAsync(rootId: null, folderId: 15);

        (await act.Should().ThrowAsync<FolderUnavailableException>()).Which.FolderId.Should().Be(15);
    }

    [Fact]
    public async Task QueueDiscoveryAsync_FolderUnderAnExcludedAncestor_Throws()
    {
        var folder = new Folder { Id = 15, RootId = 1, ParentId = 10, RelativePath = "sub", Name = "sub" };
        _folders.GetByIdAsync(15, Arg.Any<CancellationToken>()).Returns(folder);
        _folders.HasExcludedAncestorAsync(15, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateService().QueueDiscoveryAsync(rootId: null, folderId: 15);

        await act.Should().ThrowAsync<FolderUnavailableException>();
    }

    [Fact]
    public async Task RunDiscoveryAsync_ExistingChildFolderWithNowExcludedName_IsDeletedWithSubtree()
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

        await CreateService().DiscoverNowAsync(rootId: 1);

        await _folders.Received(1).DeleteSubtreeAsync(30, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().DeleteSubtreeAsync(31, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_VanishedChildFolder_IsMarkedMissingWithItsSubtree()
    {
        // An unrelated entry keeps the root "available" (IsRootAvailable) while "Trip" stays absent from disk.
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });
        _folders.GetChildrenAsync(10, Arg.Any<CancellationToken>()).Returns(new List<Folder>
        {
            new() { Id = 40, RootId = 1, ParentId = 10, Name = "Trip", RelativePath = "Trip" }
        });

        await CreateService().DiscoverNowAsync(rootId: 1);

        await _folders.Received(1).MarkSubtreeMissingAsync(40, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_ReturningChildFolder_ClearsMissingSinceUtc()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "back"));
        var backFolder = new Folder { Id = 50, RootId = 1, ParentId = 10, Name = "back", RelativePath = "back", MissingSinceUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
        _folders.GetByRootAndRelativePathAsync(1, "back", Arg.Any<CancellationToken>()).Returns(backFolder);

        await CreateService().DiscoverNowAsync(rootId: 1);

        // Updated twice: once here to clear MissingSinceUtc, once more when the walk reaches "back" itself
        // and records its own ChildrenDiscoveredAt/LastWriteTimeUtc.
        backFolder.MissingSinceUtc.Should().BeNull();
        await _folders.Received(2).UpdateAsync(backFolder, Arg.Any<CancellationToken>());
    }

    private const string DevUnavailable = "Root 'dev' is unavailable: its folder is missing or empty. Check that the share is mounted.";

    [Fact]
    public async Task RunDiscoveryAsync_RootFolderEmpty_ChangesNothing_AndFailsWithMessage()
    {
        var act = () => CreateService().DiscoverNowAsync(rootId: 1);

        await act.Should().ThrowAsync<ScanRootsUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, DevUnavailable, JobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().UpdateAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueDiscoveryAsync_NonRecursive_StoresItOnTheJob()
    {
        await CreateService().QueueDiscoveryAsync(rootId: 1, folderId: null, isRecursive: false);

        await _jobs.Received(1).AddAsync(Arg.Is<Job>(j => j.Kind == JobKind.Discovery && !j.IsRecursive), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_NonRecursive_DiscoversOnlyTheTargetsDirectChildren()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "A", "A1"));

        var discoveryJobId = await CreateService().DiscoverNowAsync(rootId: 1, isRecursive: false);

        await _folders.Received(1).AddAsync(Arg.Is<Folder>(f => f.RootId == 1 && f.ParentId == 10 && f.Name == "A"), Arg.Any<CancellationToken>());
        await _folders.DidNotReceive().AddAsync(Arg.Is<Folder>(f => f.Name == "A1"), Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetEnumerationResultAsync(discoveryJobId, foldersScanned: 1, filesFound: 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_FolderScoped_ResolvesAndWalksThatFolderOnly()
    {
        var subFolder = new Folder { Id = 15, RootId = 1, ParentId = 10, RelativePath = "sub", Name = "sub" };
        _folders.GetByIdAsync(15, Arg.Any<CancellationToken>()).Returns(subFolder);
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "sub"));
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "sub", "inner"));

        var discoveryJobId = await CreateService().DiscoverFolderNowAsync(folderId: 15);

        discoveryJobId.Should().Be(999);
        await _folders.Received(1).AddAsync(
            Arg.Is<Folder>(f => f.ParentId == 15 && f.Name == "inner"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunDiscoveryAsync_FolderGoneFromDisk_Throws()
    {
        // An unrelated entry keeps the root itself "available" (IsRootAvailable) while "sub" stays absent,
        // isolating the folder-not-found check from the root-unavailable one.
        Directory.CreateDirectory(Path.Combine(_tempRoot.FullName, "other"));
        var subFolder = new Folder { Id = 15, RootId = 1, ParentId = 10, RelativePath = "sub", Name = "sub" };
        _folders.GetByIdAsync(15, Arg.Any<CancellationToken>()).Returns(subFolder);

        var act = () => CreateService().DiscoverFolderNowAsync(folderId: 15);

        await act.Should().ThrowAsync<FolderNotOnDiskException>();
    }
}
