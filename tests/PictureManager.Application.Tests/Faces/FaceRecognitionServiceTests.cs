using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceRecognitionServiceTests : IDisposable
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
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"pm-face-roots-{Guid.NewGuid():N}");

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

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>A root whose mount folder exists with a file in it (mounted) or doesn't exist (share offline).</summary>
    private ImageRoot Root(int id, string name, bool mounted)
    {
        var mountPath = Path.Combine(_tempDir, name);
        if (mounted)
        {
            Directory.CreateDirectory(mountPath);
            File.WriteAllText(Path.Combine(mountPath, "a.jpg"), "x");
        }

        var root = new ImageRoot { Id = id, Name = name, MountPath = mountPath, IsActive = true };
        _roots.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(root);
        return root;
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

    [Fact]
    public async Task RunAsync_AllRootsUnavailable_FailsAtOnceWithTheMessage_ProcessesNothing()
    {
        var roots = new List<ImageRoot> { Root(1, "nas", mounted: false), Root(2, "usb", mounted: false) };
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(roots);
        _faces.GetCandidateImageIdsAsync(3, null, true, Arg.Any<CancellationToken>()).Returns(new List<int> { 1, 2 });

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, null, true));

        var thrown = await act.Should().ThrowAsync<ScanRootsUnavailableException>();
        thrown.Which.RootNames.Should().Equal("nas", "usb");
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default);
        await _clusterer.DidNotReceiveWithAnyArgs().ClusterAsync(default, default);
        await _jobs.Received(1).SetFailureResultAsync(
            50, 0, 0, Arg.Is<string?>(m => m!.Contains("'nas'") && m.Contains("'usb'")), JobStatus.Failed, Arg.Any<DateTime>(), CancellationToken.None);
        await _jobs.DidNotReceiveWithAnyArgs().TryMarkCompletedAsync(default, default, default, default);
    }

    [Fact]
    public async Task RunAsync_FolderOnUnavailableRoot_FailsAtOnce()
    {
        Root(1, "nas", mounted: false);
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 20, RootId = 1, RelativePath = "Trips" });

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, 20, true));

        (await act.Should().ThrowAsync<ScanRootsUnavailableException>()).Which.RootNames.Should().Equal("nas");
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default);
        await _clusterer.DidNotReceiveWithAnyArgs().ClusterAsync(default, default);
    }

    [Fact]
    public async Task RunAsync_SomeRootsUnavailable_ProcessesAndClusters_ThenFailsNamingThem()
    {
        var inactive = Root(3, "old", mounted: false);
        inactive.IsActive = false;
        var roots = new List<ImageRoot> { Root(1, "nas", mounted: true), Root(2, "usb", mounted: false), inactive };
        _roots.GetAllAsync(Arg.Any<CancellationToken>()).Returns(roots);
        _faces.GetCandidateImageIdsAsync(3, null, true, Arg.Any<CancellationToken>()).Returns(new List<int> { 1, 2 });

        var act = () => Create().RunAsync(new QueuedFaceRecognition(50, null, true));

        (await act.Should().ThrowAsync<ScanRootsUnavailableException>()).Which.RootNames.Should().Equal("usb");
        await _processor.Received(2).ProcessAsync(Arg.Any<int>(), 3, Arg.Any<CancellationToken>());
        await _clusterer.Received(1).ClusterAsync(3, Arg.Any<CancellationToken>());
        await _jobs.Received(1).SetFailureResultAsync(
            50, 0, 2, Arg.Is<string?>(m => m!.Contains("'usb'") && !m.Contains("'nas'")), JobStatus.Failed, Arg.Any<DateTime>(), CancellationToken.None);
        await _jobs.DidNotReceiveWithAnyArgs().TryMarkCompletedAsync(default, default, default, default);
    }

    [Fact]
    public async Task RunAsync_AllRootsAvailable_Completes()
    {
        Root(1, "nas", mounted: true);
        _folders.GetByIdAsync(20, Arg.Any<CancellationToken>()).Returns(new Folder { Id = 20, RootId = 1, RelativePath = "Trips" });
        _faces.GetCandidateImageIdsAsync(3, 20, true, Arg.Any<CancellationToken>()).Returns(new List<int> { 1 });

        await Create().RunAsync(new QueuedFaceRecognition(50, 20, true));

        await _processor.Received(1).ProcessAsync(1, 3, Arg.Any<CancellationToken>());
        await _clusterer.Received(1).ClusterAsync(3, Arg.Any<CancellationToken>());
        await _jobs.Received(1).TryMarkCompletedAsync(50, JobStatus.Enriching, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _jobs.DidNotReceiveWithAnyArgs().SetFailureResultAsync(default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task GetFolderCoverageAsync_RollsUpTheCurrentModelsCounts()
    {
        _faces.GetFolderFaceCountsAsync(3, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new FolderFaceCounts(1, null, 1, 1, 0, 0),
            new FolderFaceCounts(2, 1, 4, 1, 1, 1),
        });

        var coverage = await Create().GetFolderCoverageAsync();

        coverage.Should().Equal(new FolderFaceCoverage(1, 5, 2, 1, 1), new FolderFaceCoverage(2, 4, 1, 1, 1));
    }

    [Fact]
    public async Task GetFolderCoverageAsync_ModelsUnavailable_ReturnsEmpty()
    {
        _analyzer.Model.Throws(new FaceModelUnavailableException("missing"));

        var coverage = await Create().GetFolderCoverageAsync();

        coverage.Should().BeEmpty();
        await _faces.DidNotReceive().GetFolderFaceCountsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
