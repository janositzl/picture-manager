using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PictureManager.Application.Discovery;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Api.Endpoints;

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapPost("/discoveries", StartDiscoveryAsync);
        admin.MapGet("/discoveries/{id:int}/events", StreamDiscoveryEventsAsync);
        return admin;
    }

    public static async Task<IResult> StartDiscoveryAsync(DiscoveryRequest request, IDiscoveryService discoveryService, CancellationToken cancellationToken)
    {
        try
        {
            var discoveryJobId = await discoveryService.QueueDiscoveryAsync(request.RootId, request.FolderId, request.IsRecursive, cancellationToken);
            return Results.Ok(new DiscoveryStartedResponse(discoveryJobId));
        }
        catch (DiscoveryAlreadyInProgressException ex)
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

    public static async Task StreamDiscoveryEventsAsync(HttpContext context, int id, IJobRepository jobRepository, CancellationToken cancellationToken)
    {
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        while (!cancellationToken.IsCancellationRequested)
        {
            var discoveryJob = await jobRepository.GetByIdAsync(id, cancellationToken);
            if (discoveryJob is null)
            {
                await context.Response.WriteAsync("event: error\ndata: not found\n\n", cancellationToken);
                return;
            }

            var payload = JsonSerializer.Serialize(new DiscoveryProgress(
                discoveryJob.Id, discoveryJob.Status.ToString(), discoveryJob.FoldersProcessed, discoveryJob.ErrorMessage));
            await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);

            if (discoveryJob.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                return;

            await Task.Delay(1000, cancellationToken);
        }
    }
}

public sealed record DiscoveryRequest(int? RootId, int? FolderId, bool IsRecursive = true);
public sealed record DiscoveryStartedResponse(int DiscoveryJobId);
public sealed record DiscoveryProgress(int Id, string Status, int FoldersDiscovered, string? ErrorMessage);
