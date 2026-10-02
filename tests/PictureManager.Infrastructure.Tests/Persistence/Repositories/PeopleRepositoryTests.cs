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
