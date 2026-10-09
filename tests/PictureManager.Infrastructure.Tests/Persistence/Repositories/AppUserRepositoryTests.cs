using System;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using System.Threading.Tasks;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;
using PictureManager.Infrastructure.Persistence.Repositories;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AppUserRepositoryTests
{
    [Fact]
    public async Task GetByIdAsync_ReturnsSeededSystemUser()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new PictureManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var repository = new AppUserRepository(context);
        var user = await repository.GetByIdAsync(AppUser.InitialAdminId);

        user.Should().NotBeNull();
        user!.DisplayName.Should().Be("Administrator");
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new PictureManagerDbContext(options);

        var repository = new AppUserRepository(context);
        var user = await repository.GetByIdAsync(999);

        user.Should().BeNull();
    }
}
