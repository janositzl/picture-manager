using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using PictureManager.Application.Folders;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class FolderEndpointsTests
{
    private readonly IFolderService _service = Substitute.For<IFolderService>();

    [Fact]
    public async Task GetRootsAsync_ReturnsOk()
    {
        _service.GetRootsAsync(Arg.Any<CancellationToken>()).Returns(new[] { new FolderNode(1, "nas", true, 0, false, false) });

        var result = await FolderEndpoints.GetRootsAsync(_service, CancellationToken.None);

        result.Value.Should().ContainSingle();
    }

    [Fact]
    public async Task GetChildrenAsync_NotFound_Returns404()
    {
        _service.GetChildrenAsync(9, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        (await FolderEndpoints.GetChildrenAsync(9, _service, CancellationToken.None)).Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task RemoveAsync_RootTopFolder_ReturnsValidationProblem()
    {
        _service.RemoveAsync(1, Arg.Any<CancellationToken>()).Returns(Result.Invalid("id", "nope"));

        (await FolderEndpoints.RemoveAsync(1, _service, CancellationToken.None)).Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task RestoreAsync_Success_ReturnsNoContent()
    {
        _service.RestoreAsync(4, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        (await FolderEndpoints.RestoreAsync(4, _service, CancellationToken.None)).Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task SetExcludedAsync_Success_ReturnsNoContent()
    {
        _service.SetExcludedAsync(4, true, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        (await FolderEndpoints.SetExcludedAsync(4, new FolderExclusionRequest(true), _service, CancellationToken.None))
            .Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task SetExcludedAsync_ParentExcluded_ReturnsValidationProblem()
    {
        _service.SetExcludedAsync(4, false, Arg.Any<CancellationToken>()).Returns(Result.Invalid("id", "nope"));

        (await FolderEndpoints.SetExcludedAsync(4, new FolderExclusionRequest(false), _service, CancellationToken.None))
            .Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task SetExcludedAsync_ScanRunning_ReturnsConflict()
    {
        _service.SetExcludedAsync(4, true, Arg.Any<CancellationToken>()).Returns(Result.Conflict("busy"));

        (await FolderEndpoints.SetExcludedAsync(4, new FolderExclusionRequest(true), _service, CancellationToken.None))
            .Result.Should().BeOfType<Conflict<ProblemDetails>>();
    }
}
