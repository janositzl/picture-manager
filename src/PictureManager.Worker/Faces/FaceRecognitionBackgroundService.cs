using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Faces;

/// <summary>
/// Runs queued face recognition jobs one at a time. Each runs under its user-cancellation token linked with
/// shutdown: a user cancel ends that job and the loop moves on; shutdown ends the loop.
/// </summary>
public sealed class FaceRecognitionBackgroundService : BackgroundService
{
    private const int MaxFailureRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IFaceRecognitionQueue _queue;
    private readonly IJobCancellationRegistry _cancellations;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<FaceRecognitionBackgroundService> _logger;

    public FaceRecognitionBackgroundService(
        IFaceRecognitionQueue queue, IJobCancellationRegistry cancellations, IServiceScopeFactory scopeFactory, IClock clock,
        ILogger<FaceRecognitionBackgroundService> logger)
    {
        _queue = queue;
        _cancellations = cancellations;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.ReadAllAsync(stoppingToken))
        {
            // Register is idempotent: QueueAsync already registered it, so a cancel while queued is honored here.
            using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cancellations.Register(job.JobId));
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var faceService = scope.ServiceProvider.GetRequiredService<IFaceRecognitionService>();
                await faceService.RunAsync(job, jobCancellation.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: RunAsync already recorded the job as Cancelled.
                break;
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the user: RunAsync recorded it. Keep serving the queue.
                _logger.LogInformation("Face recognition {JobId} cancelled by the user", job.JobId);
            }
            catch (Exception ex) when (ex is FaceModelUnavailableException or ScanRootsUnavailableException
                                            or ScanRootUnavailableException or FolderUnavailableException)
            {
                // Expected: the job already carries the message for the UI.
                _logger.LogWarning("Face recognition {JobId} failed: {Message}", job.JobId, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Face recognition {JobId} failed", job.JobId);
                await MarkFailedWithRetryAsync(job.JobId, ex, stoppingToken);
            }
            finally
            {
                _cancellations.Release(job.JobId);
            }
        }
    }

    private async Task MarkFailedWithRetryAsync(int jobId, Exception ex, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxFailureRetries; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                // Only one job is active at a time, so this targets exactly the job that failed to record itself.
                await jobs.FailActiveJobsAsync(ex.Message, _clock.UtcNow, CancellationToken.None);
                return;
            }
            catch (Exception retryEx) when (attempt < MaxFailureRetries)
            {
                _logger.LogWarning(retryEx, "Retry {Attempt} failed to record face recognition {JobId} failure", attempt, jobId);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
