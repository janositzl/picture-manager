using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class ScanTargetsJobFolderTests
{
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();

    public ScanTargetsJobFolderTests()
    {
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new ImageRoot { Id = 1, Name = "nas", IsActive = true });
    }

    [Fact]
    public async Task Neither_ReturnsNull()
    {
        (await ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, null, null, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Root_ReturnsItsTopFolder()
    {
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 10, RootId = 1, RelativePath = string.Empty });

        (await ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, 1, null, CancellationToken.None)).Should().Be(10);
    }

    [Fact]
    public async Task MissingFolder_Throws()
    {
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 20, RootId = 1, IsActive = true, MissingSinceUtc = DateTime.UtcNow });

        var act = () => ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, null, 20, CancellationToken.None);

        await act.Should().ThrowAsync<FolderUnavailableException>();
    }

    [Fact]
    public async Task FolderUnderExcludedAncestor_Throws()
    {
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 20, RootId = 1, IsActive = true });
        _folders.HasExcludedAncestorAsync(20, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, null, 20, CancellationToken.None);

        await act.Should().ThrowAsync<FolderUnavailableException>();
    }

    [Fact]
    public async Task VisibleFolder_ReturnsIt()
    {
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 20, RootId = 1, IsActive = true });

        (await ScanTargets.ResolveJobFolderIdAsync(_folders, _roots, null, 20, CancellationToken.None)).Should().Be(20);
    }
}
