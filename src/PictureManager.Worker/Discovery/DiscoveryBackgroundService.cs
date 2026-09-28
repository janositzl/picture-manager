using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Discovery;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;

namespace PictureManager.Worker.Discovery;

/// <summary>
/// Runs queued folder discoveries one at a time, off the HTTP request that started them. Shares the single
/// active-job invariant with ScanBackgroundService via IJobRepository.HasActiveJobAsync.
/// </summary>
public sealed class DiscoveryBackgroundService : BackgroundService
{
    private const int MaxFailureRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IDiscoveryQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<DiscoveryBackgroundService> _logger;

    public DiscoveryBackgroundService(IDiscoveryQueue queue, IServiceScopeFactory scopeFactory, IClock clock, ILogger<DiscoveryBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var discovery in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var discoveryService = scope.ServiceProvider.GetRequiredService<IDiscoveryService>();
                await discoveryService.RunDiscoveryAsync(discovery, stoppingToken);
            }
            catch (Exception ex) when (ex is ScanRootsUnavailableException or ScanRootUnavailableException
                                            or FolderUnavailableException or FolderNotOnDiskException)
            {
                // Expected (share not mounted, root or folder removed while queued, folder gone from disk): the job
                // already carries the message for the UI.
                _logger.LogWarning("Discovery {DiscoveryJobId} failed: {Message}", discovery.DiscoveryJobId, ex.Message);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down: RunDiscoveryAsync already recorded the job as Cancelled.
                break;
            }
            catch (Exception ex)
            {
                // RunDiscoveryAsync's own attempt to record this failure already ran (and may itself have failed) --
                // so retry marking the job Failed instead of leaving it stuck Enumerating forever, which would
                // otherwise 409 every later scan/discovery until the next restart.
                _logger.LogError(ex, "Discovery {DiscoveryJobId} failed", discovery.DiscoveryJobId);
                await MarkFailedWithRetryAsync(discovery.DiscoveryJobId, ex, stoppingToken);
            }

            // Only one job runs at a time, so a startup with several new/unfinished roots only gets the first one
            // queued directly; chain to the next one here rather than waiting for a restart. Runs whether the
            // discovery above succeeded or (expectedly or not) failed -- an unmounted share on one root must not
            // strand every other root still waiting for its first discovery.
            await QueueNextUndiscoveredRootAsync(stoppingToken);
        }
    }

    private async Task QueueNextUndiscoveredRootAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var folders = scope.ServiceProvider.GetRequiredService<IFolderRepository>();
        var roots = scope.ServiceProvider.GetRequiredService<IImageRootRepository>();
        var discoveryService = scope.ServiceProvider.GetRequiredService<IDiscoveryService>();

        foreach (var root in await roots.GetAllAsync(stoppingToken))
        {
            if (!root.IsActive || !await folders.HasUndiscoveredFoldersAsync(root.Id, stoppingToken))
                continue;

            try
            {
                await discoveryService.QueueDiscoveryAsync(root.Id, null, cancellationToken: stoppingToken);
            }
            catch (DiscoveryAlreadyInProgressException)
            {
                // A scan started in the gap between this discovery finishing and this check; the scan (or
                // its own completion) will get another chance to pick this root up.
            }

            return;
        }
    }

    private async Task MarkFailedWithRetryAsync(int discoveryJobId, Exception ex, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxFailureRetries; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                // Only one job can be Enumerating/Enriching at a time, so this targets exactly the job that
                // just failed to record itself.
                await jobs.FailActiveJobsAsync(ex.Message, _clock.UtcNow, CancellationToken.None);
                return;
            }
            catch (Exception retryEx) when (attempt < MaxFailureRetries)
            {
                _logger.LogWarning(retryEx, "Retry {Attempt} failed to record discovery {DiscoveryJobId} failure", attempt, discoveryJobId);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }
}
