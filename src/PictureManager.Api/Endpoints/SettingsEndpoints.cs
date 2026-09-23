using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using PictureManager.Application.Settings;

namespace PictureManager.Api.Endpoints;

public sealed record SettingsRequest(string?[]? ExcludedFolderNames, string?[]? ExcludedExtensions, string?[]? IncludedExtensions);

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/settings", GetAsync);
        admin.MapPut("/settings", UpdateAsync);
        return admin;
    }

    public static async Task<Ok<SettingsDto>> GetAsync(ISettingsService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(cancellationToken));

    public static async Task<Results<Ok<SettingsSaveResult>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> UpdateAsync(
        SettingsRequest body, ISettingsService service, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(new SettingsInput(body.ExcludedFolderNames, body.ExcludedExtensions, body.IncludedExtensions), cancellationToken)).ToOk();
}
