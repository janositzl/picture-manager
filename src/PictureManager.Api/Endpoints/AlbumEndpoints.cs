using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Albums;
using PictureManager.Application.Common;

namespace PictureManager.Api.Endpoints;

public sealed record AlbumCreateRequest(string? Name, string? Description);

public sealed record AlbumAddImagesRequest(int[]? ImageIds, int? FolderId);

public sealed record AlbumRemoveImagesRequest(int[]? ImageIds);

public sealed record AlbumMoveRequest(int? AfterImageId);

public sealed record AlbumSortRequest(string? By);

public static class AlbumEndpoints
{
    public static IEndpointRouteBuilder MapAlbumEndpoints(this IEndpointRouteBuilder user)
    {
        user.MapGet("/albums", GetAllAsync);
        user.MapPost("/albums", CreateAsync);
        user.MapGet("/albums/{id:int}", GetAsync);
        user.MapPatch("/albums/{id:int}", UpdateAsync);
        user.MapDelete("/albums/{id:int}", DeleteAsync);
        user.MapGet("/albums/{id:int}/images", ListImagesAsync);
        user.MapPost("/albums/{id:int}/images", AddImagesAsync);
        user.MapPost("/albums/{id:int}/images/remove", RemoveImagesAsync);
        user.MapPost("/albums/{id:int}/images/{imageId:int}/move", MoveImageAsync);
        user.MapPost("/albums/{id:int}/sort", SortAsync);
        user.MapGet("/albums/{id:int}/export", ExportAsync);
        return user;
    }

    public static async Task<Ok<IReadOnlyList<AlbumSummary>>> GetAllAsync(IAlbumService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAllAsync(cancellationToken));

    public static async Task<Results<Created<AlbumDetail>, ValidationProblem, Conflict<ProblemDetails>>> CreateAsync(
        AlbumCreateRequest body, IAlbumService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(new AlbumCreate(body.Name, body.Description), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Success => TypedResults.Created($"/api/albums/{result.Value!.Id}", result.Value),
            ResultStatus.Conflict => TypedResults.Conflict(ResultHttpExtensions.ConflictProblem(result.Message)),
            _ => TypedResults.ValidationProblem(ResultHttpExtensions.ToErrors(result.Errors))
        };
    }

    public static async Task<Results<Ok<AlbumDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> GetAsync(
        int id, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToOk();

    public static async Task<Results<Ok<AlbumDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateAsync(
        int id, JsonElement body, IAlbumService service, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ResultHttpExtensions.Invalid("body", "Expected a JSON object.");
        if (!PatchJson.TryReadString(body, "name", out var namePresent, out var name) || (namePresent && name is null))
            return ResultHttpExtensions.Invalid("name", "Must be a string.");
        if (!PatchJson.TryReadString(body, "description", out var descriptionPresent, out var description))
            return ResultHttpExtensions.Invalid("description", "Must be a string or null.");

        return (await service.UpdateAsync(id, new AlbumUpdate(name, descriptionPresent, description), cancellationToken)).ToOk();
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> DeleteAsync(
        int id, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToNoContent();

    public static async Task<Results<Ok<PagedResult<AlbumImageItem>>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ListImagesAsync(
        int id, string? cursor, int? limit, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.ListImagesAsync(id, cursor, limit, cancellationToken)).ToOk();

    public static async Task<Results<Ok<AlbumAddResult>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> AddImagesAsync(
        int id, AlbumAddImagesRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.AddImagesAsync(id, new AlbumAddImages(body.ImageIds, body.FolderId), cancellationToken)).ToOk();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> RemoveImagesAsync(
        int id, AlbumRemoveImagesRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.RemoveImagesAsync(id, body.ImageIds, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> MoveImageAsync(
        int id, int imageId, AlbumMoveRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.MoveImageAsync(id, imageId, body.AfterImageId, cancellationToken)).ToNoContent();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> SortAsync(
        int id, AlbumSortRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.SortAsync(id, body.By, cancellationToken)).ToNoContent();

    public static async Task<Results<FileContentHttpResult, NotFound>> ExportAsync(
        int id, string? prefix, IAlbumService service, CancellationToken cancellationToken)
    {
        var result = await service.ExportAsync(id, prefix, cancellationToken);
        if (!result.IsSuccess)
            return TypedResults.NotFound();

        // Encoding.UTF8.GetBytes never writes a BOM. Passing fileDownloadName makes ASP.NET Core emit
        // both filename= and the RFC 5987 filename*= form, so non-ASCII album names survive.
        return TypedResults.File(Encoding.UTF8.GetBytes(result.Value!.Content), "text/plain; charset=utf-8", result.Value.FileName);
    }
}
