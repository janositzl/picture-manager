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
    public async Task ResetProcessingStatesAsync_ForgetsOnlyTheScopesStates_SoTheyBecomeCandidatesAgain()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var trips = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
        var madeira = await FaceTestData.AddFolderAsync(db.Context, trips, "Madeira");
        var other = await FaceTestData.AddFolderAsync(db.Context, top, "Other");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var inTrips = await FaceTestData.AddImageAsync(db.Context, trips, "a", hash: "h1");
        var inMadeira = await FaceTestData.AddImageAsync(db.Context, madeira, "b", hash: "h2");
        var inOther = await FaceTestData.AddImageAsync(db.Context, other, "c", hash: "h3");
        db.Context.FaceProcessingStates.AddRange(
            new FaceProcessingState { ImageId = inTrips.Id, FaceModelId = model, ImageFingerprint = "h1", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = inMadeira.Id, FaceModelId = model, ImageFingerprint = "h2", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = inOther.Id, FaceModelId = model, ImageFingerprint = "h3", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now });
        await db.Context.SaveChangesAsync();

        (await new FaceRepository(db.CreateContext()).ResetProcessingStatesAsync(trips.Id, isRecursive: false)).Should().Be(1);
        (await new FaceRepository(db.CreateContext()).GetCandidateImageIdsAsync(model, null, isRecursive: true))
            .Should().BeEquivalentTo(new[] { inTrips.Id });

        (await new FaceRepository(db.CreateContext()).ResetProcessingStatesAsync(trips.Id, isRecursive: true)).Should().Be(1);
        (await new FaceRepository(db.CreateContext()).GetCandidateImageIdsAsync(model, null, isRecursive: true))
            .Should().BeEquivalentTo(new[] { inTrips.Id, inMadeira.Id });
        (await new FaceRepository(db.CreateContext()).ResetProcessingStatesAsync(folderId: 999_999, isRecursive: true)).Should().Be(0);
    }

    [Fact]
    public async Task GetCandidateImageIdsAsync_SkipsImagesInOrBeneathAnExcludedFolder()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var trips = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
        var privateFolder = await FaceTestData.AddFolderAsync(db.Context, trips, "Private");
        var nested = await FaceTestData.AddFolderAsync(db.Context, privateFolder, "Nested");      // not flagged itself
        var privateBis = await FaceTestData.AddFolderAsync(db.Context, trips, "Private2");        // prefix trap: stays in scope
        var model = await FaceTestData.AddModelAsync(db.Context);

        var inTrips = await FaceTestData.AddImageAsync(db.Context, trips, "a");
        await FaceTestData.AddImageAsync(db.Context, privateFolder, "b");     // indexed before the exclusion
        await FaceTestData.AddImageAsync(db.Context, nested, "c");
        var inPrivateBis = await FaceTestData.AddImageAsync(db.Context, privateBis, "d");
        privateFolder.IsExcluded = true;
        await db.Context.SaveChangesAsync();
        var repository = new FaceRepository(db.CreateContext());

        var expected = new[] { inTrips.Id, inPrivateBis.Id };
        (await repository.GetCandidateImageIdsAsync(model, trips.Id, isRecursive: true)).Should().BeEquivalentTo(expected);
        (await repository.GetCandidateImageIdsAsync(model, top.Id, isRecursive: true)).Should().BeEquivalentTo(expected);
        (await repository.GetCandidateImageIdsAsync(model, null, isRecursive: true)).Should().BeEquivalentTo(expected);
        (await repository.GetCandidateImageIdsAsync(model, privateFolder.Id, isRecursive: true)).Should().BeEmpty();
        (await repository.GetCandidateImageIdsAsync(model, nested.Id, isRecursive: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetFolderFaceCountsAsync_CountsOwnImages_WithTheCandidateRules()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var trips = await FaceTestData.AddFolderAsync(db.Context, top, "Trips");
        var madeira = await FaceTestData.AddFolderAsync(db.Context, trips, "Madeira");
        var gone = await FaceTestData.AddFolderAsync(db.Context, top, "Gone");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var otherModel = await FaceTestData.AddModelAsync(db.Context, "model-2");

        await FaceTestData.AddImageAsync(db.Context, trips, "new");                                  // never processed
        await FaceTestData.AddImageAsync(db.Context, trips, "notIndexed", IndexState.Pending);       // not counted
        var missing = await FaceTestData.AddImageAsync(db.Context, trips, "missing");                // not counted
        missing.MissingSinceUtc = Now;
        var done = await FaceTestData.AddImageAsync(db.Context, trips, "done", hash: "h1");          // done
        var changed = await FaceTestData.AddImageAsync(db.Context, trips, "changed", hash: "new");   // stale (old fingerprint)
        var retry = await FaceTestData.AddImageAsync(db.Context, trips, "retry", hash: "h2");        // never processed (retryable)
        var givenUp = await FaceTestData.AddImageAsync(db.Context, trips, "gaveup", hash: "h3");     // failed
        var otherDone = await FaceTestData.AddImageAsync(db.Context, madeira, "other", hash: "h4");  // stale (other model)
        var madeiraDone = await FaceTestData.AddImageAsync(db.Context, madeira, "m", hash: "h5");    // done
        await FaceTestData.AddImageAsync(db.Context, gone, "g");                                     // folder missing: not counted
        gone.MissingSinceUtc = Now;
        var excluded = await FaceTestData.AddFolderAsync(db.Context, top, "Private");
        var underExcluded = await FaceTestData.AddFolderAsync(db.Context, excluded, "Nested");
        await FaceTestData.AddImageAsync(db.Context, excluded, "x");                                 // excluded: not counted
        await FaceTestData.AddImageAsync(db.Context, underExcluded, "y");                            // beneath excluded: not counted
        excluded.IsExcluded = true;
        db.Context.FaceProcessingStates.AddRange(
            new FaceProcessingState { ImageId = done.Id, FaceModelId = model, ImageFingerprint = "h1", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = changed.Id, FaceModelId = model, ImageFingerprint = "old", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = retry.Id, FaceModelId = model, ImageFingerprint = "h2", Status = FaceProcessingStatus.Failed, Attempts = 1, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = givenUp.Id, FaceModelId = model, ImageFingerprint = "h3", Status = FaceProcessingStatus.PermanentlyFailed, Attempts = 3, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = otherDone.Id, FaceModelId = otherModel, ImageFingerprint = "h4", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now },
            new FaceProcessingState { ImageId = madeiraDone.Id, FaceModelId = model, ImageFingerprint = "h5", Status = FaceProcessingStatus.Completed, ProcessedUtc = Now });
        await db.Context.SaveChangesAsync();
        var repository = new FaceRepository(db.CreateContext());

        var counts = await repository.GetFolderFaceCountsAsync(model);

        counts.Should().BeEquivalentTo(new[]
        {
            new FolderFaceCounts(top.Id, null, 0, 0, 0, 0),
            new FolderFaceCounts(trips.Id, top.Id, Total: 5, Done: 1, Failed: 1, Stale: 1),
            new FolderFaceCounts(madeira.Id, trips.Id, Total: 2, Done: 1, Failed: 0, Stale: 1),
            new FolderFaceCounts(gone.Id, top.Id, 0, 0, 0, 0),
            new FolderFaceCounts(excluded.Id, top.Id, 0, 0, 0, 0),
            new FolderFaceCounts(underExcluded.Id, excluded.Id, 0, 0, 0, 0),
        });

        // Invariant with the job: a folder's candidates are exactly its images that are neither Done nor Failed.
        var tripsCandidates = await repository.GetCandidateImageIdsAsync(model, trips.Id, isRecursive: false);
        tripsCandidates.Should().HaveCount(5 - 1 - 1);
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
        faces.Should().HaveCount(2).And.OnlyContain(f => f.AssignmentState == FaceAssignmentState.Unknown && f.FaceModelId == model);
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
        await AddFaceAtAsync(db, imageId, model, 0.6f, 0.6f, anna, FaceAssignmentState.Suggested); // gone on re-processing

        var saved = await new FaceRepository(db.CreateContext()).SaveResultAsync(
            imageId, model, "h2", new[] { FaceAt(0.11f, 0.1f, 0), FaceAt(0.6f, 0.1f, 1) }, Now);

        saved.Should().BeTrue();
        await using var read = db.CreateContext();
        var faces = await read.Faces.Where(f => f.ImageId == imageId).OrderBy(f => f.X).ToListAsync();
        faces.Should().HaveCount(2).And.NotContain(f => f.Id == confirmed.Id);
        faces[0].Should().BeEquivalentTo(new { X = 0.11f, PersonId = (int?)anna, AssignmentState = FaceAssignmentState.Confirmed });
        faces[1].Should().BeEquivalentTo(new { X = 0.6f, PersonId = (int?)null, AssignmentState = FaceAssignmentState.Unknown });
    }

    [Fact]
    public async Task SaveResultAsync_Reprocess_IgnoredFaceStaysIgnored()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var (imageId, model, anna) = await SeedImageWithPersonAsync(db);
        await AddFaceAtAsync(db, imageId, model, 0.1f, 0.1f, anna, FaceAssignmentState.Ignored);

        await new FaceRepository(db.CreateContext()).SaveResultAsync(imageId, model, "h2", new[] { FaceAt(0.1f, 0.12f, 0) }, Now);

        await using var read = db.CreateContext();
        (await read.Faces.SingleAsync(f => f.ImageId == imageId))
            .Should().BeEquivalentTo(new { PersonId = (int?)anna, AssignmentState = FaceAssignmentState.Ignored });
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
            .Should().BeEquivalentTo(new { PersonId = (int?)null, AssignmentState = FaceAssignmentState.Unknown });
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
        faces[1].Should().BeEquivalentTo(new { X = 0.13f, PersonId = (int?)null, AssignmentState = FaceAssignmentState.Unknown });
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
