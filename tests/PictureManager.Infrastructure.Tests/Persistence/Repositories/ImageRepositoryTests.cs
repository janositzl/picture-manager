using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class ImageRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<Folder> SeedFolderAsync(PictureManagerDbContext context)
    {
        var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
        var folder = new Folder
        {
            Name = "Vacation",
            RelativePath = "Vacation",
            Root = root,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow
        };
        context.AddRange(root, folder);
        await context.SaveChangesAsync();
        return folder;
    }

    [Fact]
    public async Task AddAsync_PersistsImage_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        var repository = new ImageRepository(context);

        var image = new Image
        {
            FolderId = folder.Id,
            FileName = "IMG001",
            Extension = ".jpg",
            ContentHash = "hash1",
            FileSize = 100,
            FileModified = DateTime.UtcNow,
            FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var added = await repository.AddAsync(image);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.FileName.Should().Be("IMG001");
    }

    [Fact]
    public async Task GetByFolderIdAsync_ReturnsOnlyImagesInThatFolder()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        var otherFolder = new Folder { Name = "Other", RelativePath = "Other", RootId = folder.RootId, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        context.Folders.Add(otherFolder);
        await context.SaveChangesAsync();

        context.Images.AddRange(
            new Image { FolderId = folder.Id, FileName = "A", Extension = ".jpg", ContentHash = "h1", FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Image { FolderId = folder.Id, FileName = "B", Extension = ".jpg", ContentHash = "h2", FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Image { FolderId = otherFolder.Id, FileName = "C", Extension = ".jpg", ContentHash = "h3", FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        var images = await repository.GetByFolderIdAsync(folder.Id);

        images.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByFolderAndFileNameAsync_IsCaseInsensitive()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        context.Images.Add(new Image
        {
            FolderId = folder.Id, FileName = "IMG001", Extension = ".jpg", ContentHash = "h1",
            FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        var found = await repository.GetByFolderAndFileNameAsync(folder.Id, "img001", ".JPG");

        found.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPendingImageIdsAsync_ReturnsOnlyPendingImagesUnderActiveFolders_ExcludingKnownMissing()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        var inactiveFolder = new Folder
        {
            Name = "Removed", RelativePath = "Removed", RootId = folder.RootId, IsActive = false,
            CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow
        };
        context.Folders.Add(inactiveFolder);
        await context.SaveChangesAsync();

        var pending = new Image
        {
            FolderId = folder.Id, FileName = "Pending", Extension = ".jpg", ContentHash = string.Empty,
            IndexState = IndexState.Pending, FileSize = 1, FileModified = DateTime.UtcNow,
            FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var indexed = new Image
        {
            FolderId = folder.Id, FileName = "Indexed", Extension = ".jpg", ContentHash = "h1",
            IndexState = IndexState.Indexed, FileSize = 1, FileModified = DateTime.UtcNow,
            FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var pendingButMissing = new Image
        {
            FolderId = folder.Id, FileName = "Missing", Extension = ".jpg", ContentHash = string.Empty,
            IndexState = IndexState.Pending, MissingSinceUtc = DateTime.UtcNow, FileSize = 1,
            FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        var pendingUnderInactiveFolder = new Image
        {
            FolderId = inactiveFolder.Id, FileName = "Tombstoned", Extension = ".jpg", ContentHash = string.Empty,
            IndexState = IndexState.Pending, FileSize = 1, FileModified = DateTime.UtcNow,
            FirstSeenUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        context.Images.AddRange(pending, indexed, pendingButMissing, pendingUnderInactiveFolder);
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        var ids = await repository.GetPendingImageIdsAsync();

        ids.Should().BeEquivalentTo(new[] { pending.Id });
    }

    [Fact]
    public async Task GetMissingByContentHashAsync_ReturnsMatchingMissingImage()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        context.Images.Add(new Image
        {
            FolderId = folder.Id, FileName = "IMG001", Extension = ".jpg", ContentHash = "abc123",
            FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, MissingSinceUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        var found = await repository.GetMissingByContentHashAsync("abc123");

        found.Should().NotBeNull();
        found!.FileName.Should().Be("IMG001");
    }

    [Fact]
    public async Task GetMissingByContentHashAsync_ExcludesActiveImageWithSameHash()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        context.Images.Add(new Image
        {
            FolderId = folder.Id, FileName = "IMG001", Extension = ".jpg", ContentHash = "abc123",
            FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, MissingSinceUtc = null
        });
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        var found = await repository.GetMissingByContentHashAsync("abc123");

        found.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges_AndDeleteAsync_RemovesRow()
    {
        await using var context = CreateContext();
        var folder = await SeedFolderAsync(context);
        var image = new Image
        {
            FolderId = folder.Id, FileName = "IMG001", Extension = ".jpg", ContentHash = "h1",
            FileSize = 1, FileModified = DateTime.UtcNow, FirstSeenUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        context.Images.Add(image);
        await context.SaveChangesAsync();

        var repository = new ImageRepository(context);
        image.IsFavorite = true;
        await repository.UpdateAsync(image);
        (await repository.GetByIdAsync(image.Id))!.IsFavorite.Should().BeTrue();

        await repository.DeleteAsync(image);
        (await repository.GetByIdAsync(image.Id)).Should().BeNull();
    }
}
