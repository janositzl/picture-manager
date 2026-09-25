using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Roots;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Roots;

public class ImageRootSeederTests
{
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly List<ImageRoot> _existing = new();

    public ImageRootSeederTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _existing);
        _roots.AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<ImageRoot>());
    }

    private Task SeedAsync(params ImageRootConfigEntry[] entries) =>
        new ImageRootSeeder(_roots, _folders, _clock, new ImageRootsOptions { Entries = new List<ImageRootConfigEntry>(entries) },
            NullLogger<ImageRootSeeder>.Instance).SeedAsync();

    private static ImageRootConfigEntry Entry(string? name, string? mountPath, string? alias = null) =>
        new() { Name = name, MountPath = mountPath, Alias = alias };

    [Fact]
    public async Task SeedAsync_UnknownMountPath_CreatesActiveRootWithAlias()
    {
        await SeedAsync(Entry("nas-photos", "/images/photos", "family_photos"));

        await _roots.Received(1).AddAsync(
            Arg.Is<ImageRoot>(r => r.Name == "nas-photos" && r.MountPath == "/images/photos" && r.Alias == "family_photos"
                                   && r.IsActive && r.CreatedUtc == _clock.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_ExistingMountPath_LeavesRootUntouched_EvenIfConfigNameDiffers()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "Family Photos", MountPath = "/images/photos", IsActive = false });

        await SeedAsync(Entry("nas-photos", "/images/photos", "family_photos"));

        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
        await _roots.DidNotReceive().UpdateAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NameClashesCaseInsensitively_SkipsEntry()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "Photos", MountPath = "/a" });

        await SeedAsync(Entry("photos", "/b"));

        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_AliasClashesWithAnotherRootsSegment_DropsAliasButCreatesRoot()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "family", MountPath = "/a" });

        await SeedAsync(Entry("nas-photos", "/b", "Family"));

        await _roots.Received(1).AddAsync(Arg.Is<ImageRoot>(r => r.Name == "nas-photos" && r.Alias == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_InvalidAlias_DropsAliasButCreatesRoot()
    {
        await SeedAsync(Entry("nas-photos", "/b", "family/photos"));

        await _roots.Received(1).AddAsync(Arg.Is<ImageRoot>(r => r.Name == "nas-photos" && r.Alias == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NameEqualsAnotherRootsAlias_SkipsEntry()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "nas-a", Alias = "photos", MountPath = "/a" });

        await SeedAsync(Entry("Photos", "/b"));

        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "/x")]
    [InlineData("name", null)]
    [InlineData("  ", "/x")]
    [InlineData("a/b", "/x")]
    public async Task SeedAsync_IncompleteOrInvalidEntry_IsSkipped(string? name, string? mountPath)
    {
        await SeedAsync(Entry(name, mountPath));

        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_SecondConfigEntryClashingWithFirst_IsSkipped()
    {
        await SeedAsync(Entry("photos", "/a"), Entry("PHOTOS", "/b"));

        await _roots.Received(1).AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NoEntries_DoesNotThrowOrAdd()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "legacy", MountPath = "/old" });

        var act = () => SeedAsync();

        await act.Should().NotThrowAsync();
        await _roots.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_NewRoot_CreatesItsTopFolder_NotYetDiscovered()
    {
        _roots.AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var root = call.Arg<ImageRoot>();
            root.Id = 5;
            return root;
        });

        await SeedAsync(Entry("nas-photos", "/images/photos"));

        await _folders.Received(1).AddAsync(
            Arg.Is<Folder>(f => f.RootId == 5 && f.ParentId == null && f.RelativePath == "" && f.Name == "nas-photos"
                                && f.ChildrenDiscoveredAt == null && f.CreatedUtc == _clock.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_ExistingAndInactiveRootsWithoutTopFolder_GetOne()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "active", MountPath = "/a", IsActive = true });
        _existing.Add(new ImageRoot { Id = 2, Name = "off", MountPath = "/b", IsActive = false });

        await SeedAsync(Entry("active", "/a"), Entry("off", "/b"));

        await _folders.Received(1).AddAsync(Arg.Is<Folder>(f => f.RootId == 1 && f.Name == "active"), Arg.Any<CancellationToken>());
        await _folders.Received(1).AddAsync(Arg.Is<Folder>(f => f.RootId == 2 && f.Name == "off"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_RootThatHasItsTopFolder_CreatesNoDuplicate()
    {
        _existing.Add(new ImageRoot { Id = 1, Name = "dev", MountPath = "/a", IsActive = true });
        _folders.GetByRootAndRelativePathAsync(1, string.Empty, Arg.Any<CancellationToken>())
            .Returns(new Folder { Id = 10, RootId = 1, Name = "dev", RelativePath = string.Empty });

        await SeedAsync(Entry("dev", "/a"));

        await _folders.DidNotReceive().AddAsync(Arg.Any<Folder>(), Arg.Any<CancellationToken>());
    }
}
