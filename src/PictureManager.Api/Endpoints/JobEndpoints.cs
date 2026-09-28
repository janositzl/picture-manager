using Microsoft.AspNetCore.Http;
using PictureManager.Application.Repositories;

namespace PictureManager.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/jobs/active", GetActiveJobAsync);
        return admin;
    }

    public static async Task<IResult> GetActiveJobAsync(IJobRepository jobs, CancellationToken cancellationToken)
    {
        var job = await jobs.GetActiveAsync(cancellationToken);
        if (job is null)
            return Results.NoContent();

        return Results.Ok(new ActiveJobDto(
            job.Kind.ToString(), job.Id, job.FolderId, job.Status.ToString(),
            job.FoldersProcessed, job.FilesFound, job.FilesEnriched, job.ErrorMessage));
    }
}

public sealed record ActiveJobDto(
    string Kind, int Id, int? FolderId, string Status,
    int FoldersProcessed, int FilesFound, int FilesEnriched, string? ErrorMessage);
