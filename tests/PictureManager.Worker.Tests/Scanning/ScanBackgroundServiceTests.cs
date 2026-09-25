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

public class ScanBackgroundServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ScanBackgroundService_RunsQueuedScans_AndSurvivesAFailingOne()
    {
        var queue = new ChannelScanQueue();
        var scanService = Substitute.For<IScanService>();
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanService.RunScanAsync(new QueuedScan(1, null, null, true), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        scanService.RunScanAsync(new QueuedScan(2, null, null, true), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                secondRan.SetResult();
                return Task.CompletedTask;
            });

        var jobs = Substitute.For<IJobRepository>();
        jobs.FailActiveJobsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(1);
        var clock = Substitute.For<IClock>();
        var provider = new ServiceCollection().AddScoped(_ => scanService).AddScoped(_ => jobs).BuildServiceProvider();
        var service = new ScanBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            clock, NullLogger<ScanBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedScan(1, null, null, true));
        queue.Enqueue(new QueuedScan(2, null, null, true));

        await secondRan.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await scanService.Received(1).RunScanAsync(new QueuedScan(1, null, null, true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanBackgroundService_WhenRunScanAsyncThrows_RetriesUntilTheJobIsMarkedFailed()
    {
        // Simulates a scan whose own attempt to record its failure also fails (e.g. the database is down):
        // RunScanAsync throws, and the first retry to mark the job Failed throws too before the second succeeds.
        var queue = new ChannelScanQueue();
        var scanService = Substitute.For<IScanService>();
        scanService.RunScanAsync(new QueuedScan(5, null, null, true), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var jobs = Substitute.For<IJobRepository>();
        var attempts = 0;
        var marked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.FailActiveJobsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                if (attempts < 2)
                    return Task.FromException<int>(new InvalidOperationException("db down"));
                marked.SetResult();
                return Task.FromResult(1);
            });

        var clock = Substitute.For<IClock>();
        var provider = new ServiceCollection().AddScoped(_ => scanService).AddScoped(_ => jobs).BuildServiceProvider();
        var service = new ScanBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            clock, NullLogger<ScanBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedScan(5, null, null, true));

        await marked.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        attempts.Should().Be(2);
    }

    [Fact]
    public async Task ScanBackgroundService_ExpectedTargetFailure_DoesNotRetryMarkingTheJobFailed()
    {
        var queue = new ChannelScanQueue();
        var scanService = Substitute.For<IScanService>();
        var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanService.RunScanAsync(new QueuedScan(8, null, 20, true), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                ran.SetResult();
                return Task.FromException(new FolderNotOnDiskException("dev", "Trips"));
            });

        var jobs = Substitute.For<IJobRepository>();
        var provider = new ServiceCollection().AddScoped(_ => scanService).AddScoped(_ => jobs).BuildServiceProvider();
        var service = new ScanBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IClock>(), NullLogger<ScanBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedScan(8, null, 20, true));
        await ran.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await jobs.DidNotReceive().FailActiveJobsAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChannelScanQueue_Enqueue_ThenReadAllAsync_YieldsTheScan()
    {
        var queue = new ChannelScanQueue();
        queue.Enqueue(new QueuedScan(7, 1, null, false));

        await foreach (var scan in queue.ReadAllAsync())
        {
            scan.Should().Be(new QueuedScan(7, 1, null, false));
            break;
        }
    }
}
