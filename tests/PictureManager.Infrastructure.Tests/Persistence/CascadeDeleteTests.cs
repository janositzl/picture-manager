using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

public class CascadeDeleteTests
{
    private static DbContextOptions<PictureManagerDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task DeletingFolder_CascadesToItsImagesAndTheirAlbumImages()
    {
        var options = CreateOptions();
        int folderId;

        await using (var context = new PictureManagerDbContext(options))
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
            var image = new Image
            {
                FileName = "IMG001",
                Extension = ".jpg",
                ContentHash = "hash1",
                FileSize = 100,
                FileModified = DateTime.UtcNow,
                FirstSeenUtc = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Folder = folder
            };
            var owner = new AppUser { Id = 500, DisplayName = "Owner", Role = UserRole.User };
            var album = new Album
            {
                Name = "Album1",
                OwnerUser = owner,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var albumImage = new AlbumImage { Album = album, Image = image, SortOrder = 0, AddedAt = DateTime.UtcNow };

            context.AddRange(root, folder, image, owner, album, albumImage);
            await context.SaveChangesAsync();
            folderId = folder.Id;
        }

        await using (var context = new PictureManagerDbContext(options))
        {
            var folder = await context.Folders
                .Include(f => f.Images)
                .ThenInclude(i => i.AlbumImages)
                .SingleAsync(f => f.Id == folderId);

            context.Folders.Remove(folder);
            await context.SaveChangesAsync();
        }

        await using (var context = new PictureManagerDbContext(options))
        {
            (await context.Images.CountAsync()).Should().Be(0);
            (await context.AlbumImages.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task DeletingParentFolder_CascadesToChildFolders()
    {
        var options = CreateOptions();
        int parentId;

        await using (var context = new PictureManagerDbContext(options))
        {
            var root = new ImageRoot { Name = "root", MountPath = "/images", CreatedUtc = DateTime.UtcNow };
            var parent = new Folder
            {
                Name = "Parent",
                RelativePath = "Parent",
                Root = root,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow
            };
            var child = new Folder
            {
                Name = "Child",
                RelativePath = "Parent/Child",
                Root = root,
                Parent = parent,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow
            };

            context.AddRange(root, parent, child);
            await context.SaveChangesAsync();
            parentId = parent.Id;
        }

        await using (var context = new PictureManagerDbContext(options))
        {
            var parent = await context.Folders
                .Include(f => f.Children)
                .SingleAsync(f => f.Id == parentId);

            context.Folders.Remove(parent);
            await context.SaveChangesAsync();
        }

        await using (var context = new PictureManagerDbContext(options))
        {
            (await context.Folders.CountAsync()).Should().Be(0);
        }
    }
}
