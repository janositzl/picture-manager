using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Roots;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Roots;

public class RootServiceTests
{
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly ImageRoot _photos = new() { Id = 1, Name = "nas-photos", Alias = "family", MountPath = "/images/photos", IsActive = true };
    private readonly ImageRoot _work = new() { Id = 2, Name = "work-nas", MountPath = "/images/work", IsActive = true };

    public RootServiceTests()
    {
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot> { _photos, _work });
    }

    private RootService CreateService() => new(_roots, _folders);

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
}
