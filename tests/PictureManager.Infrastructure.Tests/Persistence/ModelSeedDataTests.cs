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
    public async Task Database_SeedsSystemAppUserAndDefaultAppSettings()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var user = await context.AppUsers.SingleAsync();
        user.Id.Should().Be(1);
        user.DisplayName.Should().Be("System");
        user.Role.Should().Be(UserRole.User);
        user.ZitadelSubjectId.Should().BeNull();

        var settings = await context.Settings.SingleAsync();
        settings.Id.Should().Be(1);
        settings.ExcludedFolderNames.Should().Contain("raw");
        settings.ExcludedExtensions.Should().Contain(".heic");
    }

    [Fact]
    public void Model_RegistersAllEightEntityTypes()
    {
        using var context = CreateContext();

        context.Model.GetEntityTypes().Select(e => e.ClrType).Should().BeEquivalentTo(new[]
        {
            typeof(ImageRoot), typeof(Folder), typeof(Image), typeof(Album),
            typeof(AlbumImage), typeof(AppUser), typeof(Job), typeof(AppSettings)
        });
    }
}
