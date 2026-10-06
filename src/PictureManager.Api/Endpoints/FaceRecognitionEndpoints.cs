using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Api.Endpoints;

public static class FaceRecognitionEndpoints
{
    public static IEndpointRouteBuilder MapFaceRecognitionEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapPost("/face-recognitions", StartAsync);
        admin.MapGet("/face-recognitions/{id:int}/events", StreamEventsAsync);
        admin.MapGet("/face-recognitions/failures", GetFailuresAsync);
        admin.MapGet("/face-recognitions/coverage", GetCoverageAsync);
        return admin;
    }

    public static async Task<IResult> StartAsync(FaceRecognitionRequest request, IFaceRecognitionService service, CancellationToken cancellationToken)
    {
        if (!FaceDetectionPresets.TryParse(request.Preset, FaceDetectionPreset.Fast, out var preset))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["preset"] = new[] { "Must be 'fast' or 'detailed'." } });

        try
        {
            var jobId = await service.QueueAsync(request.RootId, request.FolderId, request.IsRecursive, request.Reanalyze, preset, cancellationToken);
            return TypedResults.Ok(new FaceRecognitionStartedResponse(jobId));
        }
        catch (FaceRecognitionAlreadyInProgressException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (ScanRootUnavailableException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["rootId"] = new[] { ex.Message } });
        }
        catch (FolderUnavailableException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["folderId"] = new[] { ex.Message } });
        }
    }

    public static async Task StreamEventsAsync(HttpContext context, int id, IJobRepository jobRepository, CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await jobRepository.GetByIdAsync(id, cancellationToken);
            if (job is null)
            {
                await context.Response.WriteAsync("event: error\ndata: not found\n\n", cancellationToken);
                return;
            }

            var payload = JsonSerializer.Serialize(new FaceRecognitionProgress(
                job.Id, job.Status.ToString(), job.FilesFound, job.FilesEnriched, job.FacesFound, job.ErrorMessage));
            await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                return;

            await Task.Delay(1000, cancellationToken);
        }
    }

    public static async Task<IResult> GetFailuresAsync(IFaceRecognitionService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetPermanentFailuresAsync(cancellationToken));

    public static async Task<IResult> GetCoverageAsync(IFaceRecognitionService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetFolderCoverageAsync(cancellationToken));
}

public sealed record FaceRecognitionRequest(int? RootId, int? FolderId, bool IsRecursive = true, bool Reanalyze = false, string? Preset = null);
public sealed record FaceRecognitionStartedResponse(int FaceRecognitionJobId);
public sealed record FaceRecognitionProgress(int Id, string Status, int ImagesFound, int ImagesProcessed, int FacesFound, string? ErrorMessage);
