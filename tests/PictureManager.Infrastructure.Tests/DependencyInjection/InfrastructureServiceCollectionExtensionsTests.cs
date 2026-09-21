using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.DependencyInjection;
using PictureManager.Infrastructure.Persistence;
using Xunit;

namespace PictureManager.Infrastructure.Tests.DependencyInjection;

public class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInfrastructure_RegistersPictureManagerDbContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PictureManagerDb"] =
                    "Host=localhost;Database=picturemanager;Username=test;Password=test"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        var dbContext = provider.GetService<PictureManagerDbContext>();

        dbContext.Should().NotBeNull();
    }

    [Fact]
    public void AddInfrastructure_MissingConnectionString_Throws()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var act = () => services.AddInfrastructure(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PictureManagerDb*");
    }

    [Fact]
    public void AddInfrastructure_RegistersAllFiveRepositories()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PictureManagerDb"] =
                    "Host=localhost;Database=picturemanager;Username=test;Password=test"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        var provider = services.BuildServiceProvider();

        provider.GetService<IFolderRepository>().Should().NotBeNull();
        provider.GetService<IImageRepository>().Should().NotBeNull();
        provider.GetService<IAlbumRepository>().Should().NotBeNull();
        provider.GetService<IAppUserRepository>().Should().NotBeNull();
        provider.GetService<IImageRootRepository>().Should().NotBeNull();
    }
}
