using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Common;
using PictureManager.Application.Roots;

namespace PictureManager.Api.Endpoints;

public sealed record RootCreateRequest(string? Name, string? MountPath, string? Alias);

public static class RootEndpoints
{
    public static IEndpointRouteBuilder MapRootEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/roots", GetAllAsync);
        admin.MapPost("/roots", CreateAsync);
        admin.MapPatch("/roots/{id:int}", UpdateAsync);
        admin.MapDelete("/roots/{id:int}", DeleteAsync);
        return admin;
    }

    public static async Task<Ok<IReadOnlyList<RootSummary>>> GetAllAsync(IRootService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAllAsync(cancellationToken));

    public static async Task<Results<Created<RootSummary>, ValidationProblem, Conflict<ProblemDetails>>> CreateAsync(
        RootCreateRequest body, IRootService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new RootCreate(body.Name, body.MountPath, body.Alias), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Success => TypedResults.Created($"/api/roots/{result.Value!.Id}", result.Value),
            ResultStatus.Conflict => TypedResults.Conflict(ResultHttpExtensions.ConflictProblem(result.Message)),
            _ => TypedResults.ValidationProblem(ResultHttpExtensions.ToErrors(result.Errors))
        };
    }

    public static async Task<Results<Ok<RootSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateAsync(
        int id, JsonElement body, IRootService service, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ResultHttpExtensions.Invalid("body", "Expected a JSON object.");
        if (!PatchJson.TryReadString(body, "name", out var namePresent, out var name) || (namePresent && name is null))
            return ResultHttpExtensions.Invalid("name", "Must be a string.");
        if (!PatchJson.TryReadString(body, "alias", out var aliasPresent, out var alias))
            return ResultHttpExtensions.Invalid("alias", "Must be a string or null.");
        if (!PatchJson.TryReadBool(body, "isActive", out var activePresent, out var isActive) || (activePresent && isActive is null))
            return ResultHttpExtensions.Invalid("isActive", "Must be true or false.");

        var update = new RootUpdate(name, aliasPresent, alias, isActive);
        return (await service.UpdateAsync(id, update, cancellationToken)).ToOk();
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> DeleteAsync(
        int id, IRootService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToNoContent();
}
