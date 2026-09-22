using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class ScanJobRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task AddAsync_PersistsScanJob_AndGetByIdAsync_ReturnsIt()
    {
        await using var context = CreateContext();
        var repository = new ScanJobRepository(context);

        var scanJob = new ScanJob { IsRecursive = true, Status = ScanJobStatus.Pending, StartedUtc = DateTime.UtcNow };

        var added = await repository.AddAsync(scanJob);
        var fetched = await repository.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.Status.Should().Be(ScanJobStatus.Pending);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChangesToExistingScanJob()
    {
        await using var context = CreateContext();
        var repository = new ScanJobRepository(context);

        var scanJob = await repository.AddAsync(new ScanJob
        {
            IsRecursive = false,
            Status = ScanJobStatus.Pending,
            StartedUtc = DateTime.UtcNow
        });

        scanJob.Status = ScanJobStatus.Completed;
        scanJob.FilesFound = 10;
        scanJob.CompletedUtc = DateTime.UtcNow;
        await repository.UpdateAsync(scanJob);

        var fetched = await repository.GetByIdAsync(scanJob.Id);
        fetched!.Status.Should().Be(ScanJobStatus.Completed);
        fetched.FilesFound.Should().Be(10);
    }
}
