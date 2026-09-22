using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Configurations;
using PictureManager.Infrastructure.Persistence.Repositories;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AppSettingsRepositoryTests
{
    [Fact]
    public async Task GetAsync_ReturnsSeededSingletonSettings()
    {
        var options = new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new PictureManagerDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var repository = new AppSettingsRepository(context);
        var settings = await repository.GetAsync();

        settings.Id.Should().Be(AppSettingsConfiguration.SingletonId);
        settings.ExcludedExtensions.Should().Contain(".heic");
    }
}
