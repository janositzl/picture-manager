using System;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Images;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class ImageQueryRepositoryDaysTests
{
    private static DateTime Local(int month, int day, int hour) => new(2025, month, day, hour, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public async Task ListDaysAsync_GroupsByWallClockDay_FallsBackToFileModified_AndHonoursDirection()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        db.Context.Images.AddRange(
            TestData.Image(folder, "a1", dateTaken: Local(8, 14, 9)),
            TestData.Image(folder, "a2", dateTaken: Local(8, 14, 23)),
            TestData.Image(folder, "b1", dateTaken: Local(8, 15, 0)),
            // No EXIF date: sorted (and so grouped) by the file's modified time.
            TestData.Image(folder, "c1", fileModified: new DateTime(2025, 8, 20, 12, 0, 0, DateTimeKind.Utc)),
            TestData.Image(folder, "gone", dateTaken: Local(8, 14, 10), missingSinceUtc: TestData.Utc));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var filter = new ImageListFilter(folder.Id, null, null, false);

        var ascending = await repository.ListDaysAsync(1, filter, SortDirection.Asc);
        var descending = await repository.ListDaysAsync(1, filter, SortDirection.Desc);

        ascending.Should().Equal(
            new ImageDay(new DateOnly(2025, 8, 14), 2),
            new ImageDay(new DateOnly(2025, 8, 15), 1),
            new ImageDay(new DateOnly(2025, 8, 20), 1));
        descending.Should().Equal(ascending.Reverse());
    }
}
