using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Api.Endpoints;

public static class ScanEndpoints
{
    public static IEndpointRouteBuilder MapScanEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapPost("/scans", StartScanAsync);
        admin.MapGet("/scans/{id:int}/events", StreamScanEventsAsync);
        return admin;
    }

    public static async Task<IResult> StartScanAsync(ScanRequest request, IScanService scanService, CancellationToken cancellationToken)
    {
        try
        {
            var scanJobId = await scanService.QueueScanAsync(request.RootId, null, request.IsRecursive, cancellationToken);
            return Results.Ok(new ScanStartedResponse(scanJobId));
        }
        catch (ScanAlreadyInProgressException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (ScanRootUnavailableException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["rootId"] = new[] { ex.Message } });
        }
    }

    public static async Task StreamScanEventsAsync(HttpContext context, int id, IJobRepository scanJobRepository, CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        while (!cancellationToken.IsCancellationRequested)
        {
            var scanJob = await scanJobRepository.GetByIdAsync(id, cancellationToken);
            if (scanJob is null)
            {
                await context.Response.WriteAsync("event: error\ndata: not found\n\n", cancellationToken);
                return;
            }

            var payload = JsonSerializer.Serialize(new ScanProgress(
                scanJob.Id, scanJob.Status.ToString(), scanJob.FoldersProcessed, scanJob.FilesFound, scanJob.FilesEnriched, scanJob.ErrorMessage));
            await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            if (scanJob.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                return;

            await Task.Delay(1000, cancellationToken);
        }
    }
}

public sealed record ScanRequest(int? RootId, bool IsRecursive);
public sealed record ScanStartedResponse(int ScanJobId);
public sealed record ScanProgress(int Id, string Status, int FoldersScanned, int FilesFound, int FilesEnriched, string? ErrorMessage);
