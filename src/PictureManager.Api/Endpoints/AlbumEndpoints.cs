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

public sealed record AlbumCoverRequest(int? ImageId);

public sealed record AlbumShareRequest(string? Permission);

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
        user.MapPut("/albums/{id:int}/cover", SetCoverAsync);
        user.MapGet("/albums/{id:int}/export", ExportAsync);
        user.MapGet("/albums/{id:int}/shares", GetSharesAsync);
        user.MapPut("/albums/{id:int}/shares/{userId:int}", SetShareAsync);
        user.MapDelete("/albums/{id:int}/shares/{userId:int}", RemoveShareAsync);
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

    public static async Task<Results<Ok<AlbumDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> GetAsync(
        int id, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.GetAsync(id, cancellationToken)).ToOkOrForbidden();

    public static async Task<Results<Ok<AlbumDetail>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> UpdateAsync(
        int id, JsonElement body, IAlbumService service, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return ResultHttpExtensions.Invalid("body", "Expected a JSON object.");
        if (!PatchJson.TryReadString(body, "name", out var namePresent, out var name) || (namePresent && name is null))
            return ResultHttpExtensions.Invalid("name", "Must be a string.");
        if (!PatchJson.TryReadString(body, "description", out var descriptionPresent, out var description))
            return ResultHttpExtensions.Invalid("description", "Must be a string or null.");

        return (await service.UpdateAsync(id, new AlbumUpdate(name, descriptionPresent, description), cancellationToken)).ToOkOrForbidden();
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> DeleteAsync(
        int id, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, cancellationToken)).ToNoContentOrForbidden();

    public static async Task<Results<Ok<PagedResult<AlbumImageItem>>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> ListImagesAsync(
        int id, string? cursor, int? limit, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.ListImagesAsync(id, cursor, limit, cancellationToken)).ToOkOrForbidden();

    public static async Task<Results<Ok<AlbumAddResult>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> AddImagesAsync(
        int id, AlbumAddImagesRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.AddImagesAsync(id, new AlbumAddImages(body.ImageIds, body.FolderId), cancellationToken)).ToOkOrForbidden();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> RemoveImagesAsync(
        int id, AlbumRemoveImagesRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.RemoveImagesAsync(id, body.ImageIds, cancellationToken)).ToNoContentOrForbidden();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> MoveImageAsync(
        int id, int imageId, AlbumMoveRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.MoveImageAsync(id, imageId, body.AfterImageId, cancellationToken)).ToNoContentOrForbidden();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> SortAsync(
        int id, AlbumSortRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.SortAsync(id, body.By, cancellationToken)).ToNoContentOrForbidden();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> SetCoverAsync(
        int id, AlbumCoverRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.SetCoverAsync(id, body.ImageId, cancellationToken)).ToNoContentOrForbidden();

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

    public static async Task<Results<Ok<IReadOnlyList<AlbumShareDto>>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> GetSharesAsync(
        int id, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.GetSharesAsync(id, cancellationToken)).ToOkOrForbidden();

    public static async Task<Results<Ok<AlbumShareDto>, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> SetShareAsync(
        int id, int userId, AlbumShareRequest body, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.SetShareAsync(id, userId, body.Permission, cancellationToken)).ToOkOrForbidden();

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>, ProblemHttpResult>> RemoveShareAsync(
        int id, int userId, IAlbumService service, CancellationToken cancellationToken) =>
        (await service.RemoveShareAsync(id, userId, cancellationToken)).ToNoContentOrForbidden();
}
