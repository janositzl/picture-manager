using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class ScanJobRepositoryTests
{
    private static PictureManagerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PictureManagerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // ExecuteUpdateAsync-based methods need a real relational provider (InMemory doesn't implement
    // ExecuteUpdate). These tests use a throwaway real-Postgres database per test.

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
        // This is the scenario ScanService used to be in (before this fix round moved its
        // finalization writes to targeted ExecuteUpdateAsync calls): it holds a ScanJob instance
        // tracked by its own DbContext/scope, while EnrichmentBackgroundService concurrently
        // updates the same row through a DIFFERENT DbContext/scope. Both contexts point at the
        // same underlying store (same InMemory database name), the way two DI-scoped DbContext
        // instances would both point at the same Postgres database. ReloadAsync remains a
        // general-purpose "refresh a tracked instance" primitive, so it's still tested directly.
        var databaseName = Guid.NewGuid().ToString();
        PictureManagerDbContext CreateSharedContext() =>
            new(new DbContextOptionsBuilder<PictureManagerDbContext>().UseInMemoryDatabase(databaseName).Options);

        // Scope 1: create and hold a tracked ScanJob (the instance returned by AddAsync stays
        // tracked by this same DbContext).
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

        // Proves holding a tracked reference across a concurrent external commit doesn't observe
        // it on its own -- the in-memory instance is simply never touched by scope 2's write.
        scanJob.FilesEnriched.Should().Be(0);
        scanJob.Status.Should().Be(ScanJobStatus.Enumerating);

        // ReloadAsync is the fix for THIS scenario: it re-queries the store and overwrites the
        // tracked instance's CURRENT VALUES in place, so scope 2's committed change becomes
        // visible here.
        await scope1Repository.ReloadAsync(scanJob);

        scanJob.FilesEnriched.Should().Be(1);
        scanJob.Status.Should().Be(ScanJobStatus.Enriching);
    }

    [Fact]
    public async Task GetByIdAsync_CalledTwiceInSameContext_ObservesConcurrentCommitFromAnotherContext()
    {
        // This is the scenario ScanEndpoints.StreamScanEventsAsync's SSE loop is actually in: it
        // polls GetByIdAsync repeatedly against ONE repository/DbContext instance for the whole
        // connection, while EnrichmentBackgroundService and ScanService commit concurrent changes
        // through their OWN DbContext scopes. Proves Critical #1's fix: AsNoTracking() means the
        // second call actually re-reads the database instead of returning the first call's
        // already-tracked (and now stale) instance via EF Core's identity resolution.
        var databaseName = Guid.NewGuid().ToString();
        PictureManagerDbContext CreateSharedContext() =>
            new(new DbContextOptionsBuilder<PictureManagerDbContext>().UseInMemoryDatabase(databaseName).Options);

        await using var scope1Context = CreateSharedContext();
        var scope1Repository = new ScanJobRepository(scope1Context);
        var scanJob = await scope1Repository.AddAsync(new ScanJob
        {
            IsRecursive = true,
            Status = ScanJobStatus.Enumerating,
            StartedUtc = DateTime.UtcNow
        });

        // First poll -- mirrors the SSE loop's first iteration.
        var firstPoll = await scope1Repository.GetByIdAsync(scanJob.Id);
        firstPoll!.Status.Should().Be(ScanJobStatus.Enumerating);

        // A different DbContext scope commits a concurrent change to the same row.
        await using (var scope2Context = CreateSharedContext())
        {
            var scope2Repository = new ScanJobRepository(scope2Context);
            var scope2View = await scope2Repository.GetByIdAsync(scanJob.Id);
            scope2View!.Status = ScanJobStatus.Completed;
            scope2View.FilesEnriched = 5;
            await scope2Repository.UpdateAsync(scope2View);
        }

        // Second poll against the SAME repository/context instance as the first call.
        var secondPoll = await scope1Repository.GetByIdAsync(scanJob.Id);

        secondPoll!.Status.Should().Be(ScanJobStatus.Completed);
        secondPoll.FilesEnriched.Should().Be(5);
    }

    [Fact]
    public async Task HasActiveJobAsync_ReturnsTrue_WhenAJobIsEnumeratingOrEnriching()
    {
        await using var context = CreateContext();
        var repository = new ScanJobRepository(context);

        (await repository.HasActiveJobAsync()).Should().BeFalse();

        var enumerating = await repository.AddAsync(new ScanJob { Status = ScanJobStatus.Enumerating, StartedUtc = DateTime.UtcNow });
        (await repository.HasActiveJobAsync()).Should().BeTrue();

        enumerating.Status = ScanJobStatus.Completed;
        await repository.UpdateAsync(enumerating);
        (await repository.HasActiveJobAsync()).Should().BeFalse();

        await repository.AddAsync(new ScanJob { Status = ScanJobStatus.Enriching, StartedUtc = DateTime.UtcNow });
        (await repository.HasActiveJobAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task SetEnumerationResultAsync_UpdatesFoldersScannedAndFilesFound_ButNotStatus()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new ScanJobRepository(db.Context);

        var scanJob = await repository.AddAsync(new ScanJob { Status = ScanJobStatus.Enumerating, StartedUtc = DateTime.UtcNow });

        await repository.SetEnumerationResultAsync(scanJob.Id, foldersScanned: 3, filesFound: 7);

        var fetched = await repository.GetByIdAsync(scanJob.Id);
        fetched!.FoldersScanned.Should().Be(3);
        fetched.FilesFound.Should().Be(7);
        fetched.Status.Should().Be(ScanJobStatus.Enumerating);
    }

    [Fact]
    public async Task TryTransitionToEnrichingAsync_WhenEnumerating_FlipsStatusAndReturnsTrue()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new ScanJobRepository(db.Context);

        var scanJob = await repository.AddAsync(new ScanJob { Status = ScanJobStatus.Enumerating, StartedUtc = DateTime.UtcNow });

        var result = await repository.TryTransitionToEnrichingAsync(scanJob.Id);

        result.Should().BeTrue();
        (await repository.GetByIdAsync(scanJob.Id))!.Status.Should().Be(ScanJobStatus.Enriching);
    }

    [Fact]
    public async Task TryTransitionToEnrichingAsync_WhenNotEnumerating_ReturnsFalse_AndLeavesRowUnchanged()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new ScanJobRepository(db.Context);

        var scanJob = await repository.AddAsync(new ScanJob { Status = ScanJobStatus.Completed, StartedUtc = DateTime.UtcNow });

        var result = await repository.TryTransitionToEnrichingAsync(scanJob.Id);

        result.Should().BeFalse();
        (await repository.GetByIdAsync(scanJob.Id))!.Status.Should().Be(ScanJobStatus.Completed);
    }

    [Fact]
    public async Task IncrementFilesEnrichedAsync_IncrementsByOne()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new ScanJobRepository(db.Context);

        var scanJob = await repository.AddAsync(new ScanJob { Status = ScanJobStatus.Enriching, FilesEnriched = 2, StartedUtc = DateTime.UtcNow });

        await repository.IncrementFilesEnrichedAsync(scanJob.Id);

        (await repository.GetByIdAsync(scanJob.Id))!.FilesEnriched.Should().Be(3);
    }

    [Fact]
    public async Task TryMarkCompletedIfEnrichedAsync_WhenEnrichedBelowFound_ReturnsFalse_AndLeavesRowUnchanged()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new ScanJobRepository(db.Context);

        var scanJob = await repository.AddAsync(new ScanJob
        {
            Status = ScanJobStatus.Enriching, FilesFound = 5, FilesEnriched = 4, StartedUtc = DateTime.UtcNow
        });

        var result = await repository.TryMarkCompletedIfEnrichedAsync(scanJob.Id, DateTime.UtcNow);

        result.Should().BeFalse();
        var fetched = await repository.GetByIdAsync(scanJob.Id);
        fetched!.Status.Should().Be(ScanJobStatus.Enriching);
        fetched.CompletedUtc.Should().BeNull();
    }

    [Fact]
    public async Task TryMarkCompletedIfEnrichedAsync_AfterFinalIncrementReachesFilesFound_CompletesTheJob()
    {
        // This is the actual race this whole fix round exists for: the last FilesEnriched
        // increment and the completion check must be atomic against concurrent writers, and
        // TryMarkCompletedIfEnrichedAsync must correctly flip the job once the count catches up.
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new ScanJobRepository(db.Context);

        var scanJob = await repository.AddAsync(new ScanJob
        {
            Status = ScanJobStatus.Enriching, FilesFound = 5, FilesEnriched = 4, StartedUtc = DateTime.UtcNow
        });

        await repository.IncrementFilesEnrichedAsync(scanJob.Id);
        var completedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = await repository.TryMarkCompletedIfEnrichedAsync(scanJob.Id, completedUtc);

        result.Should().BeTrue();
        var fetched = await repository.GetByIdAsync(scanJob.Id);
        fetched!.Status.Should().Be(ScanJobStatus.Completed);
        fetched.FilesEnriched.Should().Be(5);
        fetched.CompletedUtc.Should().Be(completedUtc);
    }

    [Fact]
    public async Task SetFailureResultAsync_SetsAllFailureFields()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new ScanJobRepository(db.Context);

        var scanJob = await repository.AddAsync(new ScanJob { Status = ScanJobStatus.Enumerating, StartedUtc = DateTime.UtcNow });
        var completedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await repository.SetFailureResultAsync(scanJob.Id, foldersScanned: 2, filesFound: 4, errorMessage: "boom", status: ScanJobStatus.Failed, completedUtc: completedUtc);

        var fetched = await repository.GetByIdAsync(scanJob.Id);
        fetched!.FoldersScanned.Should().Be(2);
        fetched.FilesFound.Should().Be(4);
        fetched.Status.Should().Be(ScanJobStatus.Failed);
        fetched.ErrorMessage.Should().Be("boom");
        fetched.CompletedUtc.Should().Be(completedUtc);
    }

    [Fact]
    public async Task SetFailureResultAsync_CancelledStatus_AllowsNullErrorMessage()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new ScanJobRepository(db.Context);

        var scanJob = await repository.AddAsync(new ScanJob { Status = ScanJobStatus.Enumerating, StartedUtc = DateTime.UtcNow });
        var completedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await repository.SetFailureResultAsync(scanJob.Id, foldersScanned: 1, filesFound: 1, errorMessage: null, status: ScanJobStatus.Cancelled, completedUtc: completedUtc);

        var fetched = await repository.GetByIdAsync(scanJob.Id);
        fetched!.Status.Should().Be(ScanJobStatus.Cancelled);
        fetched.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task FailActiveJobsAsync_FailsOnlyEnumeratingAndEnrichingJobs()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var startedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var completedUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var enumerating = new ScanJob { Status = ScanJobStatus.Enumerating, StartedUtc = startedUtc };
        var enriching = new ScanJob { Status = ScanJobStatus.Enriching, StartedUtc = startedUtc };
        var completed = new ScanJob { Status = ScanJobStatus.Completed, StartedUtc = startedUtc, CompletedUtc = startedUtc };
        var failed = new ScanJob { Status = ScanJobStatus.Failed, StartedUtc = startedUtc, CompletedUtc = startedUtc, ErrorMessage = "earlier" };
        db.Context.ScanJobs.AddRange(enumerating, enriching, completed, failed);
        await db.Context.SaveChangesAsync();

        await using (var context = db.CreateContext())
            (await new ScanJobRepository(context).FailActiveJobsAsync("Interrupted by an application restart.", completedUtc)).Should().Be(2);

        await using var verify = db.CreateContext();
        var jobs = await verify.ScanJobs.AsNoTracking().ToDictionaryAsync(j => j.Id);
        jobs[enumerating.Id].Status.Should().Be(ScanJobStatus.Failed);
        jobs[enumerating.Id].ErrorMessage.Should().Be("Interrupted by an application restart.");
        jobs[enumerating.Id].CompletedUtc.Should().Be(completedUtc);
        jobs[enriching.Id].Status.Should().Be(ScanJobStatus.Failed);
        jobs[completed.Id].Status.Should().Be(ScanJobStatus.Completed);
        jobs[failed.Id].ErrorMessage.Should().Be("earlier");
    }
}
