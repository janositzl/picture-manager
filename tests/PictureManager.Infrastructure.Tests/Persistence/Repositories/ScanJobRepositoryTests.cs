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

    [Fact]
    public async Task ReloadAsync_PullsValuesCommittedByAnotherDbContext_IntoTheTrackedInstance()
    {
        // This is the scenario ScanService is actually in: it holds a ScanJob instance tracked by
        // its own DbContext/scope for the whole duration of a scan, while EnrichmentBackgroundService
        // concurrently updates the same row through a DIFFERENT DbContext/scope. Both contexts point
        // at the same underlying store (same InMemory database name), the way two DI-scoped
        // DbContext instances would both point at the same Postgres database.
        var databaseName = Guid.NewGuid().ToString();
        PictureManagerDbContext CreateSharedContext() =>
            new(new DbContextOptionsBuilder<PictureManagerDbContext>().UseInMemoryDatabase(databaseName).Options);

        // Scope 1: create and hold a tracked ScanJob, exactly as ScanService does for the lifetime
        // of a scan (the instance returned by AddAsync stays tracked by this same DbContext).
        await using var scope1Context = CreateSharedContext();
        var scope1Repository = new ScanJobRepository(scope1Context);
        var scanJob = await scope1Repository.AddAsync(new ScanJob
        {
            IsRecursive = true,
            Status = ScanJobStatus.Enumerating,
            StartedUtc = DateTime.UtcNow
        });

        // Scope 2: a separate DbContext instance -- standing in for EnrichmentBackgroundService's
        // own DI scope -- commits a concurrent change to the same row.
        await using (var scope2Context = CreateSharedContext())
        {
            var scope2Repository = new ScanJobRepository(scope2Context);
            var scope2View = await scope2Repository.GetByIdAsync(scanJob.Id);
            scope2View!.FilesEnriched = 1;
            scope2View.Status = ScanJobStatus.Enriching;
            await scope2Repository.UpdateAsync(scope2View);
        }

        // Proves the bug this whole fix exists for: re-querying scope 1's OWN context for the same
        // key does NOT observe scope 2's committed change. EF Core's identity resolution hands back
        // the SAME already-tracked instance instead of re-querying the store, so a naive
        // "GetByIdAsync as a re-fetch" is a silent no-op.
        var staleRead = await scope1Repository.GetByIdAsync(scanJob.Id);
        staleRead.Should().BeSameAs(scanJob);
        staleRead!.FilesEnriched.Should().Be(0,
            "GetByIdAsync on an id that's already tracked in this context returns the same stale instance, not a fresh read");

        // ReloadAsync is the actual fix: it re-queries the store and overwrites the tracked
        // instance's CURRENT VALUES in place, so scope 2's committed change becomes visible here.
        await scope1Repository.ReloadAsync(scanJob);

        scanJob.FilesEnriched.Should().Be(1);
        scanJob.Status.Should().Be(ScanJobStatus.Enriching);
    }
}
