using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AlbumRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AddAsync_PersistsAlbum_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var owner = new AppUser { Id = 1, DisplayName = "System", Role = UserRole.User };
        context.AppUsers.Add(owner);
        await context.SaveChangesAsync();

        var repository = new AlbumRepository(context);
        var album = new Album
        {
            Name = "Best of 2026",
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var added = await repository.AddAsync(album);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Best of 2026");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryAlbum()
    {
        await using var context = CreateContext();
        var owner = new AppUser { Id = 1, DisplayName = "System", Role = UserRole.User };
        context.AppUsers.Add(owner);
        context.Albums.AddRange(
            new Album { Name = "A", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Album { Name = "B", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var repository = new AlbumRepository(context);
        var albums = await repository.GetAllAsync();

        albums.Should().HaveCount(2);
    }
}
