using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using PictureManager.Model;
using PictureManager.Tests.Support;
using Xunit;

namespace PictureManager.Infrastructure.Tests.Persistence;

public class FaceSchemaTests
{
    [Fact]
    public async Task Embedding_RoundTrips_AndCosineKnnIsFilteredByModel()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var modelA = await FaceTestData.AddModelAsync(db.Context, "a");
        var modelB = await FaceTestData.AddModelAsync(db.Context, "b");
        var near = await FaceTestData.AddFaceAsync(db.Context, image.Id, modelA, FaceTestData.Embedding(0, 0.1));
        await FaceTestData.AddFaceAsync(db.Context, image.Id, modelA, FaceTestData.Embedding(5));
        await FaceTestData.AddFaceAsync(db.Context, image.Id, modelB, FaceTestData.Embedding(0));
        var query = new Vector(FaceTestData.Embedding(0));

        await using var read = db.CreateContext();
        var nearest = await read.Faces
            .Where(f => f.FaceModelId == modelA)
            .OrderBy(f => f.Embedding.CosineDistance(query))
            .Select(f => new { f.Id, Distance = f.Embedding.CosineDistance(query) })
            .FirstAsync();

        nearest.Id.Should().Be(near.Id);
        nearest.Distance.Should().BeApproximately(1 - Math.Cos(0.1), 1e-4);
        (await read.Faces.FirstAsync(f => f.Id == near.Id)).Embedding.ToArray()[0].Should().BeApproximately((float)Math.Cos(0.1), 1e-6f);
    }

    [Fact]
    public async Task DeletingImage_CascadesToFacesAndState_AndDeletingPersonUnassignsFaces()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var kept = await FaceTestData.AddImageAsync(db.Context, top, "kept");
        var deleted = await FaceTestData.AddImageAsync(db.Context, top, "deleted");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var person = new Person { Name = "Anna", CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
        db.Context.People.Add(person);
        await db.Context.SaveChangesAsync();
        var keptFace = await FaceTestData.AddFaceAsync(db.Context, kept.Id, model, FaceTestData.Embedding(0), person.Id, FaceAssignmentState.Confirmed);
        await FaceTestData.AddFaceAsync(db.Context, deleted.Id, model, FaceTestData.Embedding(1));
        db.Context.FaceProcessingStates.Add(new FaceProcessingState
        {
            ImageId = deleted.Id, FaceModelId = model, ImageFingerprint = "hash", Status = FaceProcessingStatus.Completed, ProcessedUtc = DateTime.UtcNow
        });
        await db.Context.SaveChangesAsync();

        await db.Context.Images.Where(i => i.Id == deleted.Id).ExecuteDeleteAsync();
        await db.Context.People.Where(p => p.Id == person.Id).ExecuteDeleteAsync();

        await using var read = db.CreateContext();
        (await read.Faces.CountAsync(f => f.ImageId == deleted.Id)).Should().Be(0);
        (await read.FaceProcessingStates.CountAsync()).Should().Be(0);
        (await read.Faces.SingleAsync(f => f.Id == keptFace.Id)).PersonId.Should().BeNull();
    }
}