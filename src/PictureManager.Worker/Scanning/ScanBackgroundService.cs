using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

/// <summary>Runs queued scans one at a time, off the HTTP request that started them.</summary>
public sealed class ScanBackgroundService : BackgroundService
{
    private const int MaxFailureRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IScanQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<ScanBackgroundService> _logger;

    public ScanBackgroundService(IScanQueue queue, IServiceScopeFactory scopeFactory, IClock clock, ILogger<ScanBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var scan in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
                await scanService.RunScanAsync(scan, stoppingToken);
            }
            catch (ScanRootsUnavailableException ex)
            {
                // Expected when a share isn't mounted; the job already carries the message for the UI.
                _logger.LogWarning("Scan {ScanJobId} failed: {Message}", scan.ScanJobId, ex.Message);
            }
            catch (ScanRootUnavailableException ex)
            {
                // Expected when the explicit root was deactivated or deleted while the scan waited in the queue.
                _logger.LogWarning("Scan {ScanJobId} failed: {Message}", scan.ScanJobId, ex.Message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: RunScanAsync already recorded the job as Cancelled.
                break;
            }
            catch (Exception ex)
            {
                // RunScanAsync's own attempt to record this failure already ran (and may itself have failed,
                // e.g. the database is down) -- so retry marking the job Failed instead of leaving it stuck
                // Enumerating/Enriching forever, which would otherwise 409 every later scan and folder removal
                // until the next restart. The loop itself must survive regardless: a faulted ExecuteAsync would
                // stop every later scan for the rest of the process lifetime.
                _logger.LogError(ex, "Scan {ScanJobId} failed", scan.ScanJobId);
                await MarkFailedWithRetryAsync(scan.ScanJobId, ex, stoppingToken);
            }
        }
    }

    private async Task MarkFailedWithRetryAsync(int scanJobId, Exception ex, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxFailureRetries; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                // Only one job can be Enumerating/Enriching at a time (QueueScanAsync refuses a second while
                // one is active), so this targets exactly the job that just failed to record itself.
                await jobs.FailActiveJobsAsync(ex.Message, _clock.UtcNow, CancellationToken.None);
                return;
            }
            catch (Exception retryEx) when (attempt < MaxFailureRetries)
            {
                _logger.LogWarning(retryEx, "Retry {Attempt} failed to record scan {ScanJobId} failure", attempt, scanJobId);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
