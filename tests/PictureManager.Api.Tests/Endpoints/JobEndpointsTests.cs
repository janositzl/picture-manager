using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Api.Endpoints;
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
        ok.Value.Should().Be(new ActiveJobDto("Scan", 7, 20, "Enumerating", 3, 5, 1, null));
    }
}
