using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Api.Auth;
using PictureManager.Application.Common;
using PictureManager.Application.Users;

namespace PictureManager.Api.Endpoints;

public sealed record UserCreateRequest(string? Username, string? DisplayName, string? Password, string? Role, bool CanRunFolderActions);

public sealed record UserUpdateRequest(string? DisplayName, string? Role, bool? IsActive, bool? CanRunFolderActions);

public sealed record ResetPasswordRequest(string? NewPassword);

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder admin, IEndpointRouteBuilder user)
    {
        user.MapGet("/users/directory", DirectoryAsync);
        admin.MapGet("/users", ListAsync);
        admin.MapPost("/users", CreateAsync);
        admin.MapPatch("/users/{id:int}", UpdateAsync);
        admin.MapPost("/users/{id:int}/reset-password", ResetPasswordAsync);
        admin.MapDelete("/users/{id:int}", DeleteAsync);
    }

    public static async Task<Ok<IReadOnlyList<DirectoryEntry>>> DirectoryAsync(IUserService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.DirectoryAsync(cancellationToken));

    public static async Task<Ok<IReadOnlyList<UserDto>>> ListAsync(IUserService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(cancellationToken));

    public static async Task<Results<Created<UserDto>, ValidationProblem, Conflict<ProblemDetails>>> CreateAsync(
        UserCreateRequest body, IUserService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            new UserCreateInput(body.Username, body.DisplayName, body.Password, body.Role, body.CanRunFolderActions), cancellationToken);
        return result.Status switch
        {
            ResultStatus.Success => TypedResults.Created($"/api/users/{result.Value!.Id}", result.Value),
            ResultStatus.Invalid => TypedResults.ValidationProblem(ResultHttpExtensions.ToErrors(result.Errors)),
            _ => TypedResults.Conflict(ResultHttpExtensions.ConflictProblem(result.Message))
        };
    }

    public static async Task<Results<Ok<UserDto>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateAsync(
        int id, UserUpdateRequest body, IUserService service, UserSessionCache sessions, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, new UserUpdateInput(body.DisplayName, body.Role, body.IsActive, body.CanRunFolderActions), cancellationToken);
        sessions.Evict(id);
        return result.ToOk();
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> ResetPasswordAsync(
        int id, ResetPasswordRequest body, IUserService service, UserSessionCache sessions, CancellationToken cancellationToken)
    {
        var result = await service.ResetPasswordAsync(id, body.NewPassword, cancellationToken);
        sessions.Evict(id);
        return result.ToNoContent();
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, Conflict<ProblemDetails>>> DeleteAsync(
        int id, string? albums, IUserService service, UserSessionCache sessions, CancellationToken cancellationToken)
    {
        AlbumDisposition disposition;
        if (string.Equals(albums, "transfer", StringComparison.OrdinalIgnoreCase)) disposition = AlbumDisposition.Transfer;
        else if (string.Equals(albums, "delete", StringComparison.OrdinalIgnoreCase)) disposition = AlbumDisposition.Delete;
        else return ResultHttpExtensions.Invalid("albums", "Must be 'transfer' or 'delete'.");

        var result = await service.DeleteAsync(id, disposition, cancellationToken);
        sessions.Evict(id);
        return result.ToNoContent();
    }
}
