using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FolderQueryRepositoryTests
{
    [Fact]
    public async Task IsVisibleAsync_TrueOnlyForActiveFolderUnderActiveRoot()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var active = TestData.Folder(root, "");
        var removed = TestData.Folder(root, "removed", active, isActive: false);
        var offline = TestData.Folder(TestData.Root("offline", isActive: false), "");
        db.Context.Folders.AddRange(active, removed, offline);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new FolderRepository(context);

        (await repository.IsVisibleAsync(active.Id)).Should().BeTrue();
        (await repository.IsVisibleAsync(removed.Id)).Should().BeFalse();
        (await repository.IsVisibleAsync(offline.Id)).Should().BeFalse();
        (await repository.IsVisibleAsync(999_999)).Should().BeFalse();
    }
}
