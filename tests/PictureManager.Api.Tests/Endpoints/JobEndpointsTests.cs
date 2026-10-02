using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class JobEndpointsTests
{
    [Fact]
    public async Task GetActiveJobAsync_NoActiveJob_ReturnsNoContent()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetActiveAsync(Arg.Any<CancellationToken>()).Returns((Job?)null);

        var result = await JobEndpoints.GetActiveJobAsync(jobs, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NoContent>();
    }

    [Fact]
    public async Task GetActiveJobAsync_ActiveJob_ReturnsItsDto()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new Job
        {
            Id = 7,
            Kind = JobKind.Scan,
            FolderId = 20,
            Status = JobStatus.Enumerating,
            FoldersProcessed = 3,
            FilesFound = 5,
            FilesEnriched = 1,
        });

        var result = await JobEndpoints.GetActiveJobAsync(jobs, CancellationToken.None);

        var ok = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ActiveJobDto>>().Subject;
        ok.Value.Should().Be(new ActiveJobDto("Scan", 7, 20, "Enumerating", 3, 5, 1, null, 0));
    }

    [Fact]
    public async Task CancelJobAsync_ActiveFaceJob_CancelsAndReturnsAccepted()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new Job { Id = 9, Kind = JobKind.FaceRecognition, Status = JobStatus.Enriching });
        var registry = new JobCancellationRegistry();
        var token = registry.Register(9);

        var result = await JobEndpoints.CancelJobAsync(9, jobs, registry, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Accepted>();
        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CancelJobAsync_ScanJob_ReturnsConflict()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new Job { Id = 9, Kind = JobKind.Scan, Status = JobStatus.Enumerating });

        var result = await JobEndpoints.CancelJobAsync(9, jobs, new JobCancellationRegistry(), CancellationToken.None);

        result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CancelJobAsync_CompletedJob_ReturnsNotFound()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new Job { Id = 9, Kind = JobKind.FaceRecognition, Status = JobStatus.Completed });
        var registry = new JobCancellationRegistry();
        var token = registry.Register(9);

        var result = await JobEndpoints.CancelJobAsync(9, jobs, registry, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
        token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task CancelJobAsync_UnknownJob_ReturnsNotFound()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.GetByIdAsync(9, Arg.Any<CancellationToken>()).Returns((Job?)null);

        var result = await JobEndpoints.CancelJobAsync(9, jobs, new JobCancellationRegistry(), CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.NotFound>();
    }
}
