using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Duplicates;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class DuplicateQueryRepositoryTests
{
    [Fact]
    public async Task GetDuplicateGroupsAsync_CountsOnlyVisibleHashedImages_OrderedByCountDescThenHash()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        db.Context.Images.AddRange(
            TestData.Image(folder, "a1", contentHash: "AAA"),
            TestData.Image(folder, "a2", contentHash: "AAA"),
            TestData.Image(folder, "a3", contentHash: "AAA"),
            TestData.Image(folder, "b1", contentHash: "BBB"),
            TestData.Image(folder, "b2", contentHash: "BBB"),
            TestData.Image(folder, "c1", contentHash: "CCC"),
            TestData.Image(folder, "d1", contentHash: "DDD"),
            TestData.Image(folder, "d2", contentHash: "DDD", missingSinceUtc: TestData.Utc),
            TestData.Image(folder, "p1", contentHash: ""),
            TestData.Image(folder, "p2", contentHash: ""));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var groups = await new ImageQueryRepository(context).GetDuplicateGroupsAsync(null, 10);

        groups.Should().Equal(new DuplicateGroupKey("AAA", 3), new DuplicateGroupKey("BBB", 2));
    }

    [Fact]
    public async Task GetDuplicateGroupsAsync_KeysetContinuesAfterTheLastGroup()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        foreach (var hash in new[] { "CCC", "AAA", "BBB" })
            db.Context.Images.AddRange(TestData.Image(folder, hash + "1", contentHash: hash), TestData.Image(folder, hash + "2", contentHash: hash));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var first = await repository.GetDuplicateGroupsAsync(null, 2);
        var second = await repository.GetDuplicateGroupsAsync(first[^1], 2);

        first.Select(g => g.ContentHash).Should().Equal("AAA", "BBB");
        second.Select(g => g.ContentHash).Should().Equal("CCC");
    }

    [Fact]
    public async Task GetDuplicateMembersAsync_ReturnsVisibleMembersWithLocation()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var trip = TestData.Folder(root, "Trip", top);
        var original = TestData.Image(trip, "IMG_1", contentHash: "AAA");
        var copy = TestData.Image(top, "IMG_1 copy", contentHash: "AAA");
        db.Context.Images.AddRange(original, copy, TestData.Image(top, "gone", contentHash: "AAA", missingSinceUtc: TestData.Utc));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var members = await new ImageQueryRepository(context).GetDuplicateMembersAsync(new[] { "AAA" });

        members.Select(m => (m.Image.Id, m.RootName, m.RelativePath)).Should().BeEquivalentTo(new[]
        {
            (original.Id, "nas", "Trip"),
            (copy.Id, "nas", "")
        });
    }
}
