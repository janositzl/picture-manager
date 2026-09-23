using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Common;
using PictureManager.Application.Duplicates;

namespace PictureManager.Api.Endpoints;

public static class DuplicateEndpoints
{
    public static IEndpointRouteBuilder MapDuplicateEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/duplicates", ListAsync);
        return user;
    }

    public static async Task<Results<Ok<PagedResult<DuplicateGroup>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ListAsync(
        string? cursor, int? limit, IDuplicateService service, CancellationToken cancellationToken) =>
        (await service.ListAsync(cursor, limit, cancellationToken)).ToOk();
}
