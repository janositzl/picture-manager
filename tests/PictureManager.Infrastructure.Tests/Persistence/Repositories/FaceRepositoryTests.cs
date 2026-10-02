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

    private static DetectedFace FaceAt(float x, float y, int axis) =>
        new(x, y, 0.2f, 0.2f, 0.95f, 0.8f, FaceTestData.Embedding(axis));

    private static async Task<Face> AddFaceAtAsync(
        PostgresTestDatabase db, int imageId, int modelId, float x, float y, int? personId, FaceAssignmentState state)
    {
        var face = await FaceTestData.AddFaceAsync(db.Context, imageId, modelId, FaceTestData.Embedding(9), personId, state);
        face.X = x;
        face.Y = y;
        face.Width = 0.2f;
        face.Height = 0.2f;
        await db.Context.SaveChangesAsync();
        return face;
    }

    private static async Task<(int ImageId, int Model, int PersonId)> SeedImageWithPersonAsync(PostgresTestDatabase db)
    {
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a", hash: "h");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(anna);
        await db.Context.SaveChangesAsync();
        return (image.Id, model, anna.Id);
    }

    [Fact]
    public async Task SaveResultAsync_Reprocess_ConfirmedFaceWithOverlappingBoxKeepsPersonAndState()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (imageId, model, anna) = await SeedImageWithPersonAsync(db);
        var confirmed = await AddFaceAtAsync(db, imageId, model, 0.1f, 0.1f, anna, FaceAssignmentState.Confirmed);
        await AddFaceAtAsync(db, imageId, model, 0.6f, 0.6f, anna, FaceAssignmentState.Auto); // gone on re-processing

        var saved = await new FaceRepository(db.CreateContext()).SaveResultAsync(
            imageId, model, "h2", new[] { FaceAt(0.11f, 0.1f, 0), FaceAt(0.6f, 0.1f, 1) }, Now);

        saved.Should().BeTrue();
        await using var read = db.CreateContext();
        var faces = await read.Faces.Where(f => f.ImageId == imageId).OrderBy(f => f.X).ToListAsync();
        faces.Should().HaveCount(2).And.NotContain(f => f.Id == confirmed.Id);
        faces[0].Should().BeEquivalentTo(new { X = 0.11f, PersonId = (int?)anna, AssignmentState = FaceAssignmentState.Confirmed });
        faces[1].Should().BeEquivalentTo(new { X = 0.6f, PersonId = (int?)null, AssignmentState = FaceAssignmentState.Unassigned });
    }

    [Fact]
    public async Task SaveResultAsync_Reprocess_RejectedFaceStaysRejected()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (imageId, model, anna) = await SeedImageWithPersonAsync(db);
        await AddFaceAtAsync(db, imageId, model, 0.1f, 0.1f, anna, FaceAssignmentState.Rejected);

        await new FaceRepository(db.CreateContext()).SaveResultAsync(imageId, model, "h2", new[] { FaceAt(0.1f, 0.12f, 0) }, Now);

        await using var read = db.CreateContext();
        (await read.Faces.SingleAsync(f => f.ImageId == imageId))
            .Should().BeEquivalentTo(new { PersonId = (int?)anna, AssignmentState = FaceAssignmentState.Rejected });
    }

    [Fact]
    public async Task SaveResultAsync_Reprocess_ClearlyDifferentBoxDoesNotInherit()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (imageId, model, anna) = await SeedImageWithPersonAsync(db);
        await AddFaceAtAsync(db, imageId, model, 0.1f, 0.1f, anna, FaceAssignmentState.Confirmed);

        // Shifted by half a width: IoU 1/3, below the 0.5 minimum.
        await new FaceRepository(db.CreateContext()).SaveResultAsync(imageId, model, "h2", new[] { FaceAt(0.2f, 0.1f, 0) }, Now);

        await using var read = db.CreateContext();
        (await read.Faces.SingleAsync(f => f.ImageId == imageId))
            .Should().BeEquivalentTo(new { PersonId = (int?)null, AssignmentState = FaceAssignmentState.Unassigned });
    }

    [Fact]
    public async Task SaveResultAsync_Reprocess_TwoNewFacesCompeteForOneOld_OnlyTheBestOverlapInherits()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (imageId, model, anna) = await SeedImageWithPersonAsync(db);
        await AddFaceAtAsync(db, imageId, model, 0.1f, 0.1f, anna, FaceAssignmentState.Confirmed);

        await new FaceRepository(db.CreateContext()).SaveResultAsync(
            imageId, model, "h2", new[] { FaceAt(0.13f, 0.1f, 0), FaceAt(0.11f, 0.1f, 1) }, Now);

        await using var read = db.CreateContext();
        var faces = await read.Faces.Where(f => f.ImageId == imageId).OrderBy(f => f.X).ToListAsync();
        faces.Should().HaveCount(2);
        faces[0].Should().BeEquivalentTo(new { X = 0.11f, PersonId = (int?)anna, AssignmentState = FaceAssignmentState.Confirmed });
        faces[1].Should().BeEquivalentTo(new { X = 0.13f, PersonId = (int?)null, AssignmentState = FaceAssignmentState.Unassigned });
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
