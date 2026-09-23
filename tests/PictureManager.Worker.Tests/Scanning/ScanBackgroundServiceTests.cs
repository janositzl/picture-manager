using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Scanning;
using PictureManager.Worker.Scanning;
using Xunit;

namespace PictureManager.Worker.Tests.Scanning;

public class ScanBackgroundServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ScanBackgroundService_RunsQueuedScans_AndSurvivesAFailingOne()
    {
        var queue = new ChannelScanQueue();
        var scanService = Substitute.For<IScanService>();
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanService.RunScanAsync(new QueuedScan(1, null, true), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        scanService.RunScanAsync(new QueuedScan(2, null, true), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                secondRan.SetResult();
                return Task.CompletedTask;
            });

        var provider = new ServiceCollection().AddScoped(_ => scanService).BuildServiceProvider();
        var service = new ScanBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScanBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedScan(1, null, true));
        queue.Enqueue(new QueuedScan(2, null, true));

        await secondRan.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await scanService.Received(1).RunScanAsync(new QueuedScan(1, null, true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChannelScanQueue_Enqueue_ThenReadAllAsync_YieldsTheScan()
    {
        var queue = new ChannelScanQueue();
        queue.Enqueue(new QueuedScan(7, 1, false));

        await foreach (var scan in queue.ReadAllAsync())
        {
            scan.Should().Be(new QueuedScan(7, 1, false));
            break;
        }
    }
}
