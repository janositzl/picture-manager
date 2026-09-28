using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using PictureManager.Application.Roots;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class RootEndpointsTests
{
    private readonly IRootService _service = Substitute.For<IRootService>();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task UpdateAsync_AliasNull_IsPassedAsAliasSpecified()
    {
        var summary = new RootSummary(1, "nas", null, "/m", true, "nas");
        _service.UpdateAsync(1, Arg.Any<RootUpdate>(), Arg.Any<CancellationToken>()).Returns(Result<RootSummary>.Ok(summary));

        var result = await RootEndpoints.UpdateAsync(1, Json("""{ "alias": null }"""), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<RootSummary>>();
        await _service.Received(1).UpdateAsync(1, new RootUpdate(null, true, null, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_OmittedAlias_IsNotSpecified()
    {
        _service.UpdateAsync(1, Arg.Any<RootUpdate>(), Arg.Any<CancellationToken>())
            .Returns(Result<RootSummary>.Ok(new RootSummary(1, "x", null, "/m", false, "x")));

        await RootEndpoints.UpdateAsync(1, Json("""{ "name": "x", "isActive": false }"""), _service, CancellationToken.None);

        await _service.Received(1).UpdateAsync(1, new RootUpdate("x", false, null, false), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("""{ "name": 5 }""")]
    [InlineData("""{ "name": null }""")]
    [InlineData("""{ "isActive": "yes" }""")]
    [InlineData("""[1, 2]""")]
    public async Task UpdateAsync_BadBody_ReturnsValidationProblem_WithoutCallingService(string json)
    {
        var result = await RootEndpoints.UpdateAsync(1, Json(json), _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _service.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<RootUpdate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_Conflict_Returns409()
    {
        _service.UpdateAsync(1, Arg.Any<RootUpdate>(), Arg.Any<CancellationToken>()).Returns(Result.Conflict("taken"));

        var result = await RootEndpoints.UpdateAsync(1, Json("""{ "alias": "x" }"""), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Conflict<Microsoft.AspNetCore.Mvc.ProblemDetails>>();
    }

    [Fact]
    public async Task CreateAsync_Success_Returns201()
    {
        var summary = new RootSummary(3, "archive", null, "/m", true, "archive");
        _service.CreateAsync(Arg.Any<RootCreate>(), Arg.Any<CancellationToken>()).Returns(Result<RootSummary>.Ok(summary));

        var result = await RootEndpoints.CreateAsync(
            new RootCreateRequest("archive", "/m", null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Created<RootSummary>>();
        await _service.Received(1).CreateAsync(new RootCreate("archive", "/m", null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_Conflict_Returns409()
    {
        _service.CreateAsync(Arg.Any<RootCreate>(), Arg.Any<CancellationToken>()).Returns(Result.Conflict("taken"));

        var result = await RootEndpoints.CreateAsync(
            new RootCreateRequest("archive", "/m", null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<Conflict<Microsoft.AspNetCore.Mvc.ProblemDetails>>();
    }

    [Fact]
    public async Task CreateAsync_Invalid_ReturnsValidationProblem()
    {
        _service.CreateAsync(Arg.Any<RootCreate>(), Arg.Any<CancellationToken>()).Returns(Result.Invalid("name", "Must not be blank."));

        var result = await RootEndpoints.CreateAsync(
            new RootCreateRequest(null, "/m", null), _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task DeleteAsync_Success_Returns204()
    {
        _service.DeleteAsync(1, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await RootEndpoints.DeleteAsync(1, _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task DeleteAsync_UnknownRoot_Returns404()
    {
        _service.DeleteAsync(99, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        var result = await RootEndpoints.DeleteAsync(99, _service, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound>();
    }
}
