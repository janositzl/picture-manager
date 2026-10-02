using Microsoft.AspNetCore.Http;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;

namespace PictureManager.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGet("/jobs/active", GetActiveJobAsync);
        admin.MapPost("/jobs/{id:int}/cancel", CancelJobAsync);
        return admin;
    }

    public static async Task<IResult> GetActiveJobAsync(IJobRepository jobs, CancellationToken cancellationToken)
    {
        var job = await jobs.GetActiveAsync(cancellationToken);
        if (job is null)
            return Results.NoContent();

        return Results.Ok(new ActiveJobDto(
            job.Kind.ToString(), job.Id, job.FolderId, job.Status.ToString(),
            job.FoldersProcessed, job.FilesFound, job.FilesEnriched, job.ErrorMessage, job.FacesFound));
    }

    /// <summary>
    /// 202 = cancellation requested (the runner records Cancelled shortly). 404 = no such job, or it already
    /// finished. 409 = this kind can't be cancelled (only face recognition can, for now).
    /// </summary>
    public static async Task<IResult> CancelJobAsync(
        int id, IJobRepository jobs, IJobCancellationRegistry cancellations, CancellationToken cancellationToken)
    {
        var job = await jobs.GetByIdAsync(id, cancellationToken);
        if (job is null || job.Status is not (JobStatus.Enumerating or JobStatus.Enriching))
            return Results.NotFound();

        if (job.Kind != JobKind.FaceRecognition)
            return Results.Conflict(new { message = "Only face recognition jobs can be cancelled." });

        return cancellations.Cancel(id) ? Results.Accepted() : Results.NotFound();
    }
}

public sealed record ActiveJobDto(
    string Kind, int Id, int? FolderId, string Status,
    int FoldersProcessed, int FilesFound, int FilesEnriched, string? ErrorMessage, int FacesFound);
