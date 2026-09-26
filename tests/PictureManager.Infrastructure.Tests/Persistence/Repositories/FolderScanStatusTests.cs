using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FolderScanStatusTests
{
    private static readonly DateTime ScannedAt = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MarkSubtreeScannedAsync_StampsFolderAndDescendants_WithEachFoldersOwnImageCount_SkipsTombstonesAndSiblings()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top);
        var day1 = TestData.Folder(root, "Trip/Day1", trip);
        var removed = TestData.Folder(root, "Trip/Removed", trip, isActive: false);
        var sibling = TestData.Folder(root, "Other", top);
        db.Context.Folders.AddRange(top, trip, day1, removed, sibling);
        db.Context.Images.AddRange(
            TestData.Image(trip, "a"),
            TestData.Image(day1, "b"),
            TestData.Image(day1, "c", missingSinceUtc: ScannedAt));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).MarkSubtreeScannedAsync(trip.Id, ScannedAt);

        await using var verify = db.CreateContext();
        var folders = await verify.Folders.AsNoTracking().ToDictionaryAsync(f => f.Id);
        folders[trip.Id].ScanStatus.Should().Be(FolderScanStatus.Idle);
        folders[trip.Id].LastScannedAt.Should().Be(ScannedAt);
        folders[trip.Id].LastScanFileCount.Should().Be(1);
        folders[day1.Id].ScanStatus.Should().Be(FolderScanStatus.Idle);
        folders[day1.Id].LastScannedAt.Should().Be(ScannedAt);
        // "c" is missing, so day1's own present-image count is 1, not 2.
        folders[day1.Id].LastScanFileCount.Should().Be(1);
        folders[removed.Id].LastScannedAt.Should().BeNull();
        folders[sibling.Id].LastScannedAt.Should().BeNull();
        folders[top.Id].LastScannedAt.Should().BeNull();
    }

    [Fact]
    public async Task MarkSubtreeScannedAsync_SkipsAnExcludedChild_AndItsDescendants()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r-excluded");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top);
        var excluded = TestData.Folder(root, "Trip/Skip", trip, isExcluded: true);
        var nested = TestData.Folder(root, "Trip/Skip/Deeper", excluded);
        db.Context.Folders.AddRange(top, trip, excluded, nested);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).MarkSubtreeScannedAsync(trip.Id, ScannedAt);

        await using var verify = db.CreateContext();
        var folders = await verify.Folders.AsNoTracking().ToDictionaryAsync(f => f.Id);
        folders[trip.Id].LastScannedAt.Should().Be(ScannedAt);
        folders[excluded.Id].LastScannedAt.Should().BeNull();
        folders[nested.Id].LastScannedAt.Should().BeNull();
    }

    [Fact]
    public async Task SetScanStatusAsync_SetsOnlyThatFoldersOwnStatus()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top);
        db.Context.Folders.AddRange(top, trip);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).SetScanStatusAsync(trip.Id, FolderScanStatus.Scanning);

        await using var verify = db.CreateContext();
        (await verify.Folders.AsNoTracking().SingleAsync(f => f.Id == trip.Id)).ScanStatus
            .Should().Be(FolderScanStatus.Scanning);
        (await verify.Folders.AsNoTracking().SingleAsync(f => f.Id == top.Id)).ScanStatus
            .Should().Be(FolderScanStatus.Idle);
    }
}
