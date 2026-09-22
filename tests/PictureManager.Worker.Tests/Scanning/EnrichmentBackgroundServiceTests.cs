using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using PictureManager.Worker.Scanning;
using Xunit;

namespace PictureManager.Worker.Tests.Scanning;

public class EnrichmentBackgroundServiceTests
{
    [Fact]
    public async Task ProcessesQueuedItems_UpdatesScanJobCounters_AndCompletesWhenAllDone()
    {
        var enrichmentService = Substitute.For<IImageEnrichmentService>();
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        var scanJob = new ScanJob { Id = 1, FilesFound = 1, FilesEnriched = 0, Status = ScanJobStatus.Enriching };
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(scanJob);

        var services = new ServiceCollection();
        services.AddSingleton(enrichmentService);
        services.AddSingleton(scanJobRepository);
        var provider = services.BuildServiceProvider();

        var queue = new ChannelEnrichmentQueue();
        var service = new EnrichmentBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<EnrichmentBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(1, 100);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (scanJob.FilesEnriched == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        await service.StopAsync(CancellationToken.None);

        await enrichmentService.Received(1).EnrichAsync(100, Arg.Any<CancellationToken>());
        scanJob.FilesEnriched.Should().Be(1);
        scanJob.Status.Should().Be(ScanJobStatus.Completed);
    }

    [Fact]
    public async Task ProcessesQueuedItem_WhileScanJobStillEnumerating_DoesNotCompletePrematurely()
    {
        // FilesFound defaults to 0 and only reaches its real value once ScanService's own final
        // write flips Status to Enriching. While Status is still Enumerating (the scan is still
        // walking the tree), FilesEnriched (1) >= FilesFound (0) must NOT be enough to complete
        // the job -- that was the premature-completion bug this guard fixes.
        var enrichmentService = Substitute.For<IImageEnrichmentService>();
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        var scanJob = new ScanJob { Id = 1, FilesFound = 0, FilesEnriched = 0, Status = ScanJobStatus.Enumerating };
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(scanJob);

        var services = new ServiceCollection();
        services.AddSingleton(enrichmentService);
        services.AddSingleton(scanJobRepository);
        var provider = services.BuildServiceProvider();

        var queue = new ChannelEnrichmentQueue();
        var service = new EnrichmentBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<EnrichmentBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(1, 100);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (scanJob.FilesEnriched == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50);

        await service.StopAsync(CancellationToken.None);

        await enrichmentService.Received(1).EnrichAsync(100, Arg.Any<CancellationToken>());
        scanJob.FilesEnriched.Should().Be(1);
        scanJob.Status.Should().Be(ScanJobStatus.Enumerating);
        scanJob.CompletedUtc.Should().BeNull();
    }
}
