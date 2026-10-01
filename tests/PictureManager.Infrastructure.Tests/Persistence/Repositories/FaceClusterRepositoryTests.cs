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
        var assignedFar = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.5), person.Id, FaceAssignmentState.Auto);
        var unassignedNear = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.05));
        await FaceTestData.AddFaceAsync(db.Context, image.Id, other, FaceTestData.Embedding(0), person.Id, FaceAssignmentState.Confirmed);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0, 0.01), person.Id, FaceAssignmentState.Rejected);
        var repository = new FaceRepository(db.CreateContext());

        var assigned = await repository.GetNearestAsync(query.Id, model, NeighborPool.Assigned, 5);
        var unassigned = await repository.GetNearestAsync(query.Id, model, NeighborPool.Unassigned, 5);

        assigned.Select(n => n.FaceId).Should().Equal(assignedNear.Id, assignedFar.Id);
        assigned[0].PersonId.Should().Be(person.Id);
        unassigned.Select(n => n.FaceId).Should().Equal(unassignedNear.Id);
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
        assigned.AssignmentState.Should().Be(FaceAssignmentState.Auto);
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
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), group.Id, FaceAssignmentState.Auto);

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
}
