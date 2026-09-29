using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Images;

public class ImageQueryServiceTests
{
    private readonly IImageQueryRepository _images = Substitute.For<IImageQueryRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ImageQueryServiceTests()
    {
        _currentUser.UserId.Returns(1);
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _folders.IsVisibleAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        StubRows(Array.Empty<ImageRow>());
    }

    private ImageQueryService CreateService() => new(_images, _folders, _currentUser, _clock);

    private static ImageRow Row(int id, string hash = "H", string name = "img") =>
        new(id, 7, name, ".jpg", 10, 20, null, false, hash,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-id), name.ToLowerInvariant(), "nas", "");

    private void StubRows(IReadOnlyList<ImageRow> rows) =>
        _images.ListAsync(Arg.Any<ImageListFilter>(), Arg.Any<ImageSort>(), Arg.Any<SortDirection>(),
                Arg.Any<ImageKeyset?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(rows);

    [Fact]
    public async Task ListAsync_Defaults_AreDateDescLimit100_AndAskForOneExtraRow()
    {
        var result = await CreateService().ListAsync(new ImageListRequest());

        result.IsSuccess.Should().BeTrue();
        await _images.Received(1).ListAsync(new ImageListFilter(null, null, null, false),
            ImageSort.Date, SortDirection.Desc, null, 101, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_NameSort_DefaultsToAscending()
    {
        await CreateService().ListAsync(new ImageListRequest(Sort: "name"));

        await _images.Received(1).ListAsync(Arg.Any<ImageListFilter>(),
            ImageSort.Name, SortDirection.Asc, null, 101, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("size", null, null, "sort")]
    [InlineData(null, "sideways", null, "order")]
    [InlineData(null, null, 0, "limit")]
    [InlineData(null, null, 201, "limit")]
    public async Task ListAsync_InvalidParameters_ReturnInvalidForThatField(string? sort, string? order, int? limit, string field)
    {
        var result = await CreateService().ListAsync(new ImageListRequest(Sort: sort, Order: order, Limit: limit));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain(field);
    }

    [Fact]
    public async Task ListAsync_MalformedCursor_ReturnsInvalidCursor()
    {
        var result = await CreateService().ListAsync(new ImageListRequest(Cursor: "%%%not-a-cursor"));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain("cursor");
    }

    [Fact]
    public async Task ListAsync_CursorFromAnotherSort_ReturnsInvalidCursor()
    {
        var nameCursor = CursorCodec.Encode(new ImageCursor("name", "asc", "abc", 5));

        var result = await CreateService().ListAsync(new ImageListRequest(Sort: "date", Cursor: nameCursor));

        result.Status.Should().Be(ResultStatus.Invalid);
        result.Errors!.Keys.Should().Contain("cursor");
    }

    [Fact]
    public async Task ListAsync_MoreRowsThanLimit_ReturnsNextCursor_ThatContinuesFromTheLastRow()
    {
        StubRows(new[] { Row(1), Row(2), Row(3) });
        var service = CreateService();

        var first = await service.ListAsync(new ImageListRequest(Limit: 2));

        first.Value!.Items.Select(i => i.Id).Should().Equal(1, 2);
        first.Value.NextCursor.Should().NotBeNull();

        await service.ListAsync(new ImageListRequest(Limit: 2, Cursor: first.Value.NextCursor));

        var expectedAfter = new ImageKeyset(Row(2).SortDate, null, 2);
        await _images.Received(1).ListAsync(Arg.Any<ImageListFilter>(), ImageSort.Date, SortDirection.Desc,
            expectedAfter, 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_NameCursor_UsesTheDatabaseSortName()
    {
        StubRows(new[] { Row(1, name: "Alpha"), Row(2, name: "Beta"), Row(3, name: "Gamma") });
        var service = CreateService();

        var first = await service.ListAsync(new ImageListRequest(Sort: "name", Limit: 2));
        await service.ListAsync(new ImageListRequest(Sort: "name", Limit: 2, Cursor: first.Value!.NextCursor));

        await _images.Received(1).ListAsync(Arg.Any<ImageListFilter>(), ImageSort.Name, SortDirection.Asc,
            new ImageKeyset(null, "beta", 2), 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_LastPage_HasNullCursor()
    {
        StubRows(new[] { Row(1) });

        var result = await CreateService().ListAsync(new ImageListRequest(Limit: 2));

        result.Value!.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_FolderNotVisible_ReturnsNotFound()
    {
        _folders.IsVisibleAsync(12, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().ListAsync(new ImageListRequest(FolderId: 12));

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task ListAsync_MapsVersionedUrls_AndNullUrlsWhenNotYetHashed()
    {
        StubRows(new[] { Row(1, hash: "ABC"), Row(2, hash: "") });

        var items = (await CreateService().ListAsync(new ImageListRequest())).Value!.Items;

        items[0].ThumbnailUrl.Should().Be("/api/images/1/thumbnail?v=ABC");
        items[0].PreviewUrl.Should().Be("/api/images/1/preview?v=ABC");
        items[1].ThumbnailUrl.Should().BeNull();
        items[1].PreviewUrl.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_InvalidIndexState_MapsToIsInvalidTrue()
    {
        StubRows(new[] { Row(1) with { IndexState = IndexState.Invalid } });

        var items = (await CreateService().ListAsync(new ImageListRequest())).Value!.Items;

        items[0].IsInvalid.Should().BeTrue();
    }

    [Fact]
    public async Task ListAsync_IndexedRow_MapsToIsInvalidFalse()
    {
        StubRows(new[] { Row(1) });

        var items = (await CreateService().ListAsync(new ImageListRequest())).Value!.Items;

        items[0].IsInvalid.Should().BeFalse();
    }

    [Fact]
    public async Task GetDetailAsync_NotVisible_ReturnsNotFound()
    {
        _images.GetVisibleDetailAsync(5, Arg.Any<CancellationToken>()).Returns((ImageDetailRow?)null);

        (await CreateService().GetDetailAsync(5)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetDetailAsync_MapsFolderPathRawMetadataAndAlbums()
    {
        _images.GetVisibleDetailAsync(1, Arg.Any<CancellationToken>()).Returns(new ImageDetailRow(
            Row(1, hash: "ABC"), 2048, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 6,
            "Canon", "R6", null, 32.6, -16.9, """{"Exif IFD0.Make":"Canon"}""", "nas", "Holidays/Madeira"));
        _images.GetAlbumsContainingAsync(1, 1, Arg.Any<CancellationToken>()).Returns(new[] { new AlbumRef(3, "Best of") });

        var detail = (await CreateService().GetDetailAsync(1)).Value!;

        detail.FolderPath.Should().Be("nas/Holidays/Madeira");
        detail.RawMetadata!.Value.GetProperty("Exif IFD0.Make").GetString().Should().Be("Canon");
        detail.Albums.Should().Equal(new AlbumRef(3, "Best of"));
        detail.ThumbnailUrl.Should().Be("/api/images/1/thumbnail?v=ABC");
        detail.IsInvalid.Should().BeFalse();
    }

    [Fact]
    public async Task GetDetailAsync_InvalidIndexState_MapsToIsInvalidTrue()
    {
        _images.GetVisibleDetailAsync(1, Arg.Any<CancellationToken>()).Returns(new ImageDetailRow(
            Row(1) with { IndexState = IndexState.Invalid }, 2048, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 6,
            "Canon", "R6", null, 32.6, -16.9, """{"Exif IFD0.Make":"Canon"}""", "nas", "Holidays/Madeira"));
        _images.GetAlbumsContainingAsync(1, 1, Arg.Any<CancellationToken>()).Returns(Array.Empty<AlbumRef>());

        var detail = (await CreateService().GetDetailAsync(1)).Value!;

        detail.IsInvalid.Should().BeTrue();
    }

    [Fact]
    public async Task GetDetailAsync_UnparseableRawMetadata_BecomesNull()
    {
        _images.GetVisibleDetailAsync(1, Arg.Any<CancellationToken>()).Returns(new ImageDetailRow(
            Row(1), 1, DateTime.UtcNow, null, null, null, null, null, null, "{not json", "nas", ""));
        _images.GetAlbumsContainingAsync(1, 1, Arg.Any<CancellationToken>()).Returns(Array.Empty<AlbumRef>());

        (await CreateService().GetDetailAsync(1)).Value!.RawMetadata.Should().BeNull();
    }

    [Fact]
    public async Task SetFavoriteAsync_NoVisibleImage_ReturnsNotFound()
    {
        _images.SetFavoriteAsync(9, true, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(false);

        (await CreateService().SetFavoriteAsync(9, true)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task SetFavoriteAsync_Success_ReturnsOk_AndStampsTheClock()
    {
        _images.SetFavoriteAsync(9, false, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().SetFavoriteAsync(9, false)).IsSuccess.Should().BeTrue();
        await _images.Received(1).SetFavoriteAsync(9, false, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_ItemsCarryFolderPath_RootNameAloneForTheTopFolder()
    {
        StubRows(new[]
        {
            Row(1) with { RootName = "nas", RelativePath = "" },
            Row(2) with { RootName = "nas", RelativePath = "Holidays/Madeira" }
        });

        var items = (await CreateService().ListAsync(new ImageListRequest())).Value!.Items;

        items.Select(i => i.FolderPath).Should().Equal("nas", "nas/Holidays/Madeira");
    }
}
