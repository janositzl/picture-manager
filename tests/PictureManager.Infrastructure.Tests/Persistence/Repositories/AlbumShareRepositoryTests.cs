using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Albums;
using PictureManager.Application.Images;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AlbumShareRepositoryTests
{
    private static AlbumShare Share(Album album, AppUser user, SharePermission permission) =>
        new() { Album = album, User = user, Permission = permission, CreatedAt = TestData.Utc };

    [Fact]
    public async Task GetAccessibleAsync_ResolvesOwnerEditorViewer_AndNullForStrangers()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var carol = TestData.User("carol");
        var dave = TestData.User("dave");
        var album = TestData.Album("Trip");
        db.Context.AlbumShares.AddRange(Share(album, bob, SharePermission.Viewer), Share(album, carol, SharePermission.Editor));
        db.Context.AppUsers.Add(dave);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);

        (await repository.GetAccessibleAsync(album.Id, AppUser.InitialAdminId))!.Access.Should().Be(AlbumAccess.Owner);
        var forBob = await repository.GetAccessibleAsync(album.Id, bob.Id);
        forBob!.Access.Should().Be(AlbumAccess.Viewer);
        forBob.OwnerDisplayName.Should().Be("Administrator");
        forBob.Album.Name.Should().Be("Trip");
        (await repository.GetAccessibleAsync(album.Id, carol.Id))!.Access.Should().Be(AlbumAccess.Editor);
        (await repository.GetAccessibleAsync(album.Id, dave.Id)).Should().BeNull();
        (await repository.GetAccessibleAsync(999_999, AppUser.InitialAdminId)).Should().BeNull();
    }

    [Fact]
    public async Task GetSummariesAsync_ListsOwnedAndSharedAlbums_WithAccessOwnerAndShareCount()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var carol = TestData.User("carol");
        var mine = TestData.Album("Mine");
        db.Context.AppUsers.AddRange(bob, carol);
        await db.Context.SaveChangesAsync();
        var bobsShared = TestData.Album("Bob shared", bob.Id);
        var bobsPrivate = TestData.Album("Bob private", bob.Id);
        db.Context.Albums.AddRange(mine, bobsShared, bobsPrivate);
        db.Context.AlbumShares.AddRange(
            new AlbumShare { Album = mine, UserId = bob.Id, Permission = SharePermission.Viewer, CreatedAt = TestData.Utc },
            new AlbumShare { Album = mine, UserId = carol.Id, Permission = SharePermission.Editor, CreatedAt = TestData.Utc },
            new AlbumShare { Album = bobsShared, UserId = AppUser.InitialAdminId, Permission = SharePermission.Editor, CreatedAt = TestData.Utc });
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new AlbumRepository(context).GetSummariesAsync(AppUser.InitialAdminId);

        rows.Select(r => r.Name).Should().Equal("Bob shared", "Mine");
        rows[0].Should().Match<AlbumSummaryRow>(r => !r.IsOwner && r.IsEditor && r.OwnerDisplayName == "bob");
        rows[1].Should().Match<AlbumSummaryRow>(r => r.IsOwner && r.ShareCount == 2 && r.OwnerDisplayName == "Administrator");
    }

    [Fact]
    public async Task SetShareAsync_Upserts_GetSharesAsync_OrdersByName_RemoveShareAsync_ReportsWhetherOneWasThere()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var zoe = TestData.User("zoe");
        var ann = TestData.User("ann");
        var album = TestData.Album("Trip");
        db.Context.AppUsers.AddRange(zoe, ann);
        db.Context.Albums.Add(album);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new AlbumRepository(context);
        await repository.SetShareAsync(album.Id, zoe.Id, SharePermission.Viewer, TestData.Utc);
        await repository.SetShareAsync(album.Id, ann.Id, SharePermission.Viewer, TestData.Utc);
        await repository.SetShareAsync(album.Id, zoe.Id, SharePermission.Editor, TestData.Utc);

        (await repository.GetSharesAsync(album.Id)).Should().Equal(
            new AlbumShareRow(ann.Id, "ann", SharePermission.Viewer),
            new AlbumShareRow(zoe.Id, "zoe", SharePermission.Editor));
        (await repository.RemoveShareAsync(album.Id, ann.Id)).Should().BeTrue();
        (await repository.RemoveShareAsync(album.Id, ann.Id)).Should().BeFalse();
        (await repository.GetSharesAsync(album.Id)).Select(s => s.UserId).Should().Equal(zoe.Id);
    }

    [Fact]
    public async Task DeletingAnAlbumOrAUser_CascadesToSharesAndFavorites()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var kept = TestData.Album("Kept");
        var dropped = TestData.Album("Dropped");
        var image = TestData.Image(TestData.Folder(TestData.Root("r"), ""), "pic");
        db.Context.AlbumShares.AddRange(Share(kept, bob, SharePermission.Viewer), Share(dropped, bob, SharePermission.Viewer));
        db.Context.UserFavorites.Add(new UserFavorite { User = bob, Image = image, CreatedAt = TestData.Utc });
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
        {
            var albums = new AlbumRepository(context);
            await albums.DeleteAsync((await albums.GetAccessibleAsync(dropped.Id, AppUser.InitialAdminId))!.Album);
            (await context.AlbumShares.Select(s => s.AlbumId).ToListAsync()).Should().Equal(kept.Id);

            var users = new AppUserRepository(context);
            await users.DeleteAsync((await users.GetByIdAsync(bob.Id))!, null);
        }

        await using var check = db.CreateContext();
        (await check.AlbumShares.CountAsync()).Should().Be(0);
        (await check.UserFavorites.CountAsync()).Should().Be(0);
        (await check.Images.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetAlbumsContainingAsync_ListsOnlyAlbumsTheCallerCanSee_WithTheirAccess()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var bob = TestData.User("bob");
        var image = TestData.Image(TestData.Folder(TestData.Root("r"), ""), "pic");
        db.Context.AppUsers.Add(bob);
        await db.Context.SaveChangesAsync();
        var mine = TestData.Album("Mine");
        var sharedToMe = TestData.Album("Shared to me", bob.Id);
        var bobsPrivate = TestData.Album("Bob private", bob.Id);
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(mine, image, 0), TestData.AlbumImage(sharedToMe, image, 0), TestData.AlbumImage(bobsPrivate, image, 0));
        db.Context.AlbumShares.Add(new AlbumShare { Album = sharedToMe, UserId = AppUser.InitialAdminId, Permission = SharePermission.Viewer, CreatedAt = TestData.Utc });
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var refs = await new ImageQueryRepository(context).GetAlbumsContainingAsync(image.Id, AppUser.InitialAdminId);

        refs.Should().Equal(
            new AlbumRefRow(mine.Id, "Mine", true, false),
            new AlbumRefRow(sharedToMe.Id, "Shared to me", false, false));
    }
}
