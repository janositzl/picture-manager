using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Common;
using PictureManager.Application.Images;

namespace PictureManager.Api.Endpoints;

public static class ImageQueryEndpoints
{
    public static IEndpointRouteBuilder MapImageQueryEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/images", ListAsync);
        user.MapGet("/images/{id:int}", GetAsync);
        user.MapPut("/images/{id:int}/favorite", SetFavoriteAsync);
        user.MapDelete("/images/{id:int}/favorite", ClearFavoriteAsync);
        return user;
    }

    public static async Task<Results<Ok<PagedResult<ImageListItem>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ListAsync(
        int? folderId, string? folder, string? fileName, bool? favoritesOnly, string? sort, string? order, string? cursor, int? limit,
        IImageQueryService service, CancellationToken cancellationToken)
    {
        var request = new ImageListRequest(folderId, folder, fileName, favoritesOnly ?? false, sort, order, cursor, limit);
        return (await service.ListAsync(request, cancellationToken)).ToOk();
    }

    public static async Task<Results<Ok<ImageDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IImageQueryService service, CancellationToken cancellationToken) =>
        (await service.GetDetailAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> SetFavoriteAsync(
        int id, IImageQueryService service, CancellationToken cancellationToken) =>
        (await service.SetFavoriteAsync(id, true, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ClearFavoriteAsync(
        int id, IImageQueryService service, CancellationToken cancellationToken) =>
        (await service.SetFavoriteAsync(id, false, cancellationToken)).ToNoContent();
}
