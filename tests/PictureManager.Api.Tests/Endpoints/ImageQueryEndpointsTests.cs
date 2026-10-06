using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using PictureManager.Application.Images;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class ImageQueryEndpointsTests
{
    private readonly IImageQueryService _service = Substitute.For<IImageQueryService>();

    [Fact]
    public async Task ListAsync_PassesQueryThrough_AndReturnsOk()
    {
        var page = new PagedResult<ImageListItem>(Array.Empty<ImageListItem>(), null);
        _service.ListAsync(Arg.Any<ImageListRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResult<ImageListItem>>.Ok(page));

        var result = await ImageQueryEndpoints.ListAsync(12, "Madeira", "IMG", true, null, null, null, "name", "desc", "c", 50, _service, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<PagedResult<ImageListItem>>>().Which.Value.Should().BeSameAs(page);
        await _service.Received(1).ListAsync(
            new ImageListRequest(12, "Madeira", "IMG", true, "name", "desc", "c", 50), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_FavoritesOnlyOmitted_MeansFalse()
    {
        _service.ListAsync(Arg.Any<ImageListRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResult<ImageListItem>>.Ok(new PagedResult<ImageListItem>(Array.Empty<ImageListItem>(), null)));

        await ImageQueryEndpoints.ListAsync(null, null, null, null, null, null, null, null, null, null, null, _service, CancellationToken.None);

        await _service.Received(1).ListAsync(new ImageListRequest(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_InvalidRequest_ReturnsValidationProblem()
    {
        _service.ListAsync(Arg.Any<ImageListRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Invalid("limit", "Must be between 1 and 200."));

        var result = await ImageQueryEndpoints.ListAsync(null, null, null, null, null, null, null, null, null, null, 0, _service, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task GetAsync_Unknown_ReturnsNotFound()
    {
        _service.GetDetailAsync(5, Arg.Any<CancellationToken>()).Returns(Result.NotFound());

        var result = await ImageQueryEndpoints.GetAsync(5, _service, CancellationToken.None);

        result.Result.Should().BeOfType<NotFound>();
    }

    [Fact]
    public async Task SetFavoriteAsync_SetsTrue_AndReturnsNoContent()
    {
        _service.SetFavoriteAsync(5, true, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await ImageQueryEndpoints.SetFavoriteAsync(5, _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
    }

    [Fact]
    public async Task ClearFavoriteAsync_SetsFalse_AndReturnsNoContent()
    {
        _service.SetFavoriteAsync(5, false, Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var result = await ImageQueryEndpoints.ClearFavoriteAsync(5, _service, CancellationToken.None);

        result.Result.Should().BeOfType<NoContent>();
        await _service.Received(1).SetFavoriteAsync(5, false, Arg.Any<CancellationToken>());
    }
}
