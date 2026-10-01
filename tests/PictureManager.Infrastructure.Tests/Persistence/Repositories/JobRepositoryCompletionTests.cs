using System;
using System.Threading.Tasks;
using FluentAssertions;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class JobRepositoryCompletionTests
{
    [Fact]
    public async Task TryMarkCompletedAsync_OnlyFlipsFromTheGivenStatus()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var job = new Job { Kind = JobKind.FaceRecognition, Status = JobStatus.Enriching, StartedUtc = DateTime.UtcNow };
        db.Context.Jobs.Add(job);
        await db.Context.SaveChangesAsync();
        var repository = new JobRepository(db.Context);

        (await repository.TryMarkCompletedAsync(job.Id, JobStatus.Enumerating, DateTime.UtcNow)).Should().BeFalse();
        (await repository.TryMarkCompletedAsync(job.Id, JobStatus.Enriching, DateTime.UtcNow)).Should().BeTrue();

        var reloaded = await repository.GetByIdAsync(job.Id);
        reloaded!.Status.Should().Be(JobStatus.Completed);
        reloaded.CompletedUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task IncrementFaceProgressAsync_AddsOneImageAndTheFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var job = new Job { Kind = JobKind.FaceRecognition, Status = JobStatus.Enriching, StartedUtc = DateTime.UtcNow };
        db.Context.Jobs.Add(job);
        await db.Context.SaveChangesAsync();
        var repository = new JobRepository(db.Context);

        await repository.IncrementFaceProgressAsync(job.Id, 3);
        await repository.IncrementFaceProgressAsync(job.Id, 0);

        var reloaded = await repository.GetByIdAsync(job.Id);
        reloaded!.FilesEnriched.Should().Be(2);
        reloaded.FacesFound.Should().Be(3);
    }
}
