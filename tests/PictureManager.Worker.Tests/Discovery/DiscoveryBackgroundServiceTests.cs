using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Discovery;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using PictureManager.Worker.Discovery;
using Xunit;

namespace PictureManager.Worker.Tests.Discovery;

public class DiscoveryBackgroundServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task DiscoveryBackgroundService_RootUnavailable_StillQueuesTheNextUndiscoveredRoot()
    {
        var queue = new ChannelDiscoveryQueue();
        var discoveryService = Substitute.For<IDiscoveryService>();
        discoveryService.RunDiscoveryAsync(new QueuedDiscovery(1, 10, null), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ScanRootsUnavailableException(new[] { "nas" })));

        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        discoveryService.QueueDiscoveryAsync(11, null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                queued.SetResult();
                return Task.FromResult(2);
            });

        var folders = Substitute.For<IFolderRepository>();
        folders.HasUndiscoveredFoldersAsync(11, Arg.Any<CancellationToken>()).Returns(true);
        var roots = Substitute.For<IImageRootRepository>();
        roots.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ImageRoot> { new() { Id = 11, Name = "dev", IsActive = true } });

        var provider = new ServiceCollection()
            .AddScoped(_ => discoveryService).AddScoped(_ => folders).AddScoped(_ => roots)
            .BuildServiceProvider();
        var service = new DiscoveryBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IClock>(), NullLogger<DiscoveryBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedDiscovery(1, 10, null));
        await queued.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await discoveryService.Received(1).QueueDiscoveryAsync(11, null, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DiscoveryBackgroundService_SuccessfulDiscovery_StillQueuesTheNextUndiscoveredRoot()
    {
        var queue = new ChannelDiscoveryQueue();
        var discoveryService = Substitute.For<IDiscoveryService>();
        discoveryService.RunDiscoveryAsync(new QueuedDiscovery(1, 10, null), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        discoveryService.QueueDiscoveryAsync(11, null, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                queued.SetResult();
                return Task.FromResult(2);
            });

        var folders = Substitute.For<IFolderRepository>();
        folders.HasUndiscoveredFoldersAsync(11, Arg.Any<CancellationToken>()).Returns(true);
        var roots = Substitute.For<IImageRootRepository>();
        roots.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ImageRoot> { new() { Id = 11, Name = "dev", IsActive = true } });

        var provider = new ServiceCollection()
            .AddScoped(_ => discoveryService).AddScoped(_ => folders).AddScoped(_ => roots)
            .BuildServiceProvider();
        var service = new DiscoveryBackgroundService(queue, provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IClock>(), NullLogger<DiscoveryBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedDiscovery(1, 10, null));
        await queued.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);

        await discoveryService.Received(1).QueueDiscoveryAsync(11, null, Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
