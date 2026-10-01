using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Worker.Faces;
using Xunit;

namespace PictureManager.Worker.Tests.Faces;

public class FaceRecognitionBackgroundServiceTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static (FaceRecognitionBackgroundService Service, ChannelFaceRecognitionQueue Queue, JobCancellationRegistry Registry)
        Create(IFaceRecognitionService faceService)
    {
        var queue = new ChannelFaceRecognitionQueue();
        var registry = new JobCancellationRegistry();
        var provider = new ServiceCollection()
            .AddScoped(_ => faceService)
            .AddScoped(_ => Substitute.For<IJobRepository>())
            .BuildServiceProvider();
        var service = new FaceRecognitionBackgroundService(
            queue, registry, provider.GetRequiredService<IServiceScopeFactory>(), Substitute.For<IClock>(),
            NullLogger<FaceRecognitionBackgroundService>.Instance);
        return (service, queue, registry);
    }

    [Fact]
    public async Task CancelledWhileQueued_RunsWithCancelledToken_AndLoopContinues()
    {
        var faceService = Substitute.For<IFaceRecognitionService>();
        var firstToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        faceService.RunAsync(new QueuedFaceRecognition(1, null, true), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var token = call.Arg<CancellationToken>();
            firstToken.SetResult(token);
            return Task.FromCanceled(token);
        });
        faceService.RunAsync(new QueuedFaceRecognition(2, null, true), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            secondRan.SetResult();
            return Task.CompletedTask;
        });
        var (service, queue, registry) = Create(faceService);
        registry.Register(1);
        registry.Cancel(1);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedFaceRecognition(1, null, true));
        queue.Enqueue(new QueuedFaceRecognition(2, null, true));

        (await firstToken.Task.WaitAsync(WaitTimeout)).IsCancellationRequested.Should().BeTrue();
        await secondRan.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);
        registry.Cancel(1).Should().BeFalse("the runner releases the job when it finishes");
    }

    [Fact]
    public async Task UnexpectedFailure_MarksActiveJobFailed_AndLoopContinues()
    {
        var faceService = Substitute.For<IFaceRecognitionService>();
        faceService.RunAsync(new QueuedFaceRecognition(1, null, true), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));
        var secondRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        faceService.RunAsync(new QueuedFaceRecognition(2, null, true), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            secondRan.SetResult();
            return Task.CompletedTask;
        });
        var (service, queue, _) = Create(faceService);

        await service.StartAsync(CancellationToken.None);
        queue.Enqueue(new QueuedFaceRecognition(1, null, true));
        queue.Enqueue(new QueuedFaceRecognition(2, null, true));

        await secondRan.Task.WaitAsync(WaitTimeout);
        await service.StopAsync(CancellationToken.None);
    }
}
