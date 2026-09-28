using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Discovery;
using PictureManager.Application.Repositories;
using PictureManager.Application.Roots;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Roots;

public class RootServiceTests
{
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IDiscoveryService _discovery = Substitute.For<IDiscoveryService>();
    private readonly ILogger<RootService> _logger = Substitute.For<ILogger<RootService>>();
    private readonly ImageRoot _photos = new() { Id = 1, Name = "nas-photos", Alias = "family", MountPath = "/images/photos", IsActive = true };
    private readonly ImageRoot _work = new() { Id = 2, Name = "work-nas", MountPath = "/images/work", IsActive = true };

    public RootServiceTests()
    {
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot> { _photos, _work });
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(_photos);
        _roots.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(_work);
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>())
            .Returns(call => { var root = call.Arg<ImageRoot>(); root.Id = 3; return root; });
    }

    private RootService CreateService() => new(_roots, _folders, _clock, _discovery, _logger);

    [Fact]
    public async Task GetAllAsync_IncludesExportSegment()
    {
        var all = await CreateService().GetAllAsync();

        all.Should().Equal(
            new RootSummary(1, "nas-photos", "family", "/images/photos", true, "family"),
            new RootSummary(2, "work-nas", null, "/images/work", true, "work-nas"));
    }

    [Fact]
    public async Task UpdateAsync_UnknownRoot_ReturnsNotFound()
    {
        (await CreateService().UpdateAsync(99, new RootUpdate("x", false, null, null))).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_Rename_SavesRoot_AndRenamesItsTopFolder()
    {
        var result = await CreateService().UpdateAsync(2, new RootUpdate("  Work  ", false, null, null));

        result.Value!.Name.Should().Be("Work");
        await _roots.Received(1).UpdateAsync(Arg.Is<ImageRoot>(r => r.Id == 2 && r.Name == "Work"), Arg.Any<CancellationToken>());
        await _folders.Received(1).RenameRootFolderAsync(2, "Work", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NameClashesCaseInsensitively_ReturnsConflict()
    {
        (await CreateService().UpdateAsync(2, new RootUpdate("NAS-PHOTOS", false, null, null))).Status.Should().Be(ResultStatus.Conflict);
        await _roots.DidNotReceive().UpdateAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_AliasClashesWithAnotherRootsSegment_ReturnsConflict()
    {
        (await CreateService().UpdateAsync(1, new RootUpdate(null, true, "Work-NAS", null))).Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task UpdateAsync_AliasNull_ClearsAlias_AndExportFallsBackToName()
    {
        var result = await CreateService().UpdateAsync(1, new RootUpdate(null, true, null, null));

        result.Value!.Alias.Should().BeNull();
        result.Value.ExportSegment.Should().Be("nas-photos");
        await _folders.DidNotReceive().RenameRootFolderAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("   ")]
    public async Task UpdateAsync_InvalidAlias_ReturnsInvalid(string alias)
    {
        var result = await CreateService().UpdateAsync(1, new RootUpdate(null, true, alias, null));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain("alias");
    }

    [Fact]
    public async Task UpdateAsync_BlankName_ReturnsInvalid()
    {
        var result = await CreateService().UpdateAsync(1, new RootUpdate(" ", false, null, null));

        result.Errors!.Keys.Should().Contain("name");
    }

    [Fact]
    public async Task UpdateAsync_Deactivate_SavesIsActiveFalse()
    {
        var result = await CreateService().UpdateAsync(2, new RootUpdate(null, false, null, false));

        result.Value!.IsActive.Should().BeFalse();
        await _roots.Received(1).UpdateAsync(Arg.Is<ImageRoot>(r => r.Id == 2 && !r.IsActive), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_Valid_SavesRoot_CreatesTopFolder_AndQueuesDiscovery()
    {
        var result = await CreateService().CreateAsync(new RootCreate("archive", "/images/archive", "family_archive"));

        result.Value.Should().Be(new RootSummary(3, "archive", "family_archive", "/images/archive", true, "family_archive"));
        await _roots.Received(1).AddAsync(
            Arg.Is<ImageRoot>(r => r.Name == "archive" && r.MountPath == "/images/archive" && r.Alias == "family_archive" && r.IsActive),
            Arg.Any<CancellationToken>());
        await _folders.Received(1).AddAsync(
            Arg.Is<Folder>(f => f.RootId == 3 && f.ParentId == null && f.RelativePath == "" && f.Name == "archive"),
            Arg.Any<CancellationToken>());
        await _discovery.Received(1).QueueDiscoveryAsync(3, null, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_DuplicateMountPath_ReturnsConflict()
    {
        var result = await CreateService().CreateAsync(new RootCreate("archive", "/images/photos", null));

        result.Status.Should().Be(ResultStatus.Conflict);
        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_NameClashesCaseInsensitively_ReturnsConflict()
    {
        var result = await CreateService().CreateAsync(new RootCreate("NAS-PHOTOS", "/images/new", null));

        result.Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task CreateAsync_AliasClashesWithAnotherRootsSegment_ReturnsConflict()
    {
        var result = await CreateService().CreateAsync(new RootCreate("archive", "/images/archive", "work-nas"));

        result.Status.Should().Be(ResultStatus.Conflict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankMountPath_ReturnsInvalid(string? mountPath)
    {
        var result = await CreateService().CreateAsync(new RootCreate("archive", mountPath, null));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain("mountPath");
    }

    [Fact]
    public async Task CreateAsync_BlankName_ReturnsInvalid()
    {
        var result = await CreateService().CreateAsync(new RootCreate(" ", "/images/archive", null));

        result.Errors!.Keys.Should().Contain("name");
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task CreateAsync_InvalidAlias_ReturnsInvalid(string alias)
    {
        var result = await CreateService().CreateAsync(new RootCreate("archive", "/images/archive", alias));

        result.Errors!.Keys.Should().Contain("alias");
    }

    [Fact]
    public async Task CreateAsync_DiscoveryAlreadyInProgress_StillSucceeds()
    {
        _discovery.QueueDiscoveryAsync(3, null, true, Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new DiscoveryAlreadyInProgressException());

        var result = await CreateService().CreateAsync(new RootCreate("archive", "/images/archive", null));

        result.Status.Should().Be(ResultStatus.Success);
    }

    [Fact]
    public async Task DeleteAsync_UnknownRoot_ReturnsNotFound()
    {
        (await CreateService().DeleteAsync(99)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task DeleteAsync_DeletesTopFolderSubtree_AndTheRoot()
    {
        var topFolder = new Folder { Id = 10, RootId = 2, RelativePath = "" };
        _folders.GetByRootAndRelativePathAsync(2, "", Arg.Any<CancellationToken>()).Returns(topFolder);

        var result = await CreateService().DeleteAsync(2);

        result.Status.Should().Be(ResultStatus.Success);
        await _folders.Received(1).DeleteSubtreeAsync(10, Arg.Any<CancellationToken>());
        await _roots.Received(1).DeleteAsync(2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NoTopFolder_StillDeletesTheRoot()
    {
        var result = await CreateService().DeleteAsync(2);

        result.Status.Should().Be(ResultStatus.Success);
        await _folders.DidNotReceive().DeleteSubtreeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _roots.Received(1).DeleteAsync(2, Arg.Any<CancellationToken>());
    }
}
