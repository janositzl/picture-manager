using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Folders;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Folders;

public class FolderServiceTests
{
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IScanJobRepository _scanJobs = Substitute.For<IScanJobRepository>();

    public FolderServiceTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private FolderService CreateService() => new(_folders, _scanJobs, _clock);

    [Fact]
    public async Task GetChildrenAsync_ParentNotVisible_ReturnsNotFound()
    {
        _folders.IsVisibleAsync(4, Arg.Any<CancellationToken>()).Returns(false);

        (await CreateService().GetChildrenAsync(4)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetChildrenAsync_ParentVisible_ReturnsChildren()
    {
        var children = new[] { new FolderNode(5, "a", false, 3, false) };
        _folders.IsVisibleAsync(4, Arg.Any<CancellationToken>()).Returns(true);
        _folders.GetVisibleChildrenAsync(4, Arg.Any<CancellationToken>()).Returns(children);

        (await CreateService().GetChildrenAsync(4)).Value.Should().BeEquivalentTo(children);
    }

    [Fact]
    public async Task GetAsync_NotVisible_ReturnsNotFound()
    {
        _folders.GetVisibleDetailAsync(4, Arg.Any<CancellationToken>()).Returns((FolderDetail?)null);

        (await CreateService().GetAsync(4)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RemoveAsync_UnknownFolder_ReturnsNotFound()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns((Folder?)null);

        (await CreateService().RemoveAsync(4)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RemoveAsync_AlreadyRemoved_ReturnsNotFound()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = 1, IsActive = false });

        (await CreateService().RemoveAsync(4)).Status.Should().Be(ResultStatus.NotFound);
        await _folders.DidNotReceive().RemoveFromCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_RootTopFolder_ReturnsInvalid()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = null, IsActive = true });

        (await CreateService().RemoveAsync(4)).Status.Should().Be(ResultStatus.Invalid);
        await _folders.DidNotReceive().RemoveFromCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_WhileScanIsActive_ReturnsConflict_AndRemovesNothing()
    {
        // A running scan holds Folder objects it loaded earlier; removing the subtree underneath it
        // would let the scan re-create rows under the tombstone (or fail on a deleted parent).
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = 1, IsActive = true });
        _scanJobs.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().RemoveAsync(4)).Status.Should().Be(ResultStatus.Conflict);
        await _folders.DidNotReceive().RemoveFromCollectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_ActiveSubfolder_RemovesFromCollection()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = 1, IsActive = true });

        (await CreateService().RemoveAsync(4)).IsSuccess.Should().BeTrue();
        await _folders.Received(1).RemoveFromCollectionAsync(4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_NotRemoved_ReturnsInvalid()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 4, ParentId = 1, IsActive = true });

        (await CreateService().RestoreAsync(4)).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task RestoreAsync_Tombstone_ReactivatesIt()
    {
        var folder = new Folder { Id = 4, ParentId = 1, IsActive = false };
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns(folder);

        (await CreateService().RestoreAsync(4)).IsSuccess.Should().BeTrue();
        await _folders.Received(1).UpdateAsync(
            Arg.Is<Folder>(f => f.Id == 4 && f.IsActive && f.ModifiedUtc == _clock.UtcNow), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_Unknown_ReturnsNotFound()
    {
        _folders.GetByIdAsync(4, Arg.Any<CancellationToken>()).Returns((Folder?)null);

        (await CreateService().RestoreAsync(4)).Status.Should().Be(ResultStatus.NotFound);
    }
}
