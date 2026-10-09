using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;

namespace PictureManager.Api.Endpoints;

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder user, IEndpointRouteBuilder admin)
    {
        user.MapGet("/people", GetAllAsync);
        user.MapGet("/people/{id:int}", GetAsync);
        admin.MapPatch("/people/{id:int}", NameAsync);
        admin.MapPut("/people/{id:int}/cover", SetCoverAsync);
        admin.MapDelete("/people/{id:int}", DeleteAsync);
        admin.MapPost("/people/{id:int}/ignore", IgnoreGroupAsync);
        admin.MapPost("/people/{id:int}/assign", AssignGroupAsync);
        user.MapGet("/faces/{id:int}/thumbnail", GetFaceThumbnailAsync);

        user.MapGet("/images/{id:int}/faces", GetImageFacesAsync);
        admin.MapPost("/images/{id:int}/faces/recheck", RecheckFacesAsync);
        admin.MapPost("/images/{id:int}/faces/reanalyze", ReanalyzeImageAsync);
        admin.MapPost("/faces/{id:int}/accept", AcceptAsync);
        admin.MapPost("/faces/{id:int}/reject", RejectAsync);
        admin.MapPost("/faces/{id:int}/unknown", MarkUnknownAsync);
        admin.MapPost("/faces/{id:int}/ignore", IgnoreAsync);
        admin.MapPost("/faces/{id:int}/restore", RestoreAsync);
        admin.MapPost("/faces/{id:int}/assign", AssignAsync);
        admin.MapPost("/people/{id:int}/suggestions/accept", AcceptAllAsync);
        admin.MapPost("/people/{id:int}/images/{imageId:int}/accept", AcceptImageAsync);
        admin.MapPost("/people/{id:int}/images/{imageId:int}/reject", RejectImageAsync);
        return user;
    }

    public static async Task<Results<Ok<IReadOnlyList<ImageFaceDto>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetImageFacesAsync(
        int id, bool? includeIgnored, bool? confirmedOnly, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.GetImageFacesAsync(id, includeIgnored ?? false, confirmedOnly ?? false, cancellationToken)).ToOk();

    public static async Task<Results<Ok<ReanalyzeImageResponse>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ReanalyzeImageAsync(
        int id, ReanalyzeImageRequest? request, IFaceReviewService service, CancellationToken cancellationToken)
    {
        if (!FaceDetectionPresets.TryParse(request?.Preset, FaceDetectionPreset.Detailed, out var preset))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["preset"] = new[] { "Must be 'fast' or 'detailed'." } });
        return (await service.ReanalyzeImageAsync(id, preset, cancellationToken)).ToOk();
    }

    public static async Task<Results<Ok<CountResponse>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RecheckFacesAsync(
        int id, IFaceReviewService service, CancellationToken cancellationToken) =>
        (await service.RecheckFacesAsync(id, cancellationToken)).ToOk();

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

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> SetCoverAsync(
        int id, PersonCoverRequest request, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.SetCoverAsync(id, request.FaceId, cancellationToken)).ToOk();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> DeleteAsync(
        int id, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<Ok<CountResponse>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> IgnoreGroupAsync(
        int id, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.IgnoreGroupAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<Ok<PersonSummary>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> AssignGroupAsync(
        int id, AssignGroupRequest request, IPeopleService service, CancellationToken cancellationToken) =>
        (await service.AssignGroupAsync(id, request.PersonId, request.ImageIds, cancellationToken)).ToOk();

    public static async Task<IResult> GetFaceThumbnailAsync(int id, IFaceCropService crops, CancellationToken cancellationToken)
    {
        var path = await crops.GetOrCreateCropPathAsync(id, cancellationToken);
        return path is null ? Results.NotFound() : Results.File(path, "image/jpeg");
    }
}

public sealed record PersonCoverRequest(int? FaceId);

public sealed record PersonNameRequest(string? Name);

public sealed record ReanalyzeImageRequest(string? Preset);

public sealed record AssignGroupRequest(int PersonId, IReadOnlyList<int>? ImageIds = null);
