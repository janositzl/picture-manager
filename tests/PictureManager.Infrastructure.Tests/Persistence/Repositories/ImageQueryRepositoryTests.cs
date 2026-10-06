using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Application.Images;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class ImageQueryRepositoryTests
{
    private static readonly ImageListFilter NoFilter = new(null, null, null, false);

    private static async Task<List<ImageRow>> PageAllAsync(
        ImageQueryRepository repository, ImageListFilter filter, ImageSort sort, SortDirection direction, int pageSize)
    {
        var all = new List<ImageRow>();
        ImageKeyset? after = null;
        for (var guard = 0; guard < 100; guard++)
        {
            var page = await repository.ListAsync(filter, sort, direction, after, pageSize);
            all.AddRange(page);
            if (page.Count < pageSize)
                return all;
            var last = page[^1];
            after = new ImageKeyset(last.SortDate, last.SortName, last.Id);
        }

        throw new InvalidOperationException("Paging did not terminate.");
    }

    [Fact]
    public async Task ListAsync_ExcludesMissingImages_InactiveFolders_AndInactiveRoots()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("active");
        var folder = TestData.Folder(root, "");
        var inactiveFolder = TestData.Folder(root, "gone", folder, isActive: false);
        var inactiveRoot = TestData.Root("offline", isActive: false);
        var offlineFolder = TestData.Folder(inactiveRoot, "");
        var visible = TestData.Image(folder, "visible");
        db.Context.Images.AddRange(
            visible,
            TestData.Image(folder, "missing", missingSinceUtc: TestData.Utc),
            TestData.Image(inactiveFolder, "in-removed-folder"),
            TestData.Image(offlineFolder, "on-offline-root"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context).ListAsync(NoFilter, ImageSort.Date, SortDirection.Desc, null, 50);

        rows.Select(r => r.Id).Should().Equal(visible.Id);
    }

    [Fact]
    public async Task ListAsync_HasFacesFilter_SplitsPhotosByNonIgnoredFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var withFace = TestData.Image(folder, "with");
        var onlyIgnored = TestData.Image(folder, "ignored");
        var none = TestData.Image(folder, "none");
        db.Context.Images.AddRange(withFace, onlyIgnored, none);
        await db.Context.SaveChangesAsync();
        var model = await FaceTestData.AddModelAsync(db.Context);
        await FaceTestData.AddFaceAsync(db.Context, withFace.Id, model, FaceTestData.Embedding(0), null, FaceAssignmentState.Unknown);
        await FaceTestData.AddFaceAsync(db.Context, onlyIgnored.Id, model, FaceTestData.Embedding(1), null, FaceAssignmentState.Ignored);

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var with = await repository.ListAsync(new ImageListFilter(null, null, null, false, HasFaces: true), ImageSort.Name, SortDirection.Asc, null, 50);
        var without = await repository.ListAsync(new ImageListFilter(null, null, null, false, HasFaces: false), ImageSort.Name, SortDirection.Asc, null, 50);

        with.Select(r => r.Id).Should().Equal(withFace.Id);
        without.Select(r => r.Id).Should().BeEquivalentTo(new[] { onlyIgnored.Id, none.Id });
    }

    [Fact]
    public async Task ListAsync_FolderIdFilter_ReturnsOnlyDirectImages()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var parent = TestData.Folder(root, "");
        var child = TestData.Folder(root, "child", parent);
        var direct = TestData.Image(parent, "direct");
        db.Context.Images.AddRange(direct, TestData.Image(child, "nested"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context)
            .ListAsync(new ImageListFilter(parent.Id, null, null, false), ImageSort.Date, SortDirection.Desc, null, 50);

        rows.Select(r => r.Id).Should().Equal(direct.Id);
    }

    [Fact]
    public async Task ListAsync_DateDesc_OrdersBySortDateWithFileModifiedFallback_AndKeysetContinues()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var newest = TestData.Image(folder, "a", dateTaken: new DateTime(2025, 1, 3, 12, 0, 0));
        var undated = TestData.Image(folder, "b", fileModified: new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc));
        var oldest = TestData.Image(folder, "c", dateTaken: new DateTime(2025, 1, 1, 12, 0, 0));
        db.Context.Images.AddRange(oldest, undated, newest);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var first = await repository.ListAsync(NoFilter, ImageSort.Date, SortDirection.Desc, null, 2);
        var last = first[^1];
        var second = await repository.ListAsync(NoFilter, ImageSort.Date, SortDirection.Desc,
            new ImageKeyset(last.SortDate, last.SortName, last.Id), 2);

        first.Select(r => r.Id).Should().Equal(newest.Id, undated.Id);
        second.Select(r => r.Id).Should().Equal(oldest.Id);
    }

    [Fact]
    public async Task ListAsync_NameAsc_IsCaseInsensitive_AndKeysetContinues()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var b = TestData.Image(folder, "b");
        var a = TestData.Image(folder, "A");
        var c = TestData.Image(folder, "c");
        db.Context.Images.AddRange(b, a, c);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var first = await repository.ListAsync(NoFilter, ImageSort.Name, SortDirection.Asc, null, 2);
        var last = first[^1];
        var second = await repository.ListAsync(NoFilter, ImageSort.Name, SortDirection.Asc,
            new ImageKeyset(last.SortDate, last.SortName, last.Id), 2);

        first.Select(r => r.Id).Should().Equal(a.Id, b.Id);
        first[0].SortName.Should().Be("a");
        second.Select(r => r.Id).Should().Equal(c.Id);
    }

    [Fact]
    public async Task ListAsync_FileNameAndFolderNameFilters_AreCaseInsensitiveSubstrings()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("r");
        var top = TestData.Folder(root, "");
        var madeira = TestData.Folder(root, "Madeira 2025", top);
        var inMadeira = TestData.Image(madeira, "IMG_4471");
        var elsewhere = TestData.Image(top, "img_9999");
        db.Context.Images.AddRange(inMadeira, elsewhere);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        var byFile = await repository.ListAsync(new ImageListFilter(null, null, "img_44", false), ImageSort.Name, SortDirection.Asc, null, 50);
        var byFolder = await repository.ListAsync(new ImageListFilter(null, "madeira", null, false), ImageSort.Name, SortDirection.Asc, null, 50);

        byFile.Select(r => r.Id).Should().Equal(inMadeira.Id);
        byFolder.Select(r => r.Id).Should().Equal(inMadeira.Id);
    }

    [Fact]
    public async Task ListAsync_FileNameFilter_TreatsLikeWildcardsLiterally()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var underscore = TestData.Image(folder, "IMG_1");
        var percent = TestData.Image(folder, "100%");
        db.Context.Images.AddRange(underscore, percent, TestData.Image(folder, "IMGX1"), TestData.Image(folder, "1000"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        (await repository.ListAsync(new ImageListFilter(null, null, "_", false), ImageSort.Name, SortDirection.Asc, null, 50))
            .Select(r => r.Id).Should().Equal(underscore.Id);
        (await repository.ListAsync(new ImageListFilter(null, null, "%", false), ImageSort.Name, SortDirection.Asc, null, 50))
            .Select(r => r.Id).Should().Equal(percent.Id);
    }

    [Fact]
    public async Task ListAsync_FavoritesOnly_ReturnsOnlyFavorites()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var favorite = TestData.Image(folder, "fav", isFavorite: true);
        db.Context.Images.AddRange(favorite, TestData.Image(folder, "plain"));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context)
            .ListAsync(new ImageListFilter(null, null, null, true), ImageSort.Date, SortDirection.Desc, null, 50);

        rows.Select(r => r.Id).Should().Equal(favorite.Id);
    }

    [Fact]
    public async Task ListAsync_ManyImagesWithSameSortDate_PagesEachImageExactlyOnce()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var images = Enumerable.Range(0, 7).Select(n => TestData.Image(folder, $"burst{n}")).ToList();
        db.Context.Images.AddRange(images);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        foreach (var direction in new[] { SortDirection.Desc, SortDirection.Asc })
        {
            var paged = await PageAllAsync(repository, NoFilter, ImageSort.Date, direction, 3);
            paged.Select(r => r.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(images.Select(i => i.Id));
        }
    }

    [Fact]
    public async Task ListAsync_NameSortAcrossPages_WithAccentedAndMixedCaseNames_ReturnsEachImageOnce()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var images = new[] { "Ádám", "zebra", "Apple", "ádám2", "Zulu", "apple", "Éva", "eva" }
            .Select(name => TestData.Image(folder, name)).ToList();
        db.Context.Images.AddRange(images);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);

        foreach (var direction in new[] { SortDirection.Asc, SortDirection.Desc })
        {
            var paged = await PageAllAsync(repository, NoFilter, ImageSort.Name, direction, 3);
            paged.Select(r => r.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(images.Select(i => i.Id));
        }
    }

    [Fact]
    public async Task ListAsync_InvalidImage_RowCarriesIndexStateInvalid()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var invalid = TestData.Image(folder, "corrupt", indexState: IndexState.Invalid);
        db.Context.Images.Add(invalid);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context).ListAsync(NoFilter, ImageSort.Date, SortDirection.Desc, null, 50);

        rows.Single().IndexState.Should().Be(IndexState.Invalid);
    }

    [Fact]
    public async Task GetVisibleDetailAsync_ReturnsMetadataAndLocation_AndNullForMissingImage()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var folder = TestData.Folder(root, "Holidays/Madeira", top);
        var image = TestData.Image(folder, "IMG_1");
        image.CameraMake = "Canon";
        image.RawMetadata = """{"Exif IFD0.Make":"Canon"}""";
        var missing = TestData.Image(folder, "IMG_2", missingSinceUtc: TestData.Utc);
        db.Context.Images.AddRange(image, missing);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var detail = await repository.GetVisibleDetailAsync(image.Id);

        detail.Should().NotBeNull();
        detail!.RootName.Should().Be("nas");
        detail.RelativePath.Should().Be("Holidays/Madeira");
        detail.CameraMake.Should().Be("Canon");
        detail.RawMetadata.Should().Contain("Canon");
        detail.Image.IndexState.Should().Be(IndexState.Indexed);
        (await repository.GetVisibleDetailAsync(missing.Id)).Should().BeNull();
    }

    [Fact]
    public async Task SetFavoriteAsync_SetsAndClears_AndReturnsFalseForMissingImage()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var image = TestData.Image(folder, "a");
        var missing = TestData.Image(folder, "b", missingSinceUtc: TestData.Utc);
        db.Context.Images.AddRange(image, missing);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var repository = new ImageQueryRepository(context);
        var favoritesOnly = new ImageListFilter(null, null, null, true);

        (await repository.SetFavoriteAsync(image.Id, true, TestData.Utc)).Should().BeTrue();
        (await repository.ListAsync(favoritesOnly, ImageSort.Date, SortDirection.Desc, null, 5))
            .Select(r => r.Id).Should().Equal(image.Id);
        (await repository.SetFavoriteAsync(image.Id, false, TestData.Utc)).Should().BeTrue();
        (await repository.ListAsync(favoritesOnly, ImageSort.Date, SortDirection.Desc, null, 5)).Should().BeEmpty();
        (await repository.SetFavoriteAsync(missing.Id, true, TestData.Utc)).Should().BeFalse();
        (await repository.SetFavoriteAsync(999_999, true, TestData.Utc)).Should().BeFalse();
    }

    [Fact]
    public async Task GetVisibleIdsAsync_ReturnsOnlyVisibleRequestedIds()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var visible = TestData.Image(folder, "a");
        var missing = TestData.Image(folder, "b", missingSinceUtc: TestData.Utc);
        db.Context.Images.AddRange(visible, missing);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var ids = await new ImageQueryRepository(context).GetVisibleIdsAsync(new[] { visible.Id, missing.Id, 999_999 });

        ids.Should().Equal(visible.Id);
    }

    [Fact]
    public async Task GetVisibleIdsInFolderAsync_OrdersBySortDateThenId()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var folder = TestData.Folder(TestData.Root("r"), "");
        var later = TestData.Image(folder, "later", dateTaken: new DateTime(2025, 5, 1, 0, 0, 0));
        var earlier = TestData.Image(folder, "earlier", dateTaken: new DateTime(2024, 5, 1, 0, 0, 0));
        db.Context.Images.AddRange(later, earlier);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var ids = await new ImageQueryRepository(context).GetVisibleIdsInFolderAsync(folder.Id);

        ids.Should().Equal(earlier.Id, later.Id);
    }

    [Fact]
    public async Task GetAlbumsContainingAsync_IsScopedToOwner_AndOrderedByName()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var other = new AppUser { DisplayName = "Other", Role = UserRole.User };
        db.Context.AppUsers.Add(other);
        await db.Context.SaveChangesAsync();

        var folder = TestData.Folder(TestData.Root("r"), "");
        var image = TestData.Image(folder, "a");
        var zoo = TestData.Album("Zoo");
        var beach = TestData.Album("beach");
        var foreign = TestData.Album("Foreign", other.Id);
        db.Context.AlbumImages.AddRange(
            TestData.AlbumImage(zoo, image, 0),
            TestData.AlbumImage(beach, image, 0),
            TestData.AlbumImage(foreign, image, 0));
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var albums = await new ImageQueryRepository(context).GetAlbumsContainingAsync(image.Id, AppUser.SystemUserId);

        albums.Should().Equal(new AlbumRef(beach.Id, "beach"), new AlbumRef(zoo.Id, "Zoo"));
    }

    [Fact]
    public async Task ListAsync_RowsCarryRootNameAndRelativePath()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var root = TestData.Root("nas");
        var top = TestData.Folder(root, "");
        var madeira = TestData.Folder(root, "Holidays/Madeira", top);
        var atTop = TestData.Image(top, "a");
        var nested = TestData.Image(madeira, "b");
        db.Context.Images.AddRange(atTop, nested);
        await db.Context.SaveChangesAsync();

        await using var context = db.CreateContext();
        var rows = await new ImageQueryRepository(context).ListAsync(NoFilter, ImageSort.Name, SortDirection.Asc, null, 50);

        rows.Select(r => (r.Id, r.RootName, r.RelativePath)).Should().Equal(
            (atTop.Id, "nas", ""),
            (nested.Id, "nas", "Holidays/Madeira"));
    }
}
