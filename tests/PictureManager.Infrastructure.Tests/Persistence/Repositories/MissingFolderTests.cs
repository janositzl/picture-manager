using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Folders;
using PictureManager.Application.Images;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class MissingFolderTests
{
    private static readonly DateTime Earlier = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MarkSubtreeMissingAsync_MarksFolderAndDescendants_KeepsEarlierDates_SkipsTombstonesAndSiblings()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top);
        var day1 = TestData.Folder(root, "Trip/Day1", trip);
        var day1Morning = TestData.Folder(root, "Trip/Day1/Morning", day1);
        var day2 = TestData.Folder(root, "Trip/Day2", trip, missingSinceUtc: Earlier);
        var removed = TestData.Folder(root, "Trip/Removed", trip, isActive: false);
        var sibling = TestData.Folder(root, "Other", top);
        db.Context.Folders.AddRange(top, trip, day1, day1Morning, day2, removed, sibling);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).MarkSubtreeMissingAsync(trip.Id, Now);

        await using var verify = db.CreateContext();
        var missing = await verify.Folders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.MissingSinceUtc);
        missing[trip.Id].Should().Be(Now);
        missing[day1.Id].Should().Be(Now);
        missing[day1Morning.Id].Should().Be(Now);
        missing[day2.Id].Should().Be(Earlier);
        missing[removed.Id].Should().BeNull();
        missing[sibling.Id].Should().BeNull();
        missing[top.Id].Should().BeNull();
    }

    [Fact]
    public async Task ImagesInAMissingFolder_AreHiddenFromSearchFavoritesDuplicatesDetailAndFavoriteToggle()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top, missingSinceUtc: Now);
        var tripItaly = TestData.Folder(root, "Trip-Italy", top);
        var hidden = TestData.Image(trip, "IMG_0001", contentHash: "AAAA", isFavorite: true);
        var shown = TestData.Image(tripItaly, "IMG_0001", contentHash: "AAAA", isFavorite: true);
        db.Context.Images.AddRange(hidden, shown);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        var byName = await repository.ListAsync(new ImageListFilter(null, null, "IMG_0001", false), ImageSort.Name, SortDirection.Asc, null, 10);
        byName.Select(r => r.Id).Should().Equal(shown.Id);

        var favorites = await repository.ListAsync(new ImageListFilter(null, null, null, true), ImageSort.Date, SortDirection.Desc, null, 10);
        favorites.Select(r => r.Id).Should().Equal(shown.Id);

        // One visible member left, so no duplicate group: a renamed folder no longer doubles every photo.
        (await repository.GetDuplicateGroupsAsync(null, 10)).Should().BeEmpty();
        (await repository.GetVisibleDetailAsync(hidden.Id)).Should().BeNull();
        (await repository.SetFavoriteAsync(hidden.Id, false, Now)).Should().BeFalse();
    }

    [Fact]
    public async Task FolderNodesAndDetail_ReportIsMissing_WithImageCountOfImagesThatWouldComeBack()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var home = TestData.Folder(root, "Home", top);
        var trip = TestData.Folder(root, "Trip", top, missingSinceUtc: Now);
        db.Context.Images.AddRange(
            TestData.Image(home, "a"),
            TestData.Image(trip, "b"),
            TestData.Image(trip, "c", missingSinceUtc: Earlier));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new FolderRepository(context);

        var nodes = await repository.GetVisibleChildrenAsync(top.Id);
        nodes.Should().Equal(
            new FolderNode(home.Id, "Home", HasChildren: false, ImageCount: 1, IsMissing: false),
            new FolderNode(trip.Id, "Trip", HasChildren: false, ImageCount: 1, IsMissing: true));

        var detail = await repository.GetVisibleDetailAsync(trip.Id);
        detail!.IsMissing.Should().BeTrue();
        detail.ImageCount.Should().Be(1);
    }

    [Fact]
    public async Task AlbumImages_InAMissingFolder_StayInTheAlbum_FlaggedMissing()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top, missingSinceUtc: Now);
        var image = TestData.Image(trip, "IMG_0001");
        var album = TestData.Album("Best");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, image, 1));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).ListImagesAsync(album.Id, null, null, 10);

        rows.Should().ContainSingle().Which.IsMissing.Should().BeTrue();
    }
}
