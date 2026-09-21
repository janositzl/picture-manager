using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class ImageRootRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AddAsync_PersistsImageRoot_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var repository = new ImageRootRepository(context);

        var root = new ImageRoot { Name = "holidays-nas", MountPath = "/images/holidays", CreatedUtc = DateTime.UtcNow };
        var added = await repository.AddAsync(root);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("holidays-nas");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryRegisteredRoot()
    {
        await using var context = CreateContext();
        context.ImageRoots.AddRange(
            new ImageRoot { Name = "root-a", MountPath = "/images/a", CreatedUtc = DateTime.UtcNow },
            new ImageRoot { Name = "root-b", MountPath = "/images/b", CreatedUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var repository = new ImageRootRepository(context);
        var roots = await repository.GetAllAsync();

        roots.Should().HaveCount(2);
    }
}
