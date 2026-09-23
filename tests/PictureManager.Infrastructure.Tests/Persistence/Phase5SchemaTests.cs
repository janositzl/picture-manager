using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

public class Phase5SchemaTests
{
    [Fact]
    public async Task SortDate_UsesDateTakenAsUtcWallClock_WhenPresent()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var folder = TestData.Folder(root, "");
        var image = TestData.Image(folder, "a", dateTaken: new DateTime(2025, 8, 14, 18, 32, 5),
            fileModified: new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));
        db.Context.Images.Add(image);
        await db.Context.SaveChangesAsync();

        await using var read = db.CreateContext();
        var stored = await read.Images.SingleAsync(i => i.Id == image.Id);
        stored.SortDate.Should().Be(new DateTime(2025, 8, 14, 18, 32, 5, DateTimeKind.Utc));
    }

    [Fact]
    public async Task SortDate_FallsBackToFileModified_WhenNoDateTaken()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var modified = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var image = TestData.Image(folder, "b", fileModified: modified);
        db.Context.Images.Add(image);
        await db.Context.SaveChangesAsync();

        await using var read = db.CreateContext();
        (await read.Images.SingleAsync(i => i.Id == image.Id)).SortDate.Should().Be(modified);
    }

    [Fact]
    public async Task ImageRootAlias_RoundTrips()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.ImageRoots.Add(TestData.Root("nas-photos", alias: "family_photos"));
        await db.Context.SaveChangesAsync();

        await using var read = db.CreateContext();
        (await read.ImageRoots.SingleAsync(r => r.Name == "nas-photos")).Alias.Should().Be("family_photos");
    }

    [Fact]
    public async Task ImageRootName_IsUniqueCaseInsensitively()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.ImageRoots.Add(TestData.Root("Photos"));
        await db.Context.SaveChangesAsync();

        await using var second = db.CreateContext();
        second.ImageRoots.Add(TestData.Root("photos"));
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ImageRootExportSegment_AliasOrName_IsUniqueAcrossRoots()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.ImageRoots.Add(TestData.Root("nas-photos", alias: "Family"));
        await db.Context.SaveChangesAsync();

        await using var second = db.CreateContext();
        second.ImageRoots.Add(TestData.Root("family"));
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AlbumName_IsUniquePerOwnerCaseInsensitively()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.Albums.Add(TestData.Album("Holidays"));
        await db.Context.SaveChangesAsync();

        await using var second = db.CreateContext();
        second.Albums.Add(TestData.Album("HOLIDAYS"));
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AlbumName_SameNameForDifferentOwners_IsAllowed()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var otherUser = new AppUser { DisplayName = "Other", Role = UserRole.User };
        db.Context.AppUsers.Add(otherUser);
        await db.Context.SaveChangesAsync();

        db.Context.Albums.Add(TestData.Album("Holidays"));
        db.Context.Albums.Add(TestData.Album("Holidays", otherUser.Id));
        var act = () => db.Context.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }
}
