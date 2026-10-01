using System;
using System.Collections.Generic;
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

public class ScanServiceRequeueTests
{
    private readonly IImageRootRepository _imageRootRepository = Substitute.For<IImageRootRepository>();
    private readonly IFolderRepository _folderRepository = Substitute.For<IFolderRepository>();
    private readonly IImageRepository _imageRepository = Substitute.For<IImageRepository>();
    private readonly IAppSettingsRepository _appSettingsRepository = Substitute.For<IAppSettingsRepository>();
    private readonly IJobRepository _scanJobRepository = Substitute.For<IJobRepository>();
    private readonly IEnrichmentQueue _enrichmentQueue = Substitute.For<IEnrichmentQueue>();
    private readonly IScanQueue _scanQueue = Substitute.For<IScanQueue>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ScanServiceRequeueTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private ScanService CreateService() => new(
        _imageRootRepository, _folderRepository, _imageRepository, _appSettingsRepository,
        _scanJobRepository, _enrichmentQueue, _scanQueue, _clock,
        new ScanningOptions { SupportedExtensions = new List<string>() });

    [Fact]
    public async Task RequeueStalledEnrichmentAsync_NothingPending_ReturnsZero_AndCreatesNoJob()
    {
        _scanJobRepository.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(false);
        _imageRepository.GetPendingImageIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int>());

        (await CreateService().RequeueStalledEnrichmentAsync()).Should().Be(0);
        await _scanJobRepository.DidNotReceive().AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        _enrichmentQueue.DidNotReceiveWithAnyArgs().Enqueue(default, default);
    }

    [Fact]
    public async Task RequeueStalledEnrichmentAsync_JobAlreadyActive_ReturnsZero_AndCreatesNoJob()
    {
        _scanJobRepository.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().RequeueStalledEnrichmentAsync()).Should().Be(0);
        await _imageRepository.DidNotReceive().GetPendingImageIdsAsync(Arg.Any<CancellationToken>());
        await _scanJobRepository.DidNotReceive().AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequeueStalledEnrichmentAsync_PendingImages_CreatesEnrichingJob_AndEnqueuesEach()
    {
        _scanJobRepository.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(false);
        _imageRepository.GetPendingImageIdsAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 5, 6, 7 });
        _scanJobRepository.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            var job = callInfo.Arg<Job>();
            job.Id = 42;
            return job;
        });

        (await CreateService().RequeueStalledEnrichmentAsync()).Should().Be(3);

        await _scanJobRepository.Received(1).AddAsync(
            Arg.Is<Job>(j => j.Kind == JobKind.Scan && j.FolderId == null && j.Status == JobStatus.Enriching
                && j.FilesFound == 3 && j.StartedUtc == _clock.UtcNow),
            Arg.Any<CancellationToken>());
        _enrichmentQueue.Received(1).Enqueue(42, 5);
        _enrichmentQueue.Received(1).Enqueue(42, 6);
        _enrichmentQueue.Received(1).Enqueue(42, 7);
    }

    [Fact]
    public async Task RequeueMissingPerceptualHashAsync_JobAlreadyActive_DoesNothing()
    {
        _scanJobRepository.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(true);

        (await CreateService().RequeueMissingPerceptualHashAsync()).Should().Be(0);
        await _imageRepository.DidNotReceive().GetIdsMissingPerceptualHashAsync(Arg.Any<CancellationToken>());
        await _scanJobRepository.DidNotReceive().AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequeueMissingPerceptualHashAsync_NothingMissing_ReturnsZero_AndCreatesNoJob()
    {
        _scanJobRepository.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(false);
        _imageRepository.GetIdsMissingPerceptualHashAsync(Arg.Any<CancellationToken>()).Returns(new List<int>());

        (await CreateService().RequeueMissingPerceptualHashAsync()).Should().Be(0);
        await _scanJobRepository.DidNotReceive().AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequeueMissingPerceptualHashAsync_EnqueuesIdsUnderOneEnrichingJob()
    {
        _scanJobRepository.HasActiveJobAsync(Arg.Any<CancellationToken>()).Returns(false);
        _imageRepository.GetIdsMissingPerceptualHashAsync(Arg.Any<CancellationToken>()).Returns(new List<int> { 5, 6, 7 });
        _scanJobRepository.AddAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            var job = callInfo.Arg<Job>();
            job.Id = 42;
            return job;
        });

        (await CreateService().RequeueMissingPerceptualHashAsync()).Should().Be(3);

        await _scanJobRepository.Received(1).AddAsync(
            Arg.Is<Job>(j => j.Kind == JobKind.Scan && j.FolderId == null && j.Status == JobStatus.Enriching
                && j.FilesFound == 3 && j.StartedUtc == _clock.UtcNow),
            Arg.Any<CancellationToken>());
        _enrichmentQueue.Received(1).Enqueue(42, 5);
        _enrichmentQueue.Received(1).Enqueue(42, 6);
        _enrichmentQueue.Received(1).Enqueue(42, 7);
    }
}
