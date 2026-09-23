using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Folders;

namespace PictureManager.Api.Endpoints;

public static class FolderEndpoints
{
    public static void MapFolderEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder admin)
    {
        user.MapGet("/folders/roots", GetRootsAsync);
        user.MapGet("/folders/{id:int}/children", GetChildrenAsync);
        user.MapGet("/folders/{id:int}", GetAsync);

        admin.MapGet("/folders/removed", GetRemovedAsync);
        admin.MapDelete("/folders/{id:int}", RemoveAsync);
        admin.MapPost("/folders/{id:int}/restore", RestoreAsync);
    }

    public static async Task<Ok<IReadOnlyList<FolderNode>>> GetRootsAsync(IFolderService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetRootsAsync(cancellationToken));

    public static async Task<Results<Ok<IReadOnlyList<FolderNode>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetChildrenAsync(
        int id, IFolderService service, CancellationToken cancellationToken) =>
        (await service.GetChildrenAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<Ok<FolderDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IFolderService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToOk();

    public static async Task<Ok<IReadOnlyList<RemovedFolder>>> GetRemovedAsync(IFolderService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetRemovedAsync(cancellationToken));

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RemoveAsync(
        int id, IFolderService service, CancellationToken cancellationToken) =>
        (await service.RemoveAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RestoreAsync(
        int id, IFolderService service, CancellationToken cancellationToken) =>
        (await service.RestoreAsync(id, cancellationToken)).ToNoContent();
}
