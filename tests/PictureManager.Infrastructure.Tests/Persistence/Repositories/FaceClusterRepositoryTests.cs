using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Application.Repositories;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class FaceClusterRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetNearestAsync_FiltersByModelAndPool_OrderedByDistance()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context, "a");
        var other = await FaceTestData.AddModelAsync(db.Context, "b");
        var person = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(person);
        await db.Context.SaveChangesAsync();
        var query = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0));
        var assignedNear = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.1), person.Id, FaceAssignmentState.Confirmed);
        var assignedFar = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.5), person.Id, FaceAssignmentState.Suggested);
        var unassignedNear = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.05));
        await FaceTestData.AddFaceAsync(db.Context, image.Id, other, FaceTestData.Embedding(0), person.Id, FaceAssignmentState.Confirmed);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.01), person.Id, FaceAssignmentState.Ignored);
        var repository = new FaceRepository(db.CreateContext());

        var assigned = await repository.GetNearestAsync(query.Id, model, NeighborPool.Assigned, 5);
        var unassigned = await repository.GetNearestAsync(query.Id, model, NeighborPool.Unassigned, 5);

        assigned.Select(n => n.FaceId).Should().Equal(assignedNear.Id, assignedFar.Id);
        assigned[0].PersonId.Should().Be(person.Id);
        unassigned.Select(n => n.FaceId).Should().Equal(unassignedNear.Id);
        // Cosine distance between unit vectors tilted by t is 1 - cos(t) (an L2 distance would be 2 sin(t/2)).
        assigned[0].Distance.Should().BeApproximately((float)(1 - Math.Cos(0.1)), 1e-5f);
        assigned[1].Distance.Should().BeApproximately((float)(1 - Math.Cos(0.5)), 1e-5f);
        unassigned[0].Distance.Should().BeApproximately((float)(1 - Math.Cos(0.05)), 1e-5f);
    }

    [Fact]
    public async Task GetNearestAsync_MinQuality_IsAppliedBeforeTheLimit()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var query = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0));
        var blurryNear = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.01), quality: 0.2f);
        var sharpFar = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.2), quality: 0.6f);
        var repository = new FaceRepository(db.CreateContext());

        (await repository.GetNearestAsync(query.Id, model, NeighborPool.Unassigned, 1, minQuality: 0.5f))
            .Should().ContainSingle().Which.FaceId.Should().Be(sharpFar.Id);
        (await repository.GetNearestAsync(query.Id, model, NeighborPool.Unassigned, 1))
            .Should().ContainSingle().Which.FaceId.Should().Be(blurryNear.Id);
    }

    [Fact]
    public async Task GetNearestAsync_ForcedIndexScan_StillReturnsKWhenTheNearestFacesBelongToAnotherModel()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context, "a");
        var other = await FaceTestData.AddModelAsync(db.Context, "b");
        var query = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0));
        // 300 faces of the other model crowd the query's neighbourhood; this model's faces are all farther away.
        db.Context.Faces.AddRange(Enumerable.Range(0, 300).Select(i => NewFace(image.Id, other, FaceTestData.Embedding(0, 0.0001 * i))));
        db.Context.Faces.AddRange(Enumerable.Range(0, 10).Select(i => NewFace(image.Id, model, FaceTestData.Embedding(0, 0.3 + 0.01 * i))));
        await db.Context.SaveChangesAsync();

        // Make the planner use the HNSW index like it would on a big table (tiny tables are scanned sequentially).
        await using var context = db.CreateContext();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET enable_seqscan = off; SET enable_sort = off");

        var neighbors = await new FaceRepository(context).GetNearestAsync(query.Id, model, NeighborPool.Unassigned, 5);

        neighbors.Should().HaveCount(5);
        neighbors.Select(n => n.Distance).Should().BeInAscendingOrder();
        neighbors[0].Distance.Should().BeApproximately((float)(1 - Math.Cos(0.3)), 1e-4f);
    }

    private static Face NewFace(int imageId, int modelId, float[] embedding, float quality = 0.9f) => new()
    {
        ImageId = imageId,
        FaceModelId = modelId,
        X = 0.1f, Y = 0.1f, Width = 0.2f, Height = 0.2f,
        DetectionConfidence = 0.9f,
        QualityScore = quality,
        Embedding = new Pgvector.Vector(embedding),
        CreatedUtc = Now
    };

    [Fact]
    public async Task AssignAsync_SkipsFaceThatRejectedThatPerson_AndStoresMatchDistance()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(anna);
        await db.Context.SaveChangesAsync();
        var rejected = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0));
        var free = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1));
        rejected.RejectedPersonId = anna.Id;
        await db.Context.SaveChangesAsync();

        await new FaceRepository(db.CreateContext()).AssignAsync(new[] { rejected.Id, free.Id }, anna.Id, 0.2f);

        await using var read = db.CreateContext();
        var skipped = await read.Faces.SingleAsync(f => f.Id == rejected.Id);
        skipped.PersonId.Should().BeNull();
        skipped.AssignmentState.Should().Be(FaceAssignmentState.Unknown);
        (await read.Faces.SingleAsync(f => f.Id == free.Id)).Should().BeEquivalentTo(
            new { PersonId = (int?)anna.Id, AssignmentState = FaceAssignmentState.Suggested, MatchDistance = (float?)0.2f });
    }

    [Fact]
    public async Task AssignAsync_OnlyTouchesUnassignedFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var named = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        var group = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(named, group);
        await db.Context.SaveChangesAsync();
        var confirmed = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), named.Id, FaceAssignmentState.Confirmed);
        var free = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1));
        var repository = new FaceRepository(db.CreateContext());

        await repository.AssignAsync(new[] { confirmed.Id, free.Id }, group.Id);

        await using var read = db.CreateContext();
        (await read.Faces.SingleAsync(f => f.Id == confirmed.Id)).PersonId.Should().Be(named.Id);
        var assigned = await read.Faces.SingleAsync(f => f.Id == free.Id);
        assigned.PersonId.Should().Be(group.Id);
        assigned.AssignmentState.Should().Be(FaceAssignmentState.Suggested);
    }

    [Fact]
    public async Task DeleteEmptyUnnamedPeopleAsync_KeepsNamedPeopleAndNonEmptyGroups()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var emptyNamed = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        var emptyGroup = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        var group = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(emptyNamed, emptyGroup, group);
        await db.Context.SaveChangesAsync();
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), group.Id, FaceAssignmentState.Suggested);

        var deleted = await new FaceRepository(db.CreateContext()).DeleteEmptyUnnamedPeopleAsync();

        deleted.Should().Be(1);
        await using var read = db.CreateContext();
        (await read.People.Select(p => p.Id).ToListAsync()).Should().BeEquivalentTo(new[] { emptyNamed.Id, group.Id });
    }

    [Fact]
    public async Task GetUnassignedFacesAsync_FiltersByQualityAndModel()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context, "a");
        var other = await FaceTestData.AddModelAsync(db.Context, "b");
        var good = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), quality: 0.8f);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1), quality: 0.2f);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, other, FaceTestData.Embedding(2), quality: 0.9f);

        var faces = await new FaceRepository(db.CreateContext()).GetUnassignedFacesAsync(model, 0.5f);

        faces.Should().ContainSingle().Which.Id.Should().Be(good.Id);
    }

    [Fact]
    public async Task GetUnclusteredFacesAsync_OnlyUnassignedNeverClusteredFacesOfTheModel()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context, "a");
        var other = await FaceTestData.AddModelAsync(db.Context, "b");
        var person = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(person);
        await db.Context.SaveChangesAsync();
        var fresh = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), quality: 0.8f);
        var freshBlurry = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1), quality: 0.2f);
        var oldNoise = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(2), quality: 0.9f);
        oldNoise.ClusteredUtc = Now;
        await db.Context.SaveChangesAsync();
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(3), person.Id, FaceAssignmentState.Suggested);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, other, FaceTestData.Embedding(4));
        var repository = new FaceRepository(db.CreateContext());

        (await repository.GetUnclusteredFacesAsync(model, 0f)).Select(f => f.Id).Should().Equal(fresh.Id, freshBlurry.Id);
        (await repository.GetUnclusteredFacesAsync(model, 0.5f)).Select(f => f.Id).Should().Equal(fresh.Id);
    }

    [Fact]
    public async Task MarkClusteredAsync_StampsOnlyTheGivenFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var first = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0));
        var second = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1));
        var untouched = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(2));

        await new FaceRepository(db.CreateContext()).MarkClusteredAsync(new[] { first.Id, second.Id }, Now);

        await using var read = db.CreateContext();
        var stamps = await read.Faces.ToDictionaryAsync(f => f.Id, f => f.ClusteredUtc);
        stamps[first.Id].Should().Be(Now);
        stamps[second.Id].Should().Be(Now);
        stamps[untouched.Id].Should().BeNull();
        (await new FaceRepository(db.CreateContext()).GetUnclusteredFacesAsync(model, 0f)).Select(f => f.Id).Should().Equal(untouched.Id);
    }
}
