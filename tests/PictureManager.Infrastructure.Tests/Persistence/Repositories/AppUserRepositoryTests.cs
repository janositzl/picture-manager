using System.Linq;
using PictureManager.Tests.Support;
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

    private static AppUser NewAccount(string username, UserRole role = UserRole.User, bool active = true) => new()
    {
        Username = username, NormalizedUsername = username.ToLowerInvariant(), DisplayName = username + " display",
        Role = role, IsActive = active, CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task AddAsync_ListAsync_OrderedByUsername_WithAlbumCounts()
    {
        await using var context = await SeededContextAsync();
        var repository = new AppUserRepository(context);
        var zed = await repository.AddAsync(NewAccount("zed"));
        context.Albums.Add(new Album { Name = "A", OwnerUserId = zed.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var rows = await repository.ListAsync();

        zed.Id.Should().BeGreaterThan(0);
        rows.Select(r => r.User.Username).Should().Equal("admin", "zed");
        rows.Single(r => r.User.Username == "zed").AlbumCount.Should().Be(1);
        (await repository.CountAlbumsAsync(zed.Id)).Should().Be(1);
    }

    [Fact]
    public async Task ListActiveDirectoryAsync_SkipsInactiveAndTheCaller_OrderedByDisplayName()
    {
        await using var context = await SeededContextAsync();
        var repository = new AppUserRepository(context);
        await repository.AddAsync(NewAccount("bob"));
        await repository.AddAsync(NewAccount("ann"));
        await repository.AddAsync(NewAccount("off", active: false));

        var entries = await repository.ListActiveDirectoryAsync(AppUser.InitialAdminId);

        entries.Select(e => e.DisplayName).Should().Equal("ann display", "bob display");
    }

    [Fact]
    public async Task CountActiveAdminsAsync_CountsOnlyActiveAdmins()
    {
        await using var context = await SeededContextAsync();
        var repository = new AppUserRepository(context);
        await repository.AddAsync(NewAccount("root2", UserRole.Admin));
        await repository.AddAsync(NewAccount("root3", UserRole.Admin, active: false));
        await repository.AddAsync(NewAccount("plain"));

        (await repository.CountActiveAdminsAsync()).Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_Transfer_MovesAlbumsToTarget_SuffixingEveryClashingName()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = NewAccount("bob");
        db.Context.AppUsers.Add(bob);
        await db.Context.SaveChangesAsync();
        db.Context.Albums.AddRange(
            TestData.Album("TRIP", AppUser.InitialAdminId), TestData.Album("Trip (from bob)", AppUser.InitialAdminId),
            TestData.Album("Beach", bob.Id), TestData.Album("Trip", bob.Id));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AppUserRepository(context);
            await repository.DeleteAsync((await repository.GetByIdAsync(bob.Id))!, AppUser.InitialAdminId);
        }

        await using var check = db.CreateContext();
        (await check.AppUsers.AnyAsync(u => u.Id == bob.Id)).Should().BeFalse();
        var names = await check.Albums.Where(a => a.OwnerUserId == AppUser.InitialAdminId).Select(a => a.Name).ToListAsync();
        names.Should().BeEquivalentTo("TRIP", "Trip (from bob)", "Beach", "Trip (from bob) 2");
        names.Select(n => n.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task DeleteAsync_TransferWithVeryLongName_StaysWithinAlbumNameLimit()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = NewAccount("bob");
        db.Context.AppUsers.Add(bob);
        await db.Context.SaveChangesAsync();
        var longName = new string('x', 300);
        db.Context.Albums.AddRange(TestData.Album(longName, AppUser.InitialAdminId), TestData.Album(longName, bob.Id));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AppUserRepository(context);
            await repository.DeleteAsync((await repository.GetByIdAsync(bob.Id))!, AppUser.InitialAdminId);
        }

        await using var check = db.CreateContext();
        (await check.Albums.Where(a => a.OwnerUserId == AppUser.InitialAdminId).Select(a => a.Name.Length).ToListAsync())
            .Should().OnlyContain(length => length <= 300).And.HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_NoTarget_DeletesTheUsersAlbumsAndTheirEntries_ButNotOthers()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = NewAccount("bob");
        db.Context.AppUsers.Add(bob);
        await db.Context.SaveChangesAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var image = TestData.Image(folder, "pic", contentHash: "H");
        var bobs = TestData.Album("Bob's", bob.Id);
        var admins = TestData.Album("Admin's", AppUser.InitialAdminId);
        db.Context.AlbumImages.AddRange(TestData.AlbumImage(bobs, image, 0), TestData.AlbumImage(admins, image, 0));
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var repository = new AppUserRepository(context);
            await repository.DeleteAsync((await repository.GetByIdAsync(bob.Id))!, null);
        }

        await using var check = db.CreateContext();
        (await check.Albums.Select(a => a.Name).ToListAsync()).Should().Equal("Admin's");
        (await check.AlbumImages.CountAsync()).Should().Be(1);
        (await check.Images.CountAsync()).Should().Be(1);
    }
}
