using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Tests.Support;

public class PostgresTestDatabaseTests
{
    [Fact]
    public async Task CreateAsync_ReturnsMigratedDatabase_WithSeedData()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();

        (await db.Context.AppUsers.AnyAsync(u => u.Id == AppUser.InitialAdminId)).Should().BeTrue();
        (await db.Context.Settings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_TwoDatabases_AreIsolated()
    {
        await using var first = await PostgresTestDatabase.CreateAsync();
        await using var second = await PostgresTestDatabase.CreateAsync();

        first.Context.ImageRoots.Add(new ImageRoot
        {
            Name = "only-in-first", MountPath = "/x", IsActive = true,
            CreatedUtc = System.DateTime.UtcNow
        });
        await first.Context.SaveChangesAsync();

        (await second.Context.ImageRoots.AnyAsync(r => r.Name == "only-in-first")).Should().BeFalse();
        first.ConnectionString.Should().NotBe(second.ConnectionString);
    }

    [Fact]
    public async Task CreateContext_ReturnsIndependentContextOnTheSameDatabase()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        db.Context.ImageRoots.Add(new ImageRoot
        {
            Name = "shared", MountPath = "/s", IsActive = true, CreatedUtc = System.DateTime.UtcNow
        });
        await db.Context.SaveChangesAsync();

        await using var other = db.CreateContext();
        (await other.ImageRoots.Select(r => r.Name).ToListAsync()).Should().Contain("shared");
    }
}
