using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Albums;
using PictureManager.Application.Common;
using PictureManager.Application.Images;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Albums;

public class AlbumServiceTests
{
    private const int Owner = 1;
    private readonly IAlbumRepository _albums = Substitute.For<IAlbumRepository>();
    private readonly IImageQueryRepository _images = Substitute.For<IImageQueryRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Album _album = new() { Id = 7, Name = "Holidays", OwnerUserId = Owner };

    public AlbumServiceTests()
    {
        _currentUser.UserId.Returns(Owner);
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _albums.GetOwnedAsync(7, Owner, Arg.Any<CancellationToken>()).Returns(_album);
        _albums.GetOrderedImageIdsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<int>());
        _albums.AddAsync(Arg.Any<Album>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var album = call.Arg<Album>();
            album.Id = 42;
            return album;
        });
    }

    private AlbumService CreateService() => new(_albums, _images, _folders, _currentUser, _clock);

    private static ImageRow Row(int id, string hash = "H") =>
        new(id, 3, $"img{id}", ".jpg", null, null, null, false, hash, DateTime.UtcNow, $"img{id}", "nas", "");

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task CreateAsync_BlankName_ReturnsInvalid(string? name)
    {
        var result = await CreateService().CreateAsync(new AlbumCreate(name, null));

        result.Errors!.Keys.Should().Contain("name");
    }

    [Fact]
    public async Task CreateAsync_NameTooLong_ReturnsInvalid()
    {
        (await CreateService().CreateAsync(new AlbumCreate(new string('x', 201), null))).Errors!.Keys.Should().Contain("name");
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsConflict()
    {
        _albums.NameExistsAsync(Owner, "Holidays", null, Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().CreateAsync(new AlbumCreate("Holidays", null))).Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task CreateAsync_Valid_CreatesTrimmedAlbumOwnedByCurrentUser()
    {
        var result = await CreateService().CreateAsync(new AlbumCreate("  Summer  ", "  sun  "));

        result.Value.Should().Be(new AlbumDetail(42, "Summer", "sun", 0, _clock.UtcNow, _clock.UtcNow));
        await _albums.Received(1).AddAsync(Arg.Is<Album>(a => a.Name == "Summer" && a.OwnerUserId == Owner), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_NotOwned_ReturnsNotFound()
    {
        _albums.GetOwnedAsync(8, Owner, Arg.Any<CancellationToken>()).Returns((Album?)null);

        (await CreateService().GetAsync(8)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateAsync_Rename_ChecksConflictExcludingItself()
    {
        _albums.NameExistsAsync(Owner, "Trips", 7, Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().UpdateAsync(7, new AlbumUpdate("Trips", false, null))).Status.Should().Be(ResultStatus.Conflict);
    }

    [Fact]
    public async Task UpdateAsync_DescriptionExplicitlyNull_ClearsIt_AndKeepsName()
    {
        _album.Description = "old";

        var result = await CreateService().UpdateAsync(7, new AlbumUpdate(null, true, null));

        result.Value!.Description.Should().BeNull();
        result.Value.Name.Should().Be("Holidays");
        await _albums.Received(1).UpdateAsync(Arg.Is<Album>(a => a.Description == null && a.UpdatedAt == _clock.UtcNow), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_CoverUrlFromCoverRow_NullWhenNoHashedImage()
    {
        // _clock.UtcNow is read into a local first: evaluating a substitute's member while
        // building the argument to another substitute's .Returns(...) call corrupts NSubstitute's
        // "last configured call" tracking and misdirects the stub.
        var updatedAt = _clock.UtcNow;
        _albums.GetSummariesAsync(Owner, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AlbumSummaryRow(1, "A", null, 3, 11, "HASH", updatedAt),
            new AlbumSummaryRow(2, "B", null, 0, null, null, updatedAt)
        });

        var all = await CreateService().GetAllAsync();

        all[0].CoverThumbnailUrl.Should().Be("/api/images/11/thumbnail?v=HASH");
        all[1].CoverThumbnailUrl.Should().BeNull();
    }

    [Fact]
    public async Task ListImagesAsync_MissingImage_HasNullUrls_AndPagingProducesCursor()
    {
        _albums.ListImagesAsync(7, null, null, 3, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AlbumImageRow(Row(1), 0, false),
            new AlbumImageRow(Row(2), 1, true),
            new AlbumImageRow(Row(3), 2, false)
        });

        var page = (await CreateService().ListImagesAsync(7, null, 2)).Value!;

        page.Items.Select(i => i.Id).Should().Equal(1, 2);
        page.Items[1].IsMissing.Should().BeTrue();
        page.Items[1].ThumbnailUrl.Should().BeNull();
        page.Items[0].ThumbnailUrl.Should().Be("/api/images/1/thumbnail?v=H");
        CursorCodec.TryDecode<AlbumImageCursor>(page.NextCursor, out var cursor).Should().BeTrue();
        cursor.Should().Be(new AlbumImageCursor(1, 2));
    }

    [Fact]
    public async Task ListImagesAsync_ItemsCarryFolderPath_RootNameAloneForTheTopFolder()
    {
        var nested = new ImageRow(4, 3, "img4", ".jpg", null, null, null, false, "H", DateTime.UtcNow, "img4", "nas", "Holidays/Madeira");
        _albums.ListImagesAsync(7, null, null, 101, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AlbumImageRow(Row(1), 0, false),
            new AlbumImageRow(nested, 1, false)
        });

        var page = (await CreateService().ListImagesAsync(7, null, null)).Value!;

        page.Items.Select(i => i.FolderPath).Should().Equal("nas", "nas/Holidays/Madeira");
    }

    [Fact]
    public async Task ListImagesAsync_BadCursor_ReturnsInvalid()
    {
        (await CreateService().ListImagesAsync(7, "garbage!!", null)).Errors!.Keys.Should().Contain("cursor");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task AddImagesAsync_BothOrNeitherSource_ReturnsInvalid(bool withIds, bool withFolder)
    {
        var input = new AlbumAddImages(withIds ? new[] { 1 } : null, withFolder ? 3 : null);

        (await CreateService().AddImagesAsync(7, input)).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task AddImagesAsync_UnknownIds_ReturnsInvalid_AndAddsNothing()
    {
        _images.GetVisibleIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new[] { 1 });

        var result = await CreateService().AddImagesAsync(7, new AlbumAddImages(new[] { 1, 99 }, null));

        result.Errors!["imageIds"].Single().Should().Contain("99");
        await _albums.DidNotReceive().AppendImagesAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_SkipsImagesAlreadyInAlbum_AppendsRestInGivenOrder()
    {
        _images.GetVisibleIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new[] { 3, 1, 2 });
        _albums.GetOrderedImageIdsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<int> { 2 });

        var result = await CreateService().AddImagesAsync(7, new AlbumAddImages(new[] { 3, 2, 1 }, null));

        result.Value.Should().Be(new AlbumAddResult(2, 1));
        await _albums.Received(1).AppendImagesAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 3, 1 })),
            _clock.UtcNow, Arg.Any<CancellationToken>());
        await _albums.Received(1).TouchAsync(7, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_DuplicateIdsInRequest_AddsEachImageOnce()
    {
        _images.GetVisibleIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(new[] { 5 });

        var result = await CreateService().AddImagesAsync(7, new AlbumAddImages(new[] { 5, 5 }, null));

        result.Value.Should().Be(new AlbumAddResult(1, 0));
        await _albums.Received(1).AppendImagesAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 5 })),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_FolderId_AddsTheFoldersVisibleImagesInRepositoryOrder()
    {
        _folders.IsVisibleAsync(3, Arg.Any<CancellationToken>()).Returns(true);
        _images.GetVisibleIdsInFolderAsync(3, Arg.Any<CancellationToken>()).Returns(new[] { 20, 10 });

        var result = await CreateService().AddImagesAsync(7, new AlbumAddImages(null, 3));

        result.Value.Should().Be(new AlbumAddResult(2, 0));
        await _albums.Received(1).AppendImagesAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 20, 10 })),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_UnknownFolder_ReturnsNotFound()
    {
        _folders.IsVisibleAsync(3, Arg.Any<CancellationToken>()).Returns(false);

        (await CreateService().AddImagesAsync(7, new AlbumAddImages(null, 3))).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task RemoveImagesAsync_NoIds_ReturnsInvalid()
    {
        (await CreateService().RemoveImagesAsync(7, Array.Empty<int>())).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task RemoveImagesAsync_RemovesAndTouches()
    {
        (await CreateService().RemoveImagesAsync(7, new[] { 4, 4, 5 })).IsSuccess.Should().BeTrue();

        await _albums.Received(1).RemoveImagesAsync(7, Arg.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 4, 5 })),
            Arg.Any<CancellationToken>());
        await _albums.Received(1).TouchAsync(7, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(104, 101, new[] { 101, 104, 102, 103 })]
    [InlineData(101, 103, new[] { 102, 103, 101, 104 })]
    [InlineData(103, null, new[] { 103, 101, 102, 104 })]
    public async Task MoveImageAsync_ReordersFullAlbum(int imageId, int? afterImageId, int[] expected)
    {
        _albums.GetOrderedImageIdsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<int> { 101, 102, 103, 104 });

        (await CreateService().MoveImageAsync(7, imageId, afterImageId)).IsSuccess.Should().BeTrue();

        await _albums.Received(1).ReorderAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(expected)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(101, 101, "afterImageId")]
    [InlineData(999, null, "imageId")]
    [InlineData(101, 999, "afterImageId")]
    public async Task MoveImageAsync_InvalidMove_ReturnsInvalid_WithoutReordering(int imageId, int? afterImageId, string field)
    {
        _albums.GetOrderedImageIdsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<int> { 101, 102 });

        var result = await CreateService().MoveImageAsync(7, imageId, afterImageId);

        result.Errors!.Keys.Should().Contain(field);
        await _albums.DidNotReceive().ReorderAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("dateAsc", AlbumSortKey.DateAscending)]
    [InlineData("dateDesc", AlbumSortKey.DateDescending)]
    [InlineData("name", AlbumSortKey.Name)]
    public async Task SortAsync_StoresTheSortedOrder(string by, AlbumSortKey key)
    {
        _albums.GetImageIdsSortedAsync(7, key, Arg.Any<CancellationToken>()).Returns(new List<int> { 103, 101, 102 });

        (await CreateService().SortAsync(7, by)).IsSuccess.Should().BeTrue();

        await _albums.Received(1).ReorderAsync(7, Arg.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { 103, 101, 102 })), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("random")]
    public async Task SortAsync_UnknownKey_ReturnsInvalid_WithoutReordering(string? by)
    {
        var result = await CreateService().SortAsync(7, by);

        result.Errors!.Keys.Should().Contain("by");
        await _albums.DidNotReceive().ReorderAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportAsync_FormatsLines_AndUsesAlbumNameForFile()
    {
        _albums.GetExportRowsAsync(7, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AlbumExportRow("nas-photos", "family_photos", "", "IMG_0001", ".jpg")
        });

        var export = (await CreateService().ExportAsync(7, "/mnt")).Value!;

        export.FileName.Should().Be("Holidays.txt");
        export.Content.Should().Be("/mnt/family_photos/IMG_0001.jpg\n");
    }

    [Fact]
    public async Task DeleteAsync_NotOwned_ReturnsNotFound()
    {
        _albums.GetOwnedAsync(8, Owner, Arg.Any<CancellationToken>()).Returns((Album?)null);

        (await CreateService().DeleteAsync(8)).Status.Should().Be(ResultStatus.NotFound);
    }
}
