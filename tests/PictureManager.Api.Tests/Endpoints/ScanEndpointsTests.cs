using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class ScanEndpointsTests
{
    [Fact]
    public async Task StartScanAsync_CallsScanService_AndReturnsScanJobId()
    {
        var scanService = Substitute.For<IScanService>();
        scanService.QueueScanAsync(1, null, true, Arg.Any<CancellationToken>()).Returns(42);

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(1, null, true), scanService, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ScanStartedResponse>>();
    }

    [Fact]
    public async Task StartScanAsync_UnavailableRoot_ReturnsValidationProblem()
    {
        var scanService = Substitute.For<IScanService>();
        scanService.QueueScanAsync(2, null, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ScanRootUnavailableException(2)));

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(2, null, true), scanService, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ValidationProblem>();
    }

    [Fact]
    public async Task StartScanAsync_FolderId_IsPassedThrough()
    {
        var scanService = Substitute.For<IScanService>();
        scanService.QueueScanAsync(null, 20, true, Arg.Any<CancellationToken>()).Returns(42);

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(null, 20, true), scanService, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ScanStartedResponse>>();
        await scanService.Received(1).QueueScanAsync(null, 20, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartScanAsync_UnavailableFolder_ReturnsValidationProblem()
    {
        var scanService = Substitute.For<IScanService>();
        scanService.QueueScanAsync(null, 20, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(FolderUnavailableException.NotFound(20)));

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(null, 20, true), scanService, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ValidationProblem>();
    }

    [Fact]
    public async Task StreamScanEventsAsync_JobAlreadyCompleted_WritesOneEventThenStops()
    {
        var scanJobRepository = Substitute.For<IJobRepository>();
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Job
        {
            Id = 1,
            Status = JobStatus.Completed,
            FoldersProcessed = 3,
            FilesFound = 5,
            FilesEnriched = 5
        });

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ScanEndpoints.StreamScanEventsAsync(context, 1, scanJobRepository, CancellationToken.None);

        body.Position = 0;
        var written = Encoding.UTF8.GetString(body.ToArray());
        written.Should().Contain("\"Status\":\"Completed\"");
    }

    [Fact]
    public async Task StreamScanEventsAsync_FailedJob_IncludesTheErrorMessage()
    {
        var scanJobRepository = Substitute.For<IJobRepository>();
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Job
        {
            Id = 1,
            Status = JobStatus.Failed,
            ErrorMessage = "Root 'dev' is unavailable: its folder is missing or empty. Check that the share is mounted."
        });

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ScanEndpoints.StreamScanEventsAsync(context, 1, scanJobRepository, CancellationToken.None);

        var written = Encoding.UTF8.GetString(body.ToArray());
        written.Should().Contain("\"Status\":\"Failed\"");
        written.Should().Contain("\"ErrorMessage\":");
        written.Should().Contain("is unavailable: its folder is missing or empty");
    }

    [Fact]
    public async Task StreamScanEventsAsync_UnknownJob_WritesErrorEvent()
    {
        var scanJobRepository = Substitute.For<IJobRepository>();
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((Job?)null);

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ScanEndpoints.StreamScanEventsAsync(context, 1, scanJobRepository, CancellationToken.None);

        body.Position = 0;
        var written = Encoding.UTF8.GetString(body.ToArray());
        written.Should().Contain("event: error");
    }
}
