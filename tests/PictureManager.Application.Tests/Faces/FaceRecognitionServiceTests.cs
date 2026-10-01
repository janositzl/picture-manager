using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceRecognitionServiceTests
{
    private static readonly FaceModelDescriptor Descriptor = new("m", "1", 512, "hash");
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IFaceRepository _faces = Substitute.For<IFaceRepository>();
    private readonly IFaceAnalyzer _analyzer = Substitute.For<IFaceAnalyzer>();
    private readonly IFaceClusterer _clusterer = Substitute.For<IFaceClusterer>();
    private readonly IFaceRecognitionQueue _queue = Substitute.For<IFaceRecognitionQueue>();
    private readonly IFaceImageProcessor _processor = Substitute.For<IFaceImageProcessor>();
    private readonly JobCancellationRegistry _cancellations = new();
    private readonly IClock _clock = Substitute.For<IClock>();

    public FaceRecognitionServiceTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        _analyzer.Model.Returns(Descriptor);
        _faces.GetOrCreateModelIdAsync(Descriptor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(3);
        _jobs.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<Job>();
            job.Id = 50;
            return job;
        });
        _processor.ProcessAsync(Arg.Any<int>(), 3, Arg.Any<CancellationToken>()).Returns(new FaceImageResult(FaceImageOutcome.Processed, 1));
    }

    private FaceRecognitionService Create()
    {
        var provider = new ServiceCollection()
            .AddScoped(_ => _processor)
            .AddScoped(_ => _jobs)
            .BuildServiceProvider();
        return new FaceRecognitionService(
            _folders, _roots, _jobs, _faces, _analyzer, _clusterer, _queue, _cancellations,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FaceRecognitionOptions { ReadConcurrency = 1, InferenceConcurrency = 1 }, _clock);
    }

    [Fact]
    public async Task QueueAsync_CreatesEnumeratingFaceJob_RegistersCancellation_AndEnqueues()
    {
        var jobId = await Create().QueueAsync(rootId: null, folderId: null, isRecursive: true);

        jobId.Should().Be(50);
        await _jobs.Received(1).AddAsync(
            Arg.Is<Job>(j => j.Kind == JobKind.FaceRecognition && j.Status == JobStatus.Enumerating && j.FolderId == null),
            Arg.Any<CancellationToken>());
        _queue.Received(1).Enqueue(new QueuedFaceRecognition(50, null, true));
        _cancellations.Cancel(50).Should().BeTrue();
    }

    [Fact]
    public async Task QueueAsync_AnotherJobActive_Throws()
    {
        _jobs.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => Create().QueueAsync(null, null, true);

        await act.Should().ThrowAsync<FaceRecognitionAlreadyInProgressException>();
        _queue.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task RunAsync_ProcessesEveryCandidate_ReportsProgress_AndCompletes()
    {
        _faces.GetCandidateImageIdsAsync(3, 20, true, Arg.Any<CancellationToken>()).Returns(new List<int> { 1, 2, 3 });

        await Create().RunAsync(new QueuedFaceRecognition(50, 20, true));

        await _jobs.Received(1).SetEnumerationResultAsync(50, 0, 3, Arg.Any<CancellationToken>());
        await _jobs.Received(1).TryTransitionToEnrichingAsync(50, Arg.Any<CancellationToken>());
        await _processor.Received(3).ProcessAsync(Arg.Any<int>(), 3, Arg.Any<CancellationToken>());
        await _jobs.Received(3).IncrementFaceProgressAsync(50, 1, Arg.Any<CancellationToken>());
        await _clusterer.Received(1).ClusterAsync(3, Arg.Any<CancellationToken>());
        await _jobs.Received(1).TryMarkCompletedAsync(50, JobStatus.Enriching, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CancelledMidway_RecordsCancelledWithCandidateCount_AndRethrows()
    {
        _faces.GetCandidateImageIdsAsync(3, null, true, Arg.Any<CancellationToken>()).Returns(new List<int> { 1, 2, 3, 4 });
        using var cts = new CancellationTokenSource();
        _processor.ProcessAsync(1, 3, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cts.Cancel();
            return new FaceImageResult(FaceImageOutcome.Processed, 0);
        });

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, null, true), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _jobs.Received(1).SetFailureResultAsync(50, 0, 4, null, JobStatus.Cancelled, Arg.Any<DateTime>(), CancellationToken.None);
        await _jobs.DidNotReceiveWithAnyArgs().TryMarkCompletedAsync(default, default, default, default);
    }

    [Fact]
    public async Task RunAsync_AlreadyCancelled_RecordsCancelledWithoutProcessing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, null, true), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default);
        await _jobs.Received(1).SetFailureResultAsync(50, 0, 0, null, JobStatus.Cancelled, Arg.Any<DateTime>(), CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_ModelsMissing_RecordsFailedWithTheMessage()
    {
        _analyzer.Model.Throws(new FaceModelUnavailableException("Face recognition models not found"));

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, null, true));

        await act.Should().ThrowAsync<FaceModelUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(50, 0, 0, "Face recognition models not found", JobStatus.Failed, Arg.Any<DateTime>(), CancellationToken.None);
    }
}
