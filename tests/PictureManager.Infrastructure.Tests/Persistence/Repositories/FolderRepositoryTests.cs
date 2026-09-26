using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FolderRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AddAsync_PersistsFolder_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        context.ImageRoots.Add(root);
        await context.SaveChangesAsync();

        var repository = new FolderRepository(context);
        var folder = new Folder
        {
            Name = "Vacation",
            RelativePath = "Vacation",
            RootId = root.Id,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };

        var added = await repository.AddAsync(folder);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Vacation");
    }

    [Fact]
    public async Task GetChildrenAsync_ReturnsOnlyDirectChildrenOfGivenParent()
    {
        await using var context = CreateContext();
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        var parent = new Folder
        {
            Name = "Parent",
            RelativePath = "Parent",
            Root = root,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };
        context.AddRange(root, parent);
        await context.SaveChangesAsync();

        var child1 = new Folder { Name = "Child1", RelativePath = "Parent/Child1", RootId = root.Id, ParentId = parent.Id, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        var child2 = new Folder { Name = "Child2", RelativePath = "Parent/Child2", RootId = root.Id, ParentId = parent.Id, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        var unrelated = new Folder { Name = "Other", RelativePath = "Other", RootId = root.Id, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        context.Folders.AddRange(child1, child2, unrelated);
        await context.SaveChangesAsync();

        var repository = new FolderRepository(context);
        var children = await repository.GetChildrenAsync(parent.Id);

        children.Should().HaveCount(2);
        children.Select(f => f.Name).Should().BeEquivalentTo("Child1", "Child2");
    }

    [Fact]
    public async Task GetByRootAndRelativePathAsync_IsCaseInsensitive()
    {
        await using var context = CreateContext();
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        var folder = new Folder { Name = "Vacation", RelativePath = "Vacation", Root = root, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        context.AddRange(root, folder);
        await context.SaveChangesAsync();

        var repository = new FolderRepository(context);
        var found = await repository.GetByRootAndRelativePathAsync(root.Id, "vacation");

        found.Should().NotBeNull();
        found!.Id.Should().Be(folder.Id);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChangesToExistingFolder()
    {
        await using var context = CreateContext();
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        var folder = new Folder { Name = "Vacation", RelativePath = "Vacation", Root = root, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        context.AddRange(root, folder);
        await context.SaveChangesAsync();

        var repository = new FolderRepository(context);
        folder.ModifiedUtc = DateTime.UtcNow.AddMinutes(5);
        await repository.UpdateAsync(folder);

        var fetched = await repository.GetByIdAsync(folder.Id);
        fetched!.ModifiedUtc.Should().Be(folder.ModifiedUtc);
    }

    [Fact]
    public async Task DiscoveryTimestamps_RoundTripThroughPostgres()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("discovery-columns");
        var top = TestData.Folder(root, "");
        top.ChildrenDiscoveredAt = TestData.Utc;
        top.LastWriteTimeUtc = TestData.Utc.AddDays(-1);
        db.Context.Folders.Add(top);
        await db.Context.SaveChangesAsync();

        await using var verify = db.CreateContext();
        var fetched = await verify.Folders.AsNoTracking().SingleAsync(f => f.Id == top.Id);
        fetched.ChildrenDiscoveredAt.Should().Be(TestData.Utc);
        fetched.LastWriteTimeUtc.Should().Be(TestData.Utc.AddDays(-1));
    }

    [Fact]
    public async Task HasUndiscoveredFoldersAsync_NewRoot_IsTrue_UntilItsTopFolderIsMarkedDiscovered()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("undiscovered");
        var top = TestData.Folder(root, "");
        db.Context.Folders.Add(top);
        await db.Context.SaveChangesAsync();

        var repository = new FolderRepository(db.Context);
        (await repository.HasUndiscoveredFoldersAsync(root.Id)).Should().BeTrue();

        top.ChildrenDiscoveredAt = TestData.Utc;
        await db.Context.SaveChangesAsync();

        (await repository.HasUndiscoveredFoldersAsync(root.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task HasUndiscoveredFoldersAsync_UndiscoveredDescendant_IsTrue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("deep-undiscovered");
        var top = TestData.Folder(root, "");
        top.ChildrenDiscoveredAt = TestData.Utc;
        var child = TestData.Folder(root, "2025", top);
        db.Context.AddRange(top, child);
        await db.Context.SaveChangesAsync();

        var repository = new FolderRepository(db.Context);

        (await repository.HasUndiscoveredFoldersAsync(root.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task HasUndiscoveredFoldersAsync_TombstonedChild_IsIgnored()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("tombstoned-undiscovered");
        var top = TestData.Folder(root, "");
        top.ChildrenDiscoveredAt = TestData.Utc;
        var removed = TestData.Folder(root, "gone", top, isActive: false);
        db.Context.AddRange(top, removed);
        await db.Context.SaveChangesAsync();

        var repository = new FolderRepository(db.Context);

        (await repository.HasUndiscoveredFoldersAsync(root.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task HasExcludedAncestorAsync_ExcludedParent_IsTrue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("excluded-parent");
        var top = TestData.Folder(root, "");
        var parent = TestData.Folder(root, "Trip", top, isExcluded: true);
        var child = TestData.Folder(root, "Trip/Day1", parent);
        db.Context.AddRange(top, parent, child);
        await db.Context.SaveChangesAsync();

        (await new FolderRepository(db.Context).HasExcludedAncestorAsync(child.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task HasExcludedAncestorAsync_ExcludedGrandparent_IsTrue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("excluded-grandparent");
        var top = TestData.Folder(root, "", isExcluded: true);
        var parent = TestData.Folder(root, "Trip", top);
        var child = TestData.Folder(root, "Trip/Day1", parent);
        db.Context.AddRange(top, parent, child);
        await db.Context.SaveChangesAsync();

        (await new FolderRepository(db.Context).HasExcludedAncestorAsync(child.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task HasExcludedAncestorAsync_OnlyTheFolderItselfIsExcluded_IsFalse()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("excluded-self");
        var top = TestData.Folder(root, "");
        var child = TestData.Folder(root, "Trip", top, isExcluded: true);
        db.Context.AddRange(top, child);
        await db.Context.SaveChangesAsync();

        (await new FolderRepository(db.Context).HasExcludedAncestorAsync(child.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task HasExcludedAncestorAsync_NothingExcluded_IsFalse()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("no-exclusions");
        var top = TestData.Folder(root, "");
        var child = TestData.Folder(root, "Trip", top);
        db.Context.AddRange(top, child);
        await db.Context.SaveChangesAsync();

        (await new FolderRepository(db.Context).HasExcludedAncestorAsync(child.Id)).Should().BeFalse();
    }
}
