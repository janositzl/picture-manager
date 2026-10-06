using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public class FaceReviewServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);

    private readonly IFaceReviewRepository _review = Substitute.For<IFaceReviewRepository>();
    private readonly IPeopleRepository _people = Substitute.For<IPeopleRepository>();
    private readonly IFaceClusterer _clusterer = Substitute.For<IFaceClusterer>();
    private readonly IFaceImageProcessor _processor = Substitute.For<IFaceImageProcessor>();
    private readonly IFaceAnalyzer _analyzer = Substitute.For<IFaceAnalyzer>();
    private readonly IFaceRepository _faceRepo = Substitute.For<IFaceRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public FaceReviewServiceTests()
    {
        _clock.UtcNow.Returns(Now);
        _review.FaceExistsAsync(5, Arg.Any<CancellationToken>()).Returns(true);
        _review.PersonExistsAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        _people.GetAsync(7, Arg.Any<CancellationToken>()).Returns(new PersonSummary(7, "Anna", 3, 1, 5));
    }

    private FaceReviewService Create() => new(_review, _people, _clusterer, _processor, _analyzer, _faceRepo, _clock);

    private void StubModel() =>
        _faceRepo.GetOrCreateModelIdAsync(Arg.Any<FaceModelDescriptor>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(3);

    [Fact]
    public async Task ReanalyzeImage_AnalysesThePhotoWithThePreset_ThenMatchesItsUnknownFaces()
    {
        StubModel();
        _review.GetUnknownFacesAsync(10, Arg.Any<CancellationToken>()).Returns(new[] { new RecheckFace(1, 3, 0.4f, null) });
        _processor.ProcessAsync(10, 3, FaceDetectionPreset.Detailed, Arg.Any<CancellationToken>())
            .Returns(new FaceImageResult(FaceImageOutcome.Processed, 3));
        _clusterer.MatchAsync(3, Arg.Any<IReadOnlyList<FaceCandidate>>(), Arg.Any<CancellationToken>()).Returns(2);

        var result = await Create().ReanalyzeImageAsync(10, FaceDetectionPreset.Detailed);

        result.Status.Should().Be(ResultStatus.Success);
        result.Value.Should().Be(new ReanalyzeImageResponse(3, 2));
    }

    [Fact]
    public async Task ReanalyzeImage_MissingPhoto_IsNotFound_AndNothingRuns()
    {
        _review.GetUnknownFacesAsync(99, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<RecheckFace>?)null);

        (await Create().ReanalyzeImageAsync(99, FaceDetectionPreset.Fast)).Status.Should().Be(ResultStatus.NotFound);
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default, default);
    }

    [Theory]
    [InlineData(FaceImageOutcome.Failed)]
    [InlineData(FaceImageOutcome.Skipped)]
    public async Task ReanalyzeImage_WhenTheAnalysisDoesNotProcessThePhoto_IsInvalid(FaceImageOutcome outcome)
    {
        StubModel();
        _review.GetUnknownFacesAsync(10, Arg.Any<CancellationToken>()).Returns(Array.Empty<RecheckFace>());
        _processor.ProcessAsync(10, 3, Arg.Any<FaceDetectionPreset>(), Arg.Any<CancellationToken>()).Returns(new FaceImageResult(outcome, 0));

        (await Create().ReanalyzeImageAsync(10, FaceDetectionPreset.Fast)).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task ReanalyzeImage_ModelsUnavailable_IsInvalid()
    {
        _review.GetUnknownFacesAsync(10, Arg.Any<CancellationToken>()).Returns(Array.Empty<RecheckFace>());
        _analyzer.Model.Returns(_ => throw new FaceModelUnavailableException("missing"));

        (await Create().ReanalyzeImageAsync(10, FaceDetectionPreset.Fast)).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task RecheckFaces_MatchesTheImagesUnknownFacesPerModel_AndCountsSuggestions()
    {
        _review.GetUnknownFacesAsync(10, Arg.Any<CancellationToken>())
            .Returns(new[] { new RecheckFace(1, 3, 0.4f, null), new RecheckFace(2, 3, 0.5f, 9) });
        _clusterer.MatchAsync(3, Arg.Is<IReadOnlyList<FaceCandidate>>(f => f.Count == 2), Arg.Any<CancellationToken>()).Returns(1);

        var result = await Create().RecheckFacesAsync(10);

        result.Status.Should().Be(ResultStatus.Success);
        result.Value!.Count.Should().Be(1);
    }

    [Fact]
    public async Task RecheckFaces_MissingImage_IsNotFound()
    {
        _review.GetUnknownFacesAsync(99, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<RecheckFace>?)null);
        (await Create().RecheckFacesAsync(99)).Status.Should().Be(ResultStatus.NotFound);
        await _clusterer.DidNotReceiveWithAnyArgs().MatchAsync(default, default!, default);
    }

    [Fact]
    public async Task Accept_FaceInAnotherState_IsStillOk_ButUnknownFaceIsNotFound()
    {
        _review.AcceptAsync(5, Arg.Any<CancellationToken>()).Returns(0);
        _review.AcceptAsync(99, Arg.Any<CancellationToken>()).Returns(0);

        (await Create().AcceptAsync(5)).Status.Should().Be(ResultStatus.Success);
        (await Create().AcceptAsync(99)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task Assign_NeedsExactlyOneOfPersonOrName()
    {
        (await Create().AssignAsync(5, new AssignFaceRequest(null, null))).Status.Should().Be(ResultStatus.Invalid);
        (await Create().AssignAsync(5, new AssignFaceRequest(7, "Anna"))).Status.Should().Be(ResultStatus.Invalid);
        await _review.DidNotReceiveWithAnyArgs().AssignAsync(default, default, default, default);
    }

    [Fact]
    public async Task Assign_ToAPerson_ConfirmsAndReturnsThem()
    {
        var result = await Create().AssignAsync(5, new AssignFaceRequest(7, null));

        result.Value!.Id.Should().Be(7);
        await _review.Received(1).AssignAsync(5, 7, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assign_ByNameOfExistingPerson_UsesThemWithoutCreatingOne()
    {
        _people.FindIdByNameAsync("Anna", Arg.Any<CancellationToken>()).Returns(7);

        await Create().AssignAsync(5, new AssignFaceRequest(null, "  Anna "));

        await _review.DidNotReceiveWithAnyArgs().CreateNamedPersonAsync(default!, default, default, default);
        await _review.Received(1).AssignAsync(5, 7, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assign_ByNewName_CreatesThePersonWithThisFaceAsCover()
    {
        _people.FindIdByNameAsync("Cili", Arg.Any<CancellationToken>()).Returns((int?)null);
        _review.CreateNamedPersonAsync("Cili", 5, Now, Arg.Any<CancellationToken>()).Returns(7);

        await Create().AssignAsync(5, new AssignFaceRequest(null, "Cili"));

        await _review.Received(1).AssignAsync(5, 7, Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assign_UnknownFaceIsNotFound_UnknownPersonIsInvalid()
    {
        (await Create().AssignAsync(99, new AssignFaceRequest(7, null))).Status.Should().Be(ResultStatus.NotFound);
        (await Create().AssignAsync(5, new AssignFaceRequest(8, null))).Status.Should().Be(ResultStatus.Invalid);
    }

    [Fact]
    public async Task GetImageFaces_ConfirmedOnly_DropsEverythingElse_AndStatesAreLowerCase()
    {
        _review.GetImageFacesAsync(1, false, Arg.Any<CancellationToken>()).Returns(new List<ImageFace>
        {
            new(1, 0, 0, 0.1f, 0.1f, FaceAssignmentState.Confirmed, 7, "Anna"),
            new(2, 0.5f, 0, 0.1f, 0.1f, FaceAssignmentState.Suggested, 8, "Bela"),
        });

        var all = await Create().GetImageFacesAsync(1, false, confirmedOnly: false);
        var confirmed = await Create().GetImageFacesAsync(1, false, confirmedOnly: true);

        all.Value!.Select(f => f.State).Should().Equal("confirmed", "suggested");
        confirmed.Value!.Should().ContainSingle().Which.PersonName.Should().Be("Anna");
    }

    [Fact]
    public async Task GetImageFaces_UnknownImage_IsNotFound()
    {
        _review.GetImageFacesAsync(9, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ImageFace>?)null);

        (await Create().GetImageFacesAsync(9, false, false)).Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task AcceptAll_UnknownPerson_IsNotFound_KnownReturnsTheCount()
    {
        _review.AcceptAllAsync(7, Arg.Any<CancellationToken>()).Returns(4);

        (await Create().AcceptAllAsync(8)).Status.Should().Be(ResultStatus.NotFound);
        (await Create().AcceptAllAsync(7)).Value!.Count.Should().Be(4);
    }
}
