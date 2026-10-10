using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

/// <summary>Rolls the schema back to before per-user favorites, sets the old flag, and migrates forward again.</summary>
public class UserFavoritesMigrationTests
{
    private const string BeforeFavorites = "20261009192921_UserAccounts";

    [Fact]
    public async Task GlobalFavorites_MoveToTheInitialAdmin_AndRollingBackRestoresTheFlag()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var favorite = TestData.Image(folder, "fav");
        var plain = TestData.Image(folder, "plain");
        db.Context.Images.AddRange(favorite, plain);
        db.Context.UserFavorites.Add(TestData.Favorite(AppUser.InitialAdminId, favorite));
        await db.Context.SaveChangesAsync();
        var migrator = db.Context.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeFavorites);
        (await db.Context.Database.SqlQueryRaw<int>("SELECT \"Id\" AS \"Value\" FROM \"Images\" WHERE \"IsFavorite\"").ToListAsync())
            .Should().Equal(favorite.Id);

        await migrator.MigrateAsync();

        await using var check = db.CreateContext();
        (await check.UserFavorites.Select(f => new { f.UserId, f.ImageId }).ToListAsync())
            .Should().Equal(new { UserId = AppUser.InitialAdminId, ImageId = favorite.Id });
    }

    [Fact]
    public async Task GlobalFavorites_GoToTheFirstActiveAdmin_WhenTheInitialAdminWasDeleted()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var favorite = TestData.Image(TestData.Folder(TestData.Root("r"), ""), "fav");
        var successor = TestData.User("root2", UserRole.Admin);
        db.Context.Images.Add(favorite);
        db.Context.AppUsers.Add(successor);
        await db.Context.SaveChangesAsync();
        var migrator = db.Context.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeFavorites);
        await db.Context.Database.ExecuteSqlRawAsync("DELETE FROM \"AppUsers\" WHERE \"Id\" = 1");
        await db.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Images\" SET \"IsFavorite\" = true WHERE \"Id\" = {favorite.Id}");

        await migrator.MigrateAsync();

        await using var check = db.CreateContext();
        (await check.UserFavorites.Select(f => new { f.UserId, f.ImageId }).ToListAsync())
            .Should().Equal(new { UserId = successor.Id, ImageId = favorite.Id });
    }
}
