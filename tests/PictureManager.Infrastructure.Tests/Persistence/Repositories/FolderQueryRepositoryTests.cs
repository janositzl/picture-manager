using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Folders;
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

    [Fact]
    public async Task GetVisibleRootFoldersAsync_ReturnsTopFoldersOfActiveRoots_OrderedByName()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var zeta = TestData.Folder(TestData.Root("Zeta"), "");
        var alpha = TestData.Folder(TestData.Root("alpha"), "");
        var offline = TestData.Folder(TestData.Root("offline", isActive: false), "");
        db.Context.Folders.AddRange(zeta, alpha, offline);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var nodes = await new FolderRepository(context).GetVisibleRootFoldersAsync();

        nodes.Select(n => n.Id).Should().Equal(alpha.Id, zeta.Id);
    }

    [Fact]
    public async Task GetVisibleChildrenAsync_ActiveOnly_WithHasChildrenAndDirectVisibleImageCount()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var madeira = TestData.Folder(root, "Madeira", top);
        var portugal = TestData.Folder(root, "portugal", top);
        var removed = TestData.Folder(root, "Removed", top, isActive: false);
        var nested = TestData.Folder(root, "Madeira/Day1", madeira);
        db.Context.Folders.AddRange(top, madeira, portugal, removed, nested);
        db.Context.Images.AddRange(
            TestData.Image(madeira, "a"),
            TestData.Image(madeira, "b"),
            TestData.Image(madeira, "gone", missingSinceUtc: TestData.Utc),
            TestData.Image(nested, "deeper"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var nodes = await new FolderRepository(context).GetVisibleChildrenAsync(top.Id);

        nodes.Should().Equal(
            new FolderNode(madeira.Id, "Madeira", HasChildren: true, IsMissing: false, IsExcluded: false),
            new FolderNode(portugal.Id, "portugal", HasChildren: false, IsMissing: false, IsExcluded: false));
    }

    [Fact]
    public async Task GetVisibleDetailAsync_BuildsBreadcrumbFromRootToSelf()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var holidays = TestData.Folder(root, "Holidays", top);
        var madeira = TestData.Folder(root, "Holidays/Madeira", holidays);
        db.Context.Folders.AddRange(top, holidays, madeira);
        db.Context.Images.Add(TestData.Image(madeira, "a"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var detail = await new FolderRepository(context).GetVisibleDetailAsync(madeira.Id);

        detail.Should().NotBeNull();
        detail!.RootName.Should().Be("nas");
        detail.RelativePath.Should().Be("Holidays/Madeira");
        detail.ImageCount.Should().Be(1);
        detail.Breadcrumb.Should().Equal(
            new BreadcrumbItem(top.Id, "nas"),
            new BreadcrumbItem(holidays.Id, "Holidays"),
            new BreadcrumbItem(madeira.Id, "Madeira"));
    }

    [Fact]
    public async Task RemoveFromCollectionAsync_TombstonesFolder_AndPurgesImagesSubfoldersAndAlbumEntries()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var target = TestData.Folder(root, "Target", top);
        var child = TestData.Folder(root, "Target/Child", target);
        var grandchild = TestData.Folder(root, "Target/Child/Grand", child);
        var sibling = TestData.Folder(root, "Sibling", top);
        var inTarget = TestData.Image(target, "t");
        var inGrandchild = TestData.Image(grandchild, "g");
        var inSibling = TestData.Image(sibling, "s");
        var album = TestData.Album("A");
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(album, inTarget, 0),
            TestData.AlbumImage(album, inGrandchild, 1),
            TestData.AlbumImage(album, inSibling, 2));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).RemoveFromCollectionAsync(target.Id);

        await using var read = db.CreateContext();
        var tombstone = await read.Folders.SingleAsync(f => f.Id == target.Id);
        tombstone.IsActive.Should().BeFalse();
        (await read.Folders.AnyAsync(f => f.Id == child.Id || f.Id == grandchild.Id)).Should().BeFalse();
        (await read.Images.Select(i => i.Id).ToListAsync()).Should().Equal(inSibling.Id);
        (await read.AlbumImages.Select(ai => ai.ImageId).ToListAsync()).Should().Equal(inSibling.Id);
        (await read.Folders.AnyAsync(f => f.Id == sibling.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveFromCollectionAsync_ReturnsHashesOfDeletedImages_ExceptThoseSharedWithRemainingImages()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var target = TestData.Folder(root, "Target", top);
        var child = TestData.Folder(root, "Target/Child", target);
        var sibling = TestData.Folder(root, "Sibling", top);
        db.Context.Images.AddRange(
            TestData.Image(target, "a", contentHash: "AAAA1111"),
            TestData.Image(target, "dup", contentHash: "BBBB2222"),
            TestData.Image(target, "dup2", contentHash: "BBBB2222"),
            TestData.Image(child, "c", contentHash: "CCCC3333"),
            TestData.Image(sibling, "kept", contentHash: "AAAA1111"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var orphaned = await new FolderRepository(context).RemoveFromCollectionAsync(target.Id);

        orphaned.Should().BeEquivalentTo(["BBBB2222", "CCCC3333"]);
    }

    [Fact]
    public async Task GetRemovedAsync_ListsTombstonesWithRootName()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var removed = TestData.Folder(root, "Old", top, isActive: false);
        db.Context.Folders.AddRange(top, removed);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var list = await new FolderRepository(context).GetRemovedAsync();

        list.Should().Equal(new RemovedFolder(removed.Id, "Old", "nas", "Old"));
    }

    [Fact]
    public async Task DeleteSubtreeAsync_DeletesFolderAndEverythingBeneath_WithoutTombstone()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var raw = TestData.Folder(root, "raw", top);
        var rawChild = TestData.Folder(root, "raw/2025", raw);
        db.Context.Images.AddRange(TestData.Image(raw, "a"), TestData.Image(rawChild, "b"), TestData.Image(top, "keep"));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).DeleteSubtreeAsync(raw.Id);

        await using var read = db.CreateContext();
        (await read.Folders.Select(f => f.Id).ToListAsync()).Should().Equal(top.Id);
        (await read.Images.Select(i => i.FileName).ToListAsync()).Should().Equal("keep");
    }

    [Fact]
    public async Task RenameRootFolderAsync_RenamesOnlyTheRootsTopFolder()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("old");
        var top = TestData.Folder(root, "");
        var child = TestData.Folder(root, "child", top);
        db.Context.Folders.AddRange(top, child);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            await new FolderRepository(context).RenameRootFolderAsync(root.Id, "Family Photos");

        await using var read = db.CreateContext();
        (await read.Folders.SingleAsync(f => f.Id == top.Id)).Name.Should().Be("Family Photos");
        (await read.Folders.SingleAsync(f => f.Id == child.Id)).Name.Should().Be("child");
    }
}
