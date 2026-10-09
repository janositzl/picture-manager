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

    private static async Task<PictureManagerDbContext> SeededContextAsync()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var context = new PictureManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    [Fact]
    public async Task GetByNormalizedUsernameAsync_FindsSeededAdmin()
    {
        await using var context = await SeededContextAsync();
        (await new AppUserRepository(context).GetByNormalizedUsernameAsync("admin"))!.Id.Should().Be(AppUser.InitialAdminId);
    }

    [Fact]
    public async Task AnyActiveAdminWithPasswordAsync_FalseUntilAPasswordIsSet_AndIgnoresInactiveAdmins()
    {
        await using var context = await SeededContextAsync();
        var repository = new AppUserRepository(context);
        (await repository.AnyActiveAdminWithPasswordAsync()).Should().BeFalse();

        context.AppUsers.Add(new AppUser { Username = "old", NormalizedUsername = "old", DisplayName = "Old", Role = UserRole.Admin, IsActive = false, PasswordHash = "x" });
        await context.SaveChangesAsync();
        (await repository.AnyActiveAdminWithPasswordAsync()).Should().BeFalse();

        var admin = (await repository.GetFirstActiveAdminAsync())!;
        admin.Id.Should().Be(AppUser.InitialAdminId);
        admin.PasswordHash = "hash";
        await repository.UpdateAsync(admin);
        (await repository.AnyActiveAdminWithPasswordAsync()).Should().BeTrue();
    }
}
