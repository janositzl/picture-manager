using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Faces;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FaceRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static DetectedFace Face(int axis) =>
        new(0.1f, 0.2f, 0.3f, 0.3f, 0.95f, 0.8f, FaceTestData.Embedding(axis));

    [Fact]
    public async Task GetOrCreateModelIdAsync_IsIdempotentByHash()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var repository = new FaceRepository(db.Context);
        var descriptor = new FaceModelDescriptor("insightface-buffalo_l", "v", 512, "abc");

        var first = await repository.GetOrCreateModelIdAsync(descriptor, Now);
        var second = await repository.GetOrCreateModelIdAsync(descriptor, Now);

        second.Should().Be(first);
        (await db.Context.FaceModels.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetCandidateImageIdsAsync_AppliesIndexStateVisibilityScopeAndState()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var trips = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
        var madeira = await FaceTestData.AddFolderAsync(db.Context, trips, "Madeira");
        var tripsBis = await FaceTestData.AddFolderAsync(db.Context, top, "Trips2"); // prefix trap: "Trips" must not match "Trips2"
        var model = await FaceTestData.AddModelAsync(db.Context);

        var inTrips = await FaceTestData.AddImageAsync(db.Context, trips, "a");
        var inMadeira = await FaceTestData.AddImageAsync(db.Context, madeira, "b");
        var inTripsBis = await FaceTestData.AddImageAsync(db.Context, tripsBis, "c");
        await FaceTestData.AddImageAsync(db.Context, trips, "pending", IndexState.Pending);
        var missing = await FaceTestData.AddImageAsync(db.Context, trips, "missing");
        missing.MissingSinceUtc = Now;
        var done = await FaceTestData.AddImageAsync(db.Context, trips, "done", hash: "h1");
        var changed = await FaceTestData.AddImageAsync(db.Context, trips, "changed", hash: "new");
        var failed = await FaceTestData.AddImageAsync(db.Context, trips, "failed", hash: "h2");
        var givenUp = await FaceTestData.AddImageAsync(db.Context, trips, "gaveup", hash: "h3");
        db.Context.FaceProcessingStates.AddRange(
            new FaceProcessingState { ImageId = done.Id, FaceModelId = model, ImageFingerprint = "h1", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = changed.Id, FaceModelId = model, ImageFingerprint = "old", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = failed.Id, FaceModelId = model, ImageFingerprint = "h2", Status = FaceProcessingStatus.Failed, Attempts = 1, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = givenUp.Id, FaceModelId = model, ImageFingerprint = "h3", Status = FaceProcessingStatus.PermanentlyFailed, Attempts = 3, ProcessedUtc = Now });
        await db.Context.SaveChangesAsync();
        var repository = new FaceRepository(db.CreateContext());

        (await repository.GetCandidateImageIdsAsync(model, trips.Id, isRecursive: true))
            .Should().BeEquivalentTo(new[] { inTrips.Id, inMadeira.Id, changed.Id, failed.Id });
        (await repository.GetCandidateImageIdsAsync(model, trips.Id, isRecursive: false))
            .Should().BeEquivalentTo(new[] { inTrips.Id, changed.Id, failed.Id });
        (await repository.GetCandidateImageIdsAsync(model, null, isRecursive: true))
            .Should().BeEquivalentTo(new[] { inTrips.Id, inMadeira.Id, inTripsBis.Id, changed.Id, failed.Id });
        (await repository.GetCandidateImageIdsAsync(model, top.Id, isRecursive: true))
            .Should().HaveCount(5);
        (await repository.GetCandidateImageIdsAsync(model, folderId: 999_999, isRecursive: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveResultAsync_ReplacesFacesAndRecordsCompletedState()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a", hash: "h");
        var model = await FaceTestData.AddModelAsync(db.Context);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(9));
        var repository = new FaceRepository(db.CreateContext());

        (await repository.SaveResultAsync(image.Id, model, "h", new[] { Face(0), Face(1) }, Now)).Should().BeTrue();

        await using var read = db.CreateContext();
        var faces = await read.Faces.Where(f => f.ImageId == image.Id).ToListAsync();
        faces.Should().HaveCount(2).And.OnlyContain(f => f.AssignmentState == FaceAssignmentState.Unassigned && f.FaceModelId == model);
        var state = await read.FaceProcessingStates.SingleAsync(s => s.ImageId == image.Id);
        state.Status.Should().Be(FaceProcessingStatus.Completed);
        state.ImageFingerprint.Should().Be("h");
        state.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task SaveResultAsync_NoFaces_RecordsCompletedState()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a", hash: "h");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var repository = new FaceRepository(db.CreateContext());

        (await repository.SaveResultAsync(image.Id, model, "h", Array.Empty<DetectedFace>(), Now)).Should().BeTrue();

        (await new FaceRepository(db.CreateContext()).GetCandidateImageIdsAsync(model, null, true)).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveResultAsync_ImageDeleted_ReturnsFalse()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        await db.Context.Images.Where(i => i.Id == image.Id).ExecuteDeleteAsync();

        var saved = await new FaceRepository(db.CreateContext()).SaveResultAsync(image.Id, model, "h", new[] { Face(0) }, Now);

        saved.Should().BeFalse();
    }

    [Fact]
    public async Task SaveFailureAsync_UpsertsState_AndPermanentFailuresAreListed()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "broken", hash: "h");
        var model = await FaceTestData.AddModelAsync(db.Context);

        await new FaceRepository(db.CreateContext()).SaveFailureAsync(image.Id, model, "h", FaceProcessingStatus.Failed, 1, "io", Now);
        await new FaceRepository(db.CreateContext()).SaveFailureAsync(image.Id, model, "h", FaceProcessingStatus.PermanentlyFailed, 2, "undecodable", Now);

        var failures = await new FaceRepository(db.CreateContext()).GetPermanentFailuresAsync(model);
        failures.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ImageId = image.Id, FileName = "broken", Attempts = 2, ErrorMessage = "undecodable" });
    }
}
