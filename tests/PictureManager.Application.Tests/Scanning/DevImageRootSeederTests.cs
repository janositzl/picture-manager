using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public class DevImageRootSeederTests
{
    [Fact]
    public async Task SeedAsync_OptionsConfigured_AndNoExistingRootWithThatName_CreatesOne()
    {
        var repository = Substitute.For<IImageRootRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<ImageRoot>());
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var seeder = new DevImageRootSeeder(repository, clock, new DevImageRootOptions { Name = "dev", MountPath = "/dev-data/images" });
        await seeder.SeedAsync();

        await repository.Received(1).AddAsync(
            Arg.Is<ImageRoot>(r => r.Name == "dev" && r.MountPath == "/dev-data/images" && r.IsActive),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_RootWithThatNameAlreadyExists_DoesNotCreateAnother()
    {
        var repository = Substitute.For<IImageRootRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ImageRoot> { new() { Id = 1, Name = "dev", MountPath = "/dev-data/images" } });

        var seeder = new DevImageRootSeeder(repository, Substitute.For<IClock>(), new DevImageRootOptions { Name = "dev", MountPath = "/dev-data/images" });
        await seeder.SeedAsync();

        await repository.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_OptionsNotConfigured_DoesNothing()
    {
        var repository = Substitute.For<IImageRootRepository>();

        var seeder = new DevImageRootSeeder(repository, Substitute.For<IClock>(), new DevImageRootOptions());
        await seeder.SeedAsync();

        await repository.DidNotReceive().AddAsync(Arg.Any<ImageRoot>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }
}
