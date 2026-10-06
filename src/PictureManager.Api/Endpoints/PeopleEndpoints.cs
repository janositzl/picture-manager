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
        user.MapDelete("/people/{id:int}", DeleteAsync);
        user.MapPost("/people/{id:int}/ignore", IgnoreGroupAsync);
        user.MapGet("/faces/{id:int}/thumbnail", GetFaceThumbnailAsync);

        user.MapGet("/images/{id:int}/faces", GetImageFacesAsync);
        user.MapPost("/faces/{id:int}/accept", AcceptAsync);
        user.MapPost("/faces/{id:int}/reject", RejectAsync);
        user.MapPost("/faces/{id:int}/unknown", MarkUnknownAsync);
        user.MapPost("/faces/{id:int}/ignore", IgnoreAsync);
        user.MapPost("/faces/{id:int}/restore", RestoreAsync);
        user.MapPost("/faces/{id:int}/assign", AssignAsync);
        user.MapPost("/people/{id:int}/suggestions/accept", AcceptAllAsync);
        user.MapPost("/people/{id:int}/images/{imageId:int}/accept", AcceptImageAsync);
        user.MapPost("/people/{id:int}/images/{imageId:int}/reject", RejectImageAsync);
        return user;
    }

    public static async Task<Results<Ok<IReadOnlyList<ImageFaceDto>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetImageFacesAsync(
        int id, bool? includeIgnored, bool? confirmedOnly, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.GetImageFacesAsync(id, includeIgnored ?? false, confirmedOnly ?? false, cancellationToken)).ToOk();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> AcceptAsync(
        int id, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.AcceptAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RejectAsync(
        int id, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.RejectAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> MarkUnknownAsync(
        int id, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.MarkUnknownAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> IgnoreAsync(
        int id, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.IgnoreAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RestoreAsync(
        int id, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.RestoreAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> AssignAsync(
        int id, AssignFaceRequest request, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.AssignAsync(id, request, cancellationToken)).ToOk();

    public static async Task<Results<Ok<CountResponse>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> AcceptAllAsync(
        int id, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.AcceptAllAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> AcceptImageAsync(
        int id, int imageId, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.AcceptImageAsync(id, imageId, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RejectImageAsync(
        int id, int imageId, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.RejectImageAsync(id, imageId, cancellationToken)).ToNoContent();

    public static async Task<Ok<IReadOnlyList<PersonSummary>>> GetAllAsync(IPeopleService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAllAsync(cancellationToken));

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> NameAsync(
        int id, PersonNameRequest request, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.NameAsync(id, request.Name, cancellationToken)).ToOk();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> DeleteAsync(
        int id, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<Ok<CountResponse>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> IgnoreGroupAsync(
        int id, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.IgnoreGroupAsync(id, cancellationToken)).ToOk();

    public static async Task<IResult> GetFaceThumbnailAsync(int id, IFaceCropService crops, CancellationToken cancellationToken)
    {
        var path = await crops.GetOrCreateCropPathAsync(id, cancellationToken);
        return path is null ? Results.NotFound() : Results.File(path, "image/jpeg");
    }
}

public sealed record PersonNameRequest(string? Name);
