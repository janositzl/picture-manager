using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class ResultHttpExtensionsTests
{
    [Fact]
    public void ToOk_Success_ReturnsOkWithValue()
    {
        var http = Result<string>.Ok("hi").ToOk();

        http.Result.Should().BeOfType<Ok<string>>().Which.Value.Should().Be("hi");
    }

    [Fact]
    public void ToOk_NotFound_ReturnsNotFound()
    {
        Result<string> result = Result.NotFound();

        result.ToOk().Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public void ToOk_Invalid_ReturnsValidationProblemWithFieldErrors()
    {
        Result<string> result = Result.Invalid("limit", "Must be between 1 and 200.");

        var problem = result.ToOk().Result.Should().BeOfType<ValidationProblem>().Subject;
        problem.ProblemDetails.Errors["limit"].Should().Equal("Must be between 1 and 200.");
    }

    [Fact]
    public void ToOk_Conflict_Returns409ProblemDetailsWithMessage()
    {
        Result<string> result = Result.Conflict("Name taken.");

        var conflict = result.ToOk().Result.Should().BeOfType<Conflict<ProblemDetails>>().Subject;
        conflict.Value!.Detail.Should().Be("Name taken.");
        conflict.Value.Status.Should().Be(409);
    }

    [Fact]
    public void ToNoContent_Success_ReturnsNoContent()
    {
        Result.Ok().ToNoContent().Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToNoContent_NotFound_ReturnsNotFound()
    {
        Result.NotFound().ToNoContent().Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public void PatchJson_DistinguishesAbsentNullAndValue()
    {
        var body = JsonDocument.Parse("""{ "name": "x", "alias": null, "isActive": false }""").RootElement;

        PatchJson.TryReadString(body, "name", out var namePresent, out var name).Should().BeTrue();
        namePresent.Should().BeTrue();
        name.Should().Be("x");

        PatchJson.TryReadString(body, "alias", out var aliasPresent, out var alias).Should().BeTrue();
        aliasPresent.Should().BeTrue();
        alias.Should().BeNull();

        PatchJson.TryReadString(body, "description", out var descriptionPresent, out _).Should().BeTrue();
        descriptionPresent.Should().BeFalse();

        PatchJson.TryReadBool(body, "isActive", out var activePresent, out var active).Should().BeTrue();
        activePresent.Should().BeTrue();
        active.Should().BeFalse();
    }

    [Fact]
    public void PatchJson_WrongType_ReturnsFalse()
    {
        var body = JsonDocument.Parse("""{ "name": 5, "isActive": "yes" }""").RootElement;

        PatchJson.TryReadString(body, "name", out _, out _).Should().BeFalse();
        PatchJson.TryReadBool(body, "isActive", out _, out _).Should().BeFalse();
    }
}
