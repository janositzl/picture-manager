using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PictureManager.Infrastructure.Persistence.Repositories;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence.Repositories;

public class PeopleRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAllAsync_CountsAssignedFacesAndDistinctPhotos_SkipsEmptyUnnamed()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var a = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var b = await FaceTestData.AddImageAsync(db.Context, top, "b");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        var empty = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(anna, empty);
        await db.Context.SaveChangesAsync();
        await FaceTestData.AddFaceAsync(db.Context, a.Id, model, FaceTestData.Embedding(0), anna.Id, FaceAssignmentState.Confirmed);
        await FaceTestData.AddFaceAsync(db.Context, a.Id, model, FaceTestData.Embedding(1), anna.Id, FaceAssignmentState.Auto);
        await FaceTestData.AddFaceAsync(db.Context, b.Id, model, FaceTestData.Embedding(2), anna.Id, FaceAssignmentState.Auto);
        await FaceTestData.AddFaceAsync(db.Context, b.Id, model, FaceTestData.Embedding(3), anna.Id, FaceAssignmentState.Rejected);

        var people = await new PeopleRepository(db.CreateContext()).GetAllAsync();

        people.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = anna.Id, Name = "Anna", FaceCount = 3, PhotoCount = 2 });
    }

    [Fact]
    public async Task CoverFaceId_StaleCoverFallsBackToBestQualityAssignedFace()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(anna);
        await db.Context.SaveChangesAsync();
        var cover = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), anna.Id, FaceAssignmentState.Auto);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1), anna.Id, FaceAssignmentState.Auto, quality: 0.6f);
        var best = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(2), anna.Id, FaceAssignmentState.Confirmed, quality: 0.8f);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(3), anna.Id, FaceAssignmentState.Rejected, quality: 0.99f);
        anna.CoverFaceId = cover.Id;
        await db.Context.SaveChangesAsync();
        await db.Context.Faces.Where(f => f.Id == cover.Id).ExecuteDeleteAsync();
        var repository = new PeopleRepository(db.CreateContext());

        (await repository.GetAsync(anna.Id))!.CoverFaceId.Should().Be(best.Id);
        (await repository.GetAllAsync()).Should().ContainSingle().Which.CoverFaceId.Should().Be(best.Id);
    }

    [Fact]
    public async Task CoverFaceId_CoverOfAnotherPersonOrRejectedFallsBack_ValidCoverIsKept()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        var bela = new Person { Name = "Bela", CreatedUtc = Now, ModifiedUtc = Now };
        var cili = new Person { Name = "Cili", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(anna, bela, cili);
        await db.Context.SaveChangesAsync();
        var annaCover = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), anna.Id, FaceAssignmentState.Auto, quality: 0.5f);
        await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(1), anna.Id, FaceAssignmentState.Confirmed, quality: 0.9f);
        var belaBest = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(2), bela.Id, FaceAssignmentState.Auto);
        var ciliRejected = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(3), cili.Id, FaceAssignmentState.Rejected);
        var ciliBest = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(4), cili.Id, FaceAssignmentState.Auto);
        anna.CoverFaceId = annaCover.Id; // valid, although not the best quality
        bela.CoverFaceId = annaCover.Id; // merged away / moved to another person
        cili.CoverFaceId = ciliRejected.Id;
        await db.Context.SaveChangesAsync();
        var repository = new PeopleRepository(db.CreateContext());

        (await repository.GetAsync(anna.Id))!.CoverFaceId.Should().Be(annaCover.Id);
        (await repository.GetAsync(bela.Id))!.CoverFaceId.Should().Be(belaBest.Id);
        (await repository.GetAsync(cili.Id))!.CoverFaceId.Should().Be(ciliBest.Id);
    }

    [Fact]
    public async Task CoverFaceId_NoAssignedFaces_IsNull()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now, CoverFaceId = 12345 };
        db.Context.People.Add(anna);
        await db.Context.SaveChangesAsync();
        var repository = new PeopleRepository(db.CreateContext());

        (await repository.GetAsync(anna.Id))!.CoverFaceId.Should().BeNull();
        (await repository.GetAllAsync()).Should().ContainSingle().Which.CoverFaceId.Should().BeNull();
    }

    [Fact]
    public async Task SetNameAsync_NamesAndConfirmsAutoFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var group = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(group);
        await db.Context.SaveChangesAsync();
        var face = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), group.Id, FaceAssignmentState.Auto);

        (await new PeopleRepository(db.CreateContext()).SetNameAsync(group.Id, "Bela", Now)).Should().BeTrue();

        await using var read = db.CreateContext();
        (await read.People.SingleAsync(p => p.Id == group.Id)).Name.Should().Be("Bela");
        (await read.Faces.SingleAsync(f => f.Id == face.Id)).AssignmentState.Should().Be(FaceAssignmentState.Confirmed);
    }

    [Fact]
    public async Task FindIdByNameAsync_IsCaseInsensitive()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(anna);
        await db.Context.SaveChangesAsync();

        (await new PeopleRepository(db.CreateContext()).FindIdByNameAsync("aNNA")).Should().Be(anna.Id);
    }

    [Fact]
    public async Task MergeAsync_MovesFacesAsConfirmed_AndDeletesSource()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var source = new Person { CreatedUtc = Now, ModifiedUtc = Now };
        var target = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(source, target);
        await db.Context.SaveChangesAsync();
        var face = await FaceTestData.AddFaceAsync(db.Context, image.Id, model, FaceTestData.Embedding(0), source.Id, FaceAssignmentState.Auto);

        await new PeopleRepository(db.CreateContext()).MergeAsync(source.Id, target.Id, Now);

        await using var read = db.CreateContext();
        (await read.People.AnyAsync(p => p.Id == source.Id)).Should().BeFalse();
        var moved = await read.Faces.SingleAsync(f => f.Id == face.Id);
        moved.PersonId.Should().Be(target.Id);
        moved.AssignmentState.Should().Be(FaceAssignmentState.Confirmed);
    }

    [Fact]
    public async Task ImageList_PersonFilter_ReturnsOnlyThatPersonsPhotos()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var withAnna = await FaceTestData.AddImageAsync(db.Context, top, "with");
        var rejected = await FaceTestData.AddImageAsync(db.Context, top, "rejected");
        await FaceTestData.AddImageAsync(db.Context, top, "without");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.Add(anna);
        await db.Context.SaveChangesAsync();
        await FaceTestData.AddFaceAsync(db.Context, withAnna.Id, model, FaceTestData.Embedding(0), anna.Id, FaceAssignmentState.Auto);
        await FaceTestData.AddFaceAsync(db.Context, rejected.Id, model, FaceTestData.Embedding(1), anna.Id, FaceAssignmentState.Rejected);
        var repository = new ImageQueryRepository(db.CreateContext());

        var rows = await repository.ListAsync(
            new PictureManager.Application.Images.ImageListFilter(null, null, null, false, anna.Id),
            PictureManager.Application.Images.ImageSort.Name, PictureManager.Application.Images.SortDirection.Asc, null, 10);

        rows.Select(r => r.Id).Should().Equal(withAnna.Id);
    }
}
