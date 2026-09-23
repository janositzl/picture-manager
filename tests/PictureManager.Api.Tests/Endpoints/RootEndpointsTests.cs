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
}
