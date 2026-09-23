using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Scanning;

/// <summary>Runs queued scans one at a time, off the HTTP request that started them.</summary>
public sealed class ScanBackgroundService : BackgroundService
{
    private readonly IScanQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScanBackgroundService> _logger;

    public ScanBackgroundService(IScanQueue queue, IServiceScopeFactory scopeFactory, ILogger<ScanBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: RunScanAsync already recorded the job as Cancelled.
                break;
            }
            catch (Exception ex)
            {
                // RunScanAsync already recorded the failure on the job. The loop must survive: a faulted
                // ExecuteAsync would stop every later scan for the rest of the process lifetime.
                _logger.LogError(ex, "Scan {ScanJobId} failed", scan.ScanJobId);
            }
        }
    }
}
