using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Worker.Scanning;
using Xunit;

namespace PictureManager.Worker.Tests.Scanning;

public class EnrichmentBackgroundServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static IClock CreateClock(DateTime utcNow)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(utcNow);
        return clock;
    }

    [Fact]
    public async Task ProcessesQueuedItems_UpdatesScanJobCounters_AndCompletesWhenAllDone()
    {
        var enrichmentService = Substitute.For<IImageEnrichmentService>();
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Simulates the real DB state this item's increment/completion check would see: a job
        // that's Enriching with exactly one item left to enrich, so the atomic completion check
        // flips it.
        scanJobRepository.TryMarkCompletedIfEnrichedAsync(1, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true)
            .AndDoes(_ => completed.TrySetResult());

        var services = new ServiceCollection();
        services.AddSingleton(enrichmentService);
        services.AddSingleton(scanJobRepository);
        var provider = services.BuildServiceProvider();

        var clock = CreateClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var queue = new ChannelEnrichmentQueue();
        var service = new EnrichmentBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<EnrichmentBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(1, 100);

        await Task.WhenAny(completed.Task, Task.Delay(WaitTimeout));

        await service.StopAsync(CancellationToken.None);

        await enrichmentService.Received(1).EnrichAsync(100, Arg.Any<CancellationToken>());
        await scanJobRepository.Received(1).IncrementFilesEnrichedAsync(1, Arg.Any<CancellationToken>());
        await scanJobRepository.Received(1).TryMarkCompletedIfEnrichedAsync(1, clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessesQueuedItem_WhileScanJobStillEnumerating_DoesNotCompletePrematurely()
    {
        // FilesFound defaults to 0 and only reaches its real value once ScanService's own final
        // write flips Status to Enriching. While Status is still Enumerating (the scan is still
        // walking the tree), TryMarkCompletedIfEnrichedAsync's own WHERE clause (Status ==
        // Enriching) must refuse to flip the job -- that was the premature-completion bug this
        // guard fixes. Stub it to return false, mirroring what a real DB would evaluate.
        var enrichmentService = Substitute.For<IImageEnrichmentService>();
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        scanJobRepository.TryMarkCompletedIfEnrichedAsync(1, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false)
            .AndDoes(_ => completed.TrySetResult());

        var services = new ServiceCollection();
        services.AddSingleton(enrichmentService);
        services.AddSingleton(scanJobRepository);
        var provider = services.BuildServiceProvider();

        var clock = CreateClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var queue = new ChannelEnrichmentQueue();
        var service = new EnrichmentBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<EnrichmentBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(1, 100);

        await Task.WhenAny(completed.Task, Task.Delay(WaitTimeout));

        await service.StopAsync(CancellationToken.None);

        await enrichmentService.Received(1).EnrichAsync(100, Arg.Any<CancellationToken>());
        await scanJobRepository.Received(1).IncrementFilesEnrichedAsync(1, Arg.Any<CancellationToken>());
        await scanJobRepository.Received(1).TryMarkCompletedIfEnrichedAsync(1, clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnhandledExceptionOutsideEnrichAsync_DoesNotFaultTheBackgroundService()
    {
        // Regression test for Important finding #4: a failure in scope creation, DI resolution,
        // or the ScanJobRepository calls themselves (i.e. anything OUTSIDE EnrichAsync's own
        // try/catch) used to propagate out of ExecuteAsync, faulting the whole BackgroundService
        // (and, under the default BackgroundServiceExceptionBehavior, the host with it) --
        // silently stopping enrichment for every scan for the rest of the process lifetime.
        var enrichmentService = Substitute.For<IImageEnrichmentService>();
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        var reachedFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        scanJobRepository.IncrementFilesEnrichedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                reachedFailure.TrySetResult();
                throw new InvalidOperationException("simulated transient DB failure");
            });

        var services = new ServiceCollection();
        services.AddSingleton(enrichmentService);
        services.AddSingleton(scanJobRepository);
        var provider = services.BuildServiceProvider();

        var clock = CreateClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var queue = new ChannelEnrichmentQueue();
        var service = new EnrichmentBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(), clock, NullLogger<EnrichmentBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(1, 100);

        await Task.WhenAny(reachedFailure.Task, Task.Delay(WaitTimeout));

        // The service must still be running (ExecuteTask not faulted) despite the thrown
        // exception -- StopAsync must complete cleanly rather than rethrowing it.
        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
