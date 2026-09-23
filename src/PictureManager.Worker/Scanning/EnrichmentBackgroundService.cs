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

public sealed class EnrichmentBackgroundService : BackgroundService
{
    private readonly IEnrichmentQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<EnrichmentBackgroundService> _logger;

    public EnrichmentBackgroundService(IEnrichmentQueue queue, IServiceScopeFactory scopeFactory, IClock clock, ILogger<EnrichmentBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (scanJobId, imageId) in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var enrichmentService = scope.ServiceProvider.GetRequiredService<IImageEnrichmentService>();
                var scanJobRepository = scope.ServiceProvider.GetRequiredService<IScanJobRepository>();

                try
                {
                    await enrichmentService.EnrichAsync(imageId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to enrich image {ImageId}", imageId);
                }

                await MarkOneEnrichedAsync(scanJobRepository, scanJobId, stoppingToken);
            }
            catch (Exception ex)
            {
                // Broader than the inner try: a scope-creation failure, a DI resolution failure, or
                // MarkOneEnrichedAsync's own DB write throwing (e.g. a transient connection drop)
                // must not fault this loop -- ExecuteAsync faulting takes the whole BackgroundService
                // down (and, under the default BackgroundServiceExceptionBehavior, the host with it),
                // silently stopping enrichment for every scan for the rest of the process lifetime.
                _logger.LogError(ex, "Unhandled error processing enrichment item for scan {ScanJobId}, image {ImageId}", scanJobId, imageId);
            }
        }
    }

    private async Task MarkOneEnrichedAsync(IScanJobRepository scanJobRepository, int scanJobId, CancellationToken cancellationToken)
    {
        await scanJobRepository.IncrementFilesEnrichedAsync(scanJobId, cancellationToken);
        await scanJobRepository.TryMarkCompletedIfEnrichedAsync(scanJobId, _clock.UtcNow, cancellationToken);
    }
}
