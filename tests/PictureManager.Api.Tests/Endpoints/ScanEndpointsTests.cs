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
        scanService.StartScanAsync(1, true, Arg.Any<CancellationToken>()).Returns(42);

        var result = await ScanEndpoints.StartScanAsync(new ScanRequest(1, true), scanService, CancellationToken.None);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Ok<ScanStartedResponse>>();
    }

    [Fact]
    public async Task StreamScanEventsAsync_JobAlreadyCompleted_WritesOneEventThenStops()
    {
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new ScanJob
        {
            Id = 1,
            Status = ScanJobStatus.Completed,
            FoldersScanned = 3,
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
    public async Task StreamScanEventsAsync_UnknownJob_WritesErrorEvent()
    {
        var scanJobRepository = Substitute.For<IScanJobRepository>();
        scanJobRepository.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((ScanJob?)null);

        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        await ScanEndpoints.StreamScanEventsAsync(context, 1, scanJobRepository, CancellationToken.None);

        body.Position = 0;
        var written = Encoding.UTF8.GetString(body.ToArray());
        written.Should().Contain("event: error");
    }
}
