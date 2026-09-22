using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Worker.Scanning;

public sealed class EnrichmentBackgroundService : BackgroundService
{
    private readonly IEnrichmentQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EnrichmentBackgroundService> _logger;

    public EnrichmentBackgroundService(IEnrichmentQueue queue, IServiceScopeFactory scopeFactory, ILogger<EnrichmentBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (scanJobId, imageId) in _queue.ReadAllAsync(stoppingToken))
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
    }

    private static async Task MarkOneEnrichedAsync(IScanJobRepository scanJobRepository, int scanJobId, CancellationToken cancellationToken)
    {
        var scanJob = await scanJobRepository.GetByIdAsync(scanJobId, cancellationToken);
        if (scanJob is null)
            return;

        scanJob.FilesEnriched++;

        // FilesFound defaults to 0 and only reaches its true value once ScanService's own final
        // write flips Status to Enriching (see ScanService.FinalizeSuccessAsync). Requiring
        // Status == Enriching here stops "FilesEnriched >= FilesFound" from being trivially true
        // (1 >= 0) the moment the first item is enriched while the scan is still walking the tree.
        if (scanJob.Status == ScanJobStatus.Enriching && scanJob.FilesEnriched >= scanJob.FilesFound)
        {
            scanJob.Status = ScanJobStatus.Completed;
            scanJob.CompletedUtc = DateTime.UtcNow;
        }

        await scanJobRepository.UpdateAsync(scanJob, cancellationToken);
    }
}
