using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class AppSettingsPostgresTests
{
    [Fact]
    public async Task UpdateAsync_PersistsAllThreeLists()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        await using (var context = db.CreateContext())
        {
            var repository = new AppSettingsRepository(context);
            var settings = await repository.GetAsync();
            settings.ExcludedFolderNames = new List<string> { "raw", "Thumbs" };
            settings.ExcludedExtensions = new List<string> { ".png" };
            settings.IncludedExtensions = new List<string> { ".jpg" };
            await repository.UpdateAsync(settings);
        }

        await using var read = db.CreateContext();
        var saved = await new AppSettingsRepository(read).GetAsync();
        saved.ExcludedFolderNames.Should().Equal("raw", "Thumbs");
        saved.ExcludedExtensions.Should().Equal(".png");
        saved.IncludedExtensions.Should().Equal(".jpg");
    }
}
