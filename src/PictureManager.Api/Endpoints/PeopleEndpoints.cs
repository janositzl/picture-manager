using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;

namespace PictureManager.Api.Endpoints;

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/people", GetAllAsync);
        user.MapGet("/people/{id:int}", GetAsync);
        user.MapPatch("/people/{id:int}", NameAsync);
        user.MapGet("/faces/{id:int}/thumbnail", GetFaceThumbnailAsync);
        return user;
    }

    public static async Task<Ok<IReadOnlyList<PersonSummary>>> GetAllAsync(IPeopleService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAllAsync(cancellationToken));

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> NameAsync(
        int id, PersonNameRequest request, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.NameAsync(id, request.Name, cancellationToken)).ToOk();

    public static async Task<IResult> GetFaceThumbnailAsync(int id, IFaceCropService crops, CancellationToken cancellationToken)
    {
        var path = await crops.GetOrCreateCropPathAsync(id, cancellationToken);
        return path is null ? Results.NotFound() : Results.File(path, "image/jpeg");
    }
}

public sealed record PersonNameRequest(string? Name);
