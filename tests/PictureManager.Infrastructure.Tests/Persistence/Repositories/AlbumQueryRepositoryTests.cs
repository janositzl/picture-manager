using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Albums;
using PictureManager.Application.Images;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AlbumQueryRepositoryTests
{
    [Fact]
    public async Task GetSummariesAsync_OwnerScoped_OrderedByName_WithCountAndCoverFromFirstHashedImage()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var other = new AppUser { DisplayName = "Other", Role = UserRole.User };
        db.Context.AppUsers.Add(other);
        await db.Context.SaveChangesAsync();

        var folder = TestData.Folder(TestData.Root("r"), "");
        var unhashed = TestData.Image(folder, "pending", contentHash: "");
        var cover = TestData.Image(folder, "cover", contentHash: "COVERHASH");
        var zoo = TestData.Album("zoo");
        var beach = TestData.Album("Beach");
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(zoo, unhashed, 0), TestData.AlbumImage(zoo, cover, 1));
        db.Context.Albums.AddRange(beach, TestData.Album("foreign", other.Id));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).GetSummariesAsync(AppUser.InitialAdminId);

        rows.Select(r => r.Name).Should().Equal("Beach", "zoo");
        rows[0].ImageCount.Should().Be(0);
        rows[0].CoverImageId.Should().BeNull();
        rows[1].ImageCount.Should().Be(2);
        rows[1].CoverImageId.Should().Be(cover.Id);
        rows[1].CoverContentHash.Should().Be("COVERHASH");
    }

    [Fact]
    public async Task GetSummariesAsync_ChosenCoverWins_UnlessItLeftTheAlbum()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var first = TestData.Image(folder, "first", contentHash: "FIRST");
        var chosen = TestData.Image(folder, "chosen", contentHash: "CHOSEN");
        var removed = TestData.Image(folder, "removed", contentHash: "REMOVED");
        var picked = TestData.Album("picked");
        var stale = TestData.Album("stale");
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(picked, first, 0), TestData.AlbumImage(picked, chosen, 1),
            TestData.AlbumImage(stale, first, 0));
        db.Context.Images.Add(removed);
        await db.Context.SaveChangesAsync();
        picked.CoverImageId = chosen.Id;
        stale.CoverImageId = removed.Id;
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).GetSummariesAsync(AppUser.InitialAdminId);

        rows.Single(r => r.Name == "picked").CoverImageId.Should().Be(chosen.Id);
        rows.Single(r => r.Name == "picked").CoverContentHash.Should().Be("CHOSEN");
        rows.Single(r => r.Name == "stale").CoverImageId.Should().Be(first.Id);
    }

    [Theory]
    [InlineData(AlbumSortKey.DateAscending, new[] { "c", "a", "b" })]
    [InlineData(AlbumSortKey.DateDescending, new[] { "b", "a", "c" })]
    [InlineData(AlbumSortKey.Name, new[] { "a", "b", "c" })]
    public async Task GetImageIdsSortedAsync_OrdersByKey_IgnoringStoredSortOrder(AlbumSortKey key, string[] expectedNames)
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var a = TestData.Image(folder, "A", dateTaken: new DateTime(2024, 5, 1));
        var b = TestData.Image(folder, "b", dateTaken: new DateTime(2025, 1, 1));
        var c = TestData.Image(folder, "c", dateTaken: new DateTime(2020, 1, 1));
        var album = TestData.Album("mixed");
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(album, a, 2), TestData.AlbumImage(album, b, 0), TestData.AlbumImage(album, c, 1));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var ids = await new AlbumRepository(context).GetImageIdsSortedAsync(album.Id, key);

        var names = new[] { a, b, c }.ToDictionary(i => i.Id, i => i.FileName.ToLowerInvariant());
        ids.Select(id => names[id]).Should().Equal(expectedNames);
    }

    [Fact]
    public async Task GetOwnedAsync_OtherOwnersAlbum_ReturnsNull()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var other = new AppUser { DisplayName = "Other", Role = UserRole.User };
        db.Context.AppUsers.Add(other);
        await db.Context.SaveChangesAsync();
        var mine = TestData.Album("mine");
        var theirs = TestData.Album("theirs", other.Id);
        db.Context.Albums.AddRange(mine, theirs);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);

        (await repository.GetOwnedAsync(mine.Id, AppUser.InitialAdminId)).Should().NotBeNull();
        (await repository.GetOwnedAsync(theirs.Id, AppUser.InitialAdminId)).Should().BeNull();
    }

    [Fact]
    public async Task NameExistsAsync_IsCaseInsensitive_AndCanExcludeAnAlbum()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var album = TestData.Album("Holidays");
        db.Context.Albums.Add(album);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);

        (await repository.NameExistsAsync(AppUser.InitialAdminId, "holidays", null)).Should().BeTrue();
        (await repository.NameExistsAsync(AppUser.InitialAdminId, "HOLIDAYS", album.Id)).Should().BeFalse();
        (await repository.NameExistsAsync(AppUser.InitialAdminId, "Other", null)).Should().BeFalse();
    }

    [Fact]
    public async Task ListImagesAsync_OrdersBySortOrder_ContinuesAfterKeyset_AndFlagsMissing()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var folder = TestData.Folder(root, "");
        var a = TestData.Image(folder, "a");
        var b = TestData.Image(folder, "b", missingSinceUtc: TestData.Utc);
        var c = TestData.Image(folder, "c");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(album, c, 0), TestData.AlbumImage(album, a, 1), TestData.AlbumImage(album, b, 2));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);
        var first = await repository.ListImagesAsync(album.Id, null, null, 2);
        var second = await repository.ListImagesAsync(album.Id, first[^1].SortOrder, first[^1].Image.Id, 2);

        first.Select(r => r.Image.Id).Should().Equal(c.Id, a.Id);
        first.Should().OnlyContain(r => !r.IsMissing);
        second.Should().ContainSingle().Which.Should().Match<AlbumImageRow>(r => r.Image.Id == b.Id && r.IsMissing);
    }

    [Fact]
    public async Task ListImagesAsync_RowsCarryRootNameAndRelativePath()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("nas"), "Holidays/Madeira");
        var image = TestData.Image(folder, "a");
        var album = TestData.Album("A");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, image, 0));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).ListImagesAsync(album.Id, null, null, 10);

        rows.Should().ContainSingle().Which.Image.Should().Match<ImageRow>(r => r.RootName == "nas" && r.RelativePath == "Holidays/Madeira");
    }

    [Fact]
    public async Task ListImagesAsync_ImageOnInactiveRoot_IsFlaggedMissing()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("offline", isActive: false), "");
        var image = TestData.Image(folder, "a");
        var album = TestData.Album("A");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, image, 0));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).ListImagesAsync(album.Id, null, null, 10);

        rows.Should().ContainSingle().Which.IsMissing.Should().BeTrue();
    }

    [Fact]
    public async Task AppendImagesAsync_AppendsAfterCurrentMax_InGivenOrder()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var existing = TestData.Image(folder, "existing");
        var x = TestData.Image(folder, "x");
        var y = TestData.Image(folder, "y");
        var album = TestData.Album("A");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, existing, 5));
        db.Context.Images.AddRange(x, y);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new AlbumRepository(context).AppendImagesAsync(album.Id, new[] { y.Id, x.Id }, TestData.Utc);

        await using var read = db.CreateContext();
        (await new AlbumRepository(read).GetOrderedImageIdsAsync(album.Id)).Should().Equal(existing.Id, y.Id, x.Id);
        (await read.AlbumImages.Where(ai => ai.AlbumId == album.Id).OrderBy(ai => ai.SortOrder).Select(ai => ai.SortOrder).ToListAsync())
            .Should().Equal(5, 6, 7);
    }

    [Fact]
    public async Task RemoveImagesAsync_RemovesOnlyTheGivenIds()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var a = TestData.Image(folder, "a");
        var b = TestData.Image(folder, "b");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(album, a, 0), TestData.AlbumImage(album, b, 1));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new AlbumRepository(context).RemoveImagesAsync(album.Id, new[] { a.Id, 999_999 });

        await using var read = db.CreateContext();
        (await new AlbumRepository(read).GetOrderedImageIdsAsync(album.Id)).Should().Equal(b.Id);
        (await read.Images.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ReorderAsync_RenumbersDenselyInTheGivenOrder()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var a = TestData.Image(folder, "a");
        var b = TestData.Image(folder, "b");
        var c = TestData.Image(folder, "c");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(album, a, 3), TestData.AlbumImage(album, b, 10), TestData.AlbumImage(album, c, 42));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new AlbumRepository(context).ReorderAsync(album.Id, new[] { c.Id, a.Id, b.Id });

        await using var read = db.CreateContext();
        var rows = await read.AlbumImages.Where(ai => ai.AlbumId == album.Id).OrderBy(ai => ai.SortOrder)
            .Select(ai => new { ai.ImageId, ai.SortOrder }).ToListAsync();
        rows.Select(r => r.ImageId).Should().Equal(c.Id, a.Id, b.Id);
        rows.Select(r => r.SortOrder).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task GetExportRowsAsync_InAlbumOrder_WithRootAliasAndFolderPath()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var photos = TestData.Root("nas-photos", alias: "family_photos");
        var work = TestData.Root("work-nas");
        var photosTop = TestData.Folder(photos, "");
        var madeira = TestData.Folder(photos, "Holidays/Madeira", photosTop);
        var workFolder = TestData.Folder(work, "2025/Q3");
        var first = TestData.Image(madeira, "IMG_4471", ".jpg");
        var second = TestData.Image(photosTop, "IMG_0001", ".jpg");
        var third = TestData.Image(workFolder, "whiteboard", ".png");
        var album = TestData.Album("Mixed");
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(album, third, 2), TestData.AlbumImage(album, first, 0), TestData.AlbumImage(album, second, 1));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).GetExportRowsAsync(album.Id);

        rows.Should().Equal(
            new AlbumExportRow("nas-photos", "family_photos", "Holidays/Madeira", "IMG_4471", ".jpg"),
            new AlbumExportRow("nas-photos", "family_photos", "", "IMG_0001", ".jpg"),
            new AlbumExportRow("work-nas", null, "2025/Q3", "whiteboard", ".png"));
    }

    [Fact]
    public async Task DeleteAsync_CascadesAlbumEntries_ButKeepsImages()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var image = TestData.Image(folder, "a");
        var album = TestData.Album("A");
        db.Context.AlbumImages.Add(TestData.AlbumImage(album, image, 0));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AlbumRepository(context);
            await repository.DeleteAsync((await repository.GetOwnedAsync(album.Id, AppUser.InitialAdminId))!);
        }

        await using var read = db.CreateContext();
        (await read.Albums.AnyAsync()).Should().BeFalse();
        (await read.AlbumImages.AnyAsync()).Should().BeFalse();
        (await read.Images.AnyAsync(i => i.Id == image.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task TouchAsync_SetsUpdatedAt_AndCountImagesAsyncCountsEntries()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(album, TestData.Image(folder, "a"), 0),
            TestData.AlbumImage(album, TestData.Image(folder, "b", missingSinceUtc: TestData.Utc), 1));
        await db.Context.SaveChangesAsync();
        var touchedAt = new DateTime(2026, 5, 5, 5, 5, 5, DateTimeKind.Utc);

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);
        await repository.TouchAsync(album.Id, touchedAt);

        (await repository.CountImagesAsync(album.Id)).Should().Be(2);
        await using var read = db.CreateContext();
        (await read.Albums.SingleAsync(a => a.Id == album.Id)).UpdatedAt.Should().Be(touchedAt);
    }
}
