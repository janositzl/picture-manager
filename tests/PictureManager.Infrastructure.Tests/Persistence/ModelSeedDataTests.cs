using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

public class ModelSeedDataTests
{
    private static PictureManagerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PictureManagerDbContext(options);
    }

    [Fact]
    public async Task Database_SeedsInitialAdminAndDefaultAppSettings()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var user = await context.AppUsers.SingleAsync();
        user.Id.Should().Be(AppUser.InitialAdminId);
        user.Username.Should().Be("admin");
        user.NormalizedUsername.Should().Be("admin");
        user.DisplayName.Should().Be("Administrator");
        user.Role.Should().Be(UserRole.Admin);
        user.IsActive.Should().BeTrue();
        user.PasswordHash.Should().BeNull();
        user.SecurityStamp.Should().NotBe(Guid.Empty);

        var settings = await context.Settings.SingleAsync();
        settings.Id.Should().Be(1);
        settings.ExcludedFolderNames.Should().Contain("raw");
        settings.ExcludedExtensions.Should().Contain(".heic");
    }

    [Fact]
    public void Model_RegistersAllEntityTypes()
    {
        using var context = CreateContext();

        context.Model.GetEntityTypes().Select(e => e.ClrType).Should().BeEquivalentTo(new[]
        {
            typeof(ImageRoot), typeof(Folder), typeof(Image), typeof(Album),
            typeof(AlbumImage), typeof(UserFavorite), typeof(AlbumShare), typeof(AppUser), typeof(Job), typeof(AppSettings),
            typeof(FaceModel), typeof(FaceProcessingState), typeof(Face), typeof(Person),
            typeof(Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey)
        });
    }
}
