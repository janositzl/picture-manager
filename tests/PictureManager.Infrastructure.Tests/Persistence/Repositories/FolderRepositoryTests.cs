using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
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
}
