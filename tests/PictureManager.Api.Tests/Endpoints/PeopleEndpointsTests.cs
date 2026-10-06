using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class PeopleEndpointsTests
{
    private readonly IFaceCropService _crops = Substitute.For<IFaceCropService>();

    [Fact]
    public async Task GetFaceThumbnailAsync_AbsolutePath_ReturnsPhysicalJpegFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
        await File.WriteAllBytesAsync(path, new byte[] { 1 });
        try
        {
            _crops.GetOrCreateCropPathAsync(5, Arg.Any<CancellationToken>()).Returns(path);

            var result = await PeopleEndpoints.GetFaceThumbnailAsync(5, _crops, CancellationToken.None);

            var file = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
            file.FileName.Should().Be(path);
            file.ContentType.Should().Be("image/jpeg");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GetFaceThumbnailAsync_NoCrop_ReturnsNotFound()
    {
        _crops.GetOrCreateCropPathAsync(6, Arg.Any<CancellationToken>()).Returns((string?)null);

        (await PeopleEndpoints.GetFaceThumbnailAsync(6, _crops, CancellationToken.None)).Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task DeleteAsync_ExistingPerson_ReturnsNoContent()
    {
        var people = Substitute.For<IPeopleService>();
        people.DeleteAsync(1, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        (await PeopleEndpoints.DeleteAsync(1, people, CancellationToken.None)).Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task DeleteAsync_MissingPerson_ReturnsNotFound()
    {
        var people = Substitute.For<IPeopleService>();
        people.DeleteAsync(2, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        (await PeopleEndpoints.DeleteAsync(2, people, CancellationToken.None)).Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task IgnoreGroupAsync_ReturnsCount_Or400ForNamedPeople()
    {
        var people = Substitute.For<IPeopleService>();
        people.IgnoreGroupAsync(1, Arg.Any<CancellationToken>()).Returns(Result<CountResponse>.Ok(new CountResponse(3)));
        people.IgnoreGroupAsync(2, Arg.Any<CancellationToken>()).Returns(Result.Invalid("name", "Only unknown groups can be ignored."));

        (await PeopleEndpoints.IgnoreGroupAsync(1, people, CancellationToken.None)).Result
            .Should().BeOfType<Ok<CountResponse>>().Which.Value.Should().Be(new CountResponse(3));
        (await PeopleEndpoints.IgnoreGroupAsync(2, people, CancellationToken.None)).Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task AssignGroupAsync_ReturnsTarget_Or400ForNamedSource()
    {
        var people = Substitute.For<IPeopleService>();
        var anna = new PersonSummary(2, "Anna", 9, 0, 200);
        people.AssignGroupAsync(1, 2, null, Arg.Any<CancellationToken>()).Returns(Result<PersonSummary>.Ok(anna));
        people.AssignGroupAsync(2, 2, null, Arg.Any<CancellationToken>()).Returns(Result.Invalid("name", "Only unknown groups can be assigned to a person."));

        (await PeopleEndpoints.AssignGroupAsync(1, new AssignGroupRequest(2), people, CancellationToken.None)).Result
            .Should().BeOfType<Ok<PersonSummary>>().Which.Value.Should().Be(anna);
        (await PeopleEndpoints.AssignGroupAsync(2, new AssignGroupRequest(2), people, CancellationToken.None)).Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task RecheckFacesAsync_ReturnsCount_OrNotFound()
    {
        var review = Substitute.For<IFaceReviewService>();
        review.RecheckFacesAsync(1, Arg.Any<CancellationToken>()).Returns(Result<CountResponse>.Ok(new CountResponse(2)));
        review.RecheckFacesAsync(2, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        (await PeopleEndpoints.RecheckFacesAsync(1, review, CancellationToken.None)).Result
            .Should().BeOfType<Ok<CountResponse>>().Which.Value.Should().Be(new CountResponse(2));
        (await PeopleEndpoints.RecheckFacesAsync(2, review, CancellationToken.None)).Result.Should().BeOfType<NotFound>();
    }
}
