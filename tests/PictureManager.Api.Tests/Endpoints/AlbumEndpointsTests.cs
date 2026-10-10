using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Albums;
using PictureManager.Application.Common;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class AlbumEndpointsTests
{
    private readonly IAlbumService _service = Substitute.For<IAlbumService>();
    private static readonly AlbumDetail Detail = new(42, "Summer", null, 0, DateTime.UtcNow, DateTime.UtcNow, "Owner", "Administrator");

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task CreateAsync_Success_Returns201WithLocation()
    {
        _service.CreateAsync(new AlbumCreate("Summer", null), Arg.Any<CancellationToken>()).Returns(Result<AlbumDetail>.Ok(Detail));

        var result = await AlbumEndpoints.CreateAsync(new AlbumCreateRequest("Summer", null), _service, CancellationToken.None);

        var created = result.Result.Should().BeOfType<Created<AlbumDetail>>().Subject;
        created.Location.Should().Be("/api/albums/42");
        created.Value.Should().Be(Detail);
    }

    [Fact]
    public async Task CreateAsync_Conflict_Returns409()
    {
        _service.CreateAsync(Arg.Any<AlbumCreate>(), Arg.Any<CancellationToken>()).Returns(Result.Conflict("taken"));

        var result = await AlbumEndpoints.CreateAsync(new AlbumCreateRequest("Summer", null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Conflict<ProblemDetails>>();
    }

    [Fact]
    public async Task CreateAsync_Invalid_ReturnsValidationProblem()
    {
        _service.CreateAsync(Arg.Any<AlbumCreate>(), Arg.Any<CancellationToken>()).Returns(Result.Invalid("name", "Must not be blank."));

        var result = await AlbumEndpoints.CreateAsync(new AlbumCreateRequest(" ", null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task UpdateAsync_DescriptionNull_IsPassedAsSpecified()
    {
        _service.UpdateAsync(42, Arg.Any<AlbumUpdate>(), Arg.Any<CancellationToken>()).Returns(Result<AlbumDetail>.Ok(Detail));

        await AlbumEndpoints.UpdateAsync(42, Json("""{ "description": null }"""), _service, CancellationToken.None);

        await _service.Received(1).UpdateAsync(42, new AlbumUpdate(null, true, null), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("""{ "name": 3 }""")]
    [InlineData("""{ "name": null }""")]
    [InlineData("""{ "description": false }""")]
    [InlineData("\"text\"")]
    public async Task UpdateAsync_BadBody_ReturnsValidationProblem(string json)
    {
        var result = await AlbumEndpoints.UpdateAsync(42, Json(json), _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _service.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<AlbumUpdate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddImagesAsync_PassesIdsThrough_AndReturnsCounts()
    {
        _service.AddImagesAsync(42, Arg.Any<AlbumAddImages>(), Arg.Any<CancellationToken>())
            .Returns(Result<AlbumAddResult>.Ok(new AlbumAddResult(2, 1)));

        var result = await AlbumEndpoints.AddImagesAsync(42, new AlbumAddImagesRequest(new[] { 1, 2, 3 }, null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<AlbumAddResult>>().Which.Value.Should().Be(new AlbumAddResult(2, 1));
        await _service.Received(1).AddImagesAsync(42,
            Arg.Is<AlbumAddImages>(a => a.ImageIds!.SequenceEqual(new[] { 1, 2, 3 }) && a.FolderId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MoveImageAsync_PassesAnchor_AndReturnsNoContent()
    {
        _service.MoveImageAsync(42, 7, 9, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await AlbumEndpoints.MoveImageAsync(42, 7, new AlbumMoveRequest(9), _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task SortAsync_PassesKey_AndReturnsNoContent()
    {
        _service.SortAsync(42, "dateAsc", Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await AlbumEndpoints.SortAsync(42, new AlbumSortRequest("dateAsc"), _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task SetCoverAsync_PassesImageId_AndReturnsNoContent()
    {
        _service.SetCoverAsync(42, 7, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await AlbumEndpoints.SetCoverAsync(42, new AlbumCoverRequest(7), _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task ExportAsync_ReturnsUtf8TextWithoutBom_AndAlbumFileName()
    {
        _service.ExportAsync(42, "/mnt", Arg.Any<CancellationToken>())
            .Returns(Result<AlbumExport>.Ok(new AlbumExport("Nyaralás 2025.txt", "/mnt/nas/Élet.jpg\n")));

        var result = await AlbumEndpoints.ExportAsync(42, "/mnt", _service, CancellationToken.None);

        var file = result.Result.Should().BeOfType<FileContentHttpResult>().Subject;
        file.ContentType.Should().Be("text/plain; charset=utf-8");
        file.FileDownloadName.Should().Be("Nyaralás 2025.txt");
        var bytes = file.FileContents.ToArray();
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        Encoding.UTF8.GetString(bytes).Should().Be("/mnt/nas/Élet.jpg\n");
    }

    [Fact]
    public async Task ExportAsync_UnknownAlbum_ReturnsNotFound()
    {
        _service.ExportAsync(8, null, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        (await AlbumEndpoints.ExportAsync(8, null, _service, CancellationToken.None)).Result.Should().BeOfType<NotFound>();
    }
}
