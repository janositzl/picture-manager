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

public class FaceReviewRepositoryTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Seed(PostgresTestDatabase Db, int ImageId, int ModelId, Person Anna, Person Bela);

    private static async Task<Seed> SeedAsync()
    {
        var db = await PostgresTestDatabase.CreateAsync();
        var top = await FaceTestData.SeedRootAsync(db.Context);
        var image = await FaceTestData.AddImageAsync(db.Context, top, "a");
        var model = await FaceTestData.AddModelAsync(db.Context);
        var anna = new Person { Name = "Anna", CreatedUtc = Now, ModifiedUtc = Now };
        var bela = new Person { Name = "Bela", CreatedUtc = Now, ModifiedUtc = Now };
        db.Context.People.AddRange(anna, bela);
        await db.Context.SaveChangesAsync();
        return new Seed(db, image.Id, model, anna, bela);
    }

    private static Task<Face> AddAsync(Seed s, int index, int? personId, FaceAssignmentState state) =>
        FaceTestData.AddFaceAsync(s.Db.Context, s.ImageId, s.ModelId, FaceTestData.Embedding(index), personId, state);

    private static async Task<Face> ReadAsync(Seed s, int faceId)
    {
        await using var read = s.Db.CreateContext();
        return await read.Faces.AsNoTracking().SingleAsync(f => f.Id == faceId);
    }

    [Fact]
    public async Task GetUnknownFaces_ReturnsOnlyUnknownFacesOfThatImage_AndNullForAMissingImage()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var unknown = await AddAsync(s, 0, null, FaceAssignmentState.Unknown);
        await AddAsync(s, 1, s.Anna.Id, FaceAssignmentState.Suggested);
        await AddAsync(s, 2, null, FaceAssignmentState.Ignored);
        var repository = new FaceReviewRepository(s.Db.CreateContext());

        var faces = await repository.GetUnknownFacesAsync(s.ImageId);

        faces!.Select(f => f.Id).Should().Equal(unknown.Id);
        faces![0].FaceModelId.Should().Be(s.ModelId);
        (await repository.GetUnknownFacesAsync(999_999)).Should().BeNull();
    }

    [Fact]
    public async Task Accept_ConfirmsOnlyASuggestedFace()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var suggested = await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Suggested);
        var ignored = await AddAsync(s, 1, null, FaceAssignmentState.Ignored);
        var repository = new FaceReviewRepository(s.Db.CreateContext());

        (await repository.AcceptAsync(suggested.Id)).Should().Be(1);
        (await repository.AcceptAsync(ignored.Id)).Should().Be(0);

        (await ReadAsync(s, suggested.Id)).AssignmentState.Should().Be(FaceAssignmentState.Confirmed);
        (await ReadAsync(s, ignored.Id)).AssignmentState.Should().Be(FaceAssignmentState.Ignored);
    }

    [Fact]
    public async Task Reject_GoesBackToUnknown_AndRemembersThePerson()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var face = await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Suggested);

        await new FaceReviewRepository(s.Db.CreateContext()).RejectAsync(face.Id);

        (await ReadAsync(s, face.Id)).Should().BeEquivalentTo(new
        {
            PersonId = (int?)null, RejectedPersonId = (int?)s.Anna.Id, AssignmentState = FaceAssignmentState.Unknown
        });
    }

    [Fact]
    public async Task Reject_LeavesAConfirmedFaceAlone_ButMarkUnknownTakesItBack()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var face = await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Confirmed);
        var repository = new FaceReviewRepository(s.Db.CreateContext());

        (await repository.RejectAsync(face.Id)).Should().Be(0);
        (await ReadAsync(s, face.Id)).AssignmentState.Should().Be(FaceAssignmentState.Confirmed);

        (await repository.MarkUnknownAsync(face.Id)).Should().Be(1);
        (await ReadAsync(s, face.Id)).Should().BeEquivalentTo(new
        {
            PersonId = (int?)null, RejectedPersonId = (int?)s.Anna.Id, AssignmentState = FaceAssignmentState.Unknown
        });
    }

    [Fact]
    public async Task Assign_ConfirmsForThePerson_AndRejectsTheSuggestedOne()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var face = await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Suggested);

        await new FaceReviewRepository(s.Db.CreateContext()).AssignAsync(face.Id, s.Bela.Id, Now);

        (await ReadAsync(s, face.Id)).Should().BeEquivalentTo(new
        {
            PersonId = (int?)s.Bela.Id, RejectedPersonId = (int?)s.Anna.Id, AssignmentState = FaceAssignmentState.Confirmed
        });
    }

    [Fact]
    public async Task Assign_ToTheRejectedPerson_ForgetsTheRejection_AndIgnoredFacesAreUntouched()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var face = await AddAsync(s, 0, null, FaceAssignmentState.Unknown);
        var ignored = await AddAsync(s, 1, null, FaceAssignmentState.Ignored);
        await using (var write = s.Db.CreateContext())
        {
            await write.Faces.Where(f => f.Id == face.Id).ExecuteUpdateAsync(u => u.SetProperty(f => f.RejectedPersonId, s.Anna.Id));
        }
        var repository = new FaceReviewRepository(s.Db.CreateContext());

        (await repository.AssignAsync(face.Id, s.Anna.Id, Now)).Should().Be(1);
        (await repository.AssignAsync(ignored.Id, s.Anna.Id, Now)).Should().Be(0);

        (await ReadAsync(s, face.Id)).Should().BeEquivalentTo(new
        {
            PersonId = (int?)s.Anna.Id, RejectedPersonId = (int?)null, AssignmentState = FaceAssignmentState.Confirmed
        });
        (await ReadAsync(s, ignored.Id)).AssignmentState.Should().Be(FaceAssignmentState.Ignored);
    }

    [Fact]
    public async Task Ignore_DropsThePerson_AndRestoreMakesItUnknown()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var face = await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Confirmed);
        var repository = new FaceReviewRepository(s.Db.CreateContext());

        (await repository.IgnoreAsync(face.Id)).Should().Be(1);
        (await ReadAsync(s, face.Id)).Should().BeEquivalentTo(new { PersonId = (int?)null, AssignmentState = FaceAssignmentState.Ignored });

        (await repository.RestoreAsync(face.Id)).Should().Be(1);
        (await ReadAsync(s, face.Id)).AssignmentState.Should().Be(FaceAssignmentState.Unknown);
    }

    [Fact]
    public async Task AcceptAll_ConfirmsEverySuggestionOfThePersonOnly()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var mine = await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Suggested);
        var theirs = await AddAsync(s, 1, s.Bela.Id, FaceAssignmentState.Suggested);

        (await new FaceReviewRepository(s.Db.CreateContext()).AcceptAllAsync(s.Anna.Id)).Should().Be(1);

        (await ReadAsync(s, mine.Id)).AssignmentState.Should().Be(FaceAssignmentState.Confirmed);
        (await ReadAsync(s, theirs.Id)).AssignmentState.Should().Be(FaceAssignmentState.Suggested);
    }

    [Fact]
    public async Task RejectImage_OnlyAffectsThatPersonsSuggestionInThatImage()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        var annas = await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Suggested);
        var belas = await AddAsync(s, 1, s.Bela.Id, FaceAssignmentState.Suggested);

        (await new FaceReviewRepository(s.Db.CreateContext()).RejectImageAsync(s.Anna.Id, s.ImageId)).Should().Be(1);

        (await ReadAsync(s, annas.Id)).Should().BeEquivalentTo(new { AssignmentState = FaceAssignmentState.Unknown, RejectedPersonId = (int?)s.Anna.Id });
        (await ReadAsync(s, belas.Id)).AssignmentState.Should().Be(FaceAssignmentState.Suggested);
    }

    [Fact]
    public async Task GetImageFaces_HidesIgnoredUnlessAsked_AndNullForAnUnknownImage()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Confirmed);
        await AddAsync(s, 1, null, FaceAssignmentState.Ignored);
        var repository = new FaceReviewRepository(s.Db.CreateContext());

        (await repository.GetImageFacesAsync(s.ImageId, includeIgnored: false))!.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { PersonName = "Anna", State = FaceAssignmentState.Confirmed });
        (await repository.GetImageFacesAsync(s.ImageId, includeIgnored: true))!.Should().HaveCount(2);
        (await repository.GetImageFacesAsync(9999, includeIgnored: true)).Should().BeNull();
    }

    [Fact]
    public async Task HiddenImage_FacesAreListedForTheViewer_ButNotOfferedForRecheck()
    {
        var s = await SeedAsync();
        await using var _ = s.Db;
        await AddAsync(s, 0, s.Anna.Id, FaceAssignmentState.Confirmed);
        await AddAsync(s, 1, null, FaceAssignmentState.Unknown);
        await s.Db.Context.Images.Where(i => i.Id == s.ImageId).ExecuteUpdateAsync(u => u.SetProperty(i => i.IsHidden, true));
        var repository = new FaceReviewRepository(s.Db.CreateContext());

        (await repository.GetImageFacesAsync(s.ImageId, includeIgnored: false))!.Should().HaveCount(2);
        (await repository.GetUnknownFacesAsync(s.ImageId)).Should().BeNull();
    }
}
