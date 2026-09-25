using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Scanning;

public sealed class ScanServiceQueueTests : IDisposable
{
    private readonly DirectoryInfo _tempRoot = Directory.CreateTempSubdirectory("pm-scan-queue-");
    private readonly IImageRootRepository _roots = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folders = Substitute.For<IFolderRepository>();
    private readonly IImageRepository _images = Substitute.For<IImageRepository>();
    private readonly IAppSettingsRepository _settings = Substitute.For<IAppSettingsRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IScanQueue _scanQueue = Substitute.For<IScanQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ScanServiceQueueTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = true });
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(new AppSettings());
        _jobs.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var job = call.Arg<Job>();
            job.Id = 999;
            return job;
        });
    }

    public void Dispose() => _tempRoot.Delete(recursive: true);

    private ScanService CreateService() =>
        new(_roots, _folders, _images, _settings, _jobs, Substitute.For<IEnrichmentQueue>(), _scanQueue, _clock, new ScanningOptions());

    [Fact]
    public async Task QueueScanAsync_CreatesEnumeratingJob_EnqueuesIt_AndDoesNotWalk()
    {
        await File.WriteAllBytesAsync(Path.Combine(_tempRoot.FullName, "a.jpg"), new byte[] { 1 });

        var scanJobId = await CreateService().QueueScanAsync(rootId: 1, isRecursive: true);

        scanJobId.Should().Be(999);
        await _jobs.Received(1).AddAsync(Arg.Is<Job>(j => j.Status == JobStatus.Enumerating && j.IsRecursive), Arg.Any<CancellationToken>());
        _scanQueue.Received(1).Enqueue(new QueuedScan(999, 1, true));
        await _folders.DidNotReceive().GetByRootAndRelativePathAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueueScanAsync_ScanAlreadyActive_ThrowsAndEnqueuesNothing()
    {
        _jobs.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        var act = () => CreateService().QueueScanAsync(rootId: 1, isRecursive: true);

        await act.Should().ThrowAsync<ScanAlreadyInProgressException>();
        _scanQueue.DidNotReceive().Enqueue(Arg.Any<QueuedScan>());
    }

    [Fact]
    public async Task RunScanAsync_RootDeactivatedWhileQueued_FailsJobWithMessage()
    {
        _roots.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ImageRoot { Id = 1, Name = "dev", MountPath = _tempRoot.FullName, IsActive = false });

        var act = () => CreateService().RunScanAsync(new QueuedScan(999, 1, true));

        await act.Should().ThrowAsync<ScanRootUnavailableException>();
        await _jobs.Received(1).SetFailureResultAsync(999, 0, 0, "Image root 1 does not exist or is inactive.",
            JobStatus.Failed, _clock.UtcNow, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailInterruptedJobsAsync_FailsActiveJobsWithTheRestartMessage()
    {
        // Hoisted: reading a substitute's property while building another substitute's call spec confuses NSubstitute.
        var now = _clock.UtcNow;
        _jobs.FailActiveJobsAsync("Interrupted by an application restart.", now, Arg.Any<CancellationToken>()).Returns(2);

        (await CreateService().FailInterruptedJobsAsync()).Should().Be(2);
    }
}
