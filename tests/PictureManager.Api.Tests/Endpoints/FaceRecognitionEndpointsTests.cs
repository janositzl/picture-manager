using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PictureManager.Api.Endpoints;
using PictureManager.Application.Faces;
using PictureManager.Application.Scanning;
using Xunit;

namespace PictureManager.Api.Tests.Endpoints;

public class FaceRecognitionEndpointsTests
{
    [Fact]
    public async Task StartAsync_Queued_ReturnsJobId()
    {
        var service = Substitute.For<IFaceRecognitionService>();
        service.QueueAsync(null, 20, true, false, FaceDetectionPreset.Fast, Arg.Any<CancellationToken>()).Returns(77);

        var result = await FaceRecognitionEndpoints.StartAsync(new FaceRecognitionRequest(null, 20, true), service, CancellationToken.None);

        result.Should().BeOfType<Ok<FaceRecognitionStartedResponse>>().Which.Value.Should().Be(new FaceRecognitionStartedResponse(77));
    }

    [Fact]
    public async Task StartAsync_Reanalyze_IsPassedToTheService()
    {
        var service = Substitute.For<IFaceRecognitionService>();
        service.QueueAsync(null, 20, false, true, FaceDetectionPreset.Detailed, Arg.Any<CancellationToken>()).Returns(78);

        var result = await FaceRecognitionEndpoints.StartAsync(new FaceRecognitionRequest(null, 20, false, true, "detailed"), service, CancellationToken.None);

        result.Should().BeOfType<Ok<FaceRecognitionStartedResponse>>().Which.Value.Should().Be(new FaceRecognitionStartedResponse(78));
    }

    [Fact]
    public async Task StartAsync_UnknownPreset_ReturnsValidationProblemOnPreset()
    {
        var service = Substitute.For<IFaceRecognitionService>();

        var result = await FaceRecognitionEndpoints.StartAsync(new FaceRecognitionRequest(null, 20, true, false, "slow"), service, CancellationToken.None);

        result.Should().BeOfType<ValidationProblem>().Which.ProblemDetails.Errors.Should().ContainKey("preset");
        await service.DidNotReceiveWithAnyArgs().QueueAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task StartAsync_AnotherJobActive_ReturnsConflict()
    {
        var service = Substitute.For<IFaceRecognitionService>();
        service.QueueAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<FaceDetectionPreset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FaceRecognitionAlreadyInProgressException());

        var result = await FaceRecognitionEndpoints.StartAsync(new FaceRecognitionRequest(null, null, true), service, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task StartAsync_UnavailableFolder_ReturnsValidationProblemOnFolderId()
    {
        var service = Substitute.For<IFaceRecognitionService>();
        service.QueueAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<FaceDetectionPreset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(FolderUnavailableException.Missing(20));

        var result = await FaceRecognitionEndpoints.StartAsync(new FaceRecognitionRequest(null, 20, true), service, CancellationToken.None);

        result.Should().BeOfType<ValidationProblem>().Which.ProblemDetails.Errors.Should().ContainKey("folderId");
    }

    [Fact]
    public async Task GetCoverageAsync_ReturnsTheServicesCoverage()
    {
        var service = Substitute.For<IFaceRecognitionService>();
        IReadOnlyList<FolderFaceCoverage> coverage = new[] { new FolderFaceCoverage(2, 4, 1, 1, 1) };
        service.GetFolderCoverageAsync(Arg.Any<CancellationToken>()).Returns(coverage);

        var result = await FaceRecognitionEndpoints.GetCoverageAsync(service, CancellationToken.None);

        result.Should().BeOfType<Ok<IReadOnlyList<FolderFaceCoverage>>>().Which.Value.Should().Equal(coverage);
    }
}
