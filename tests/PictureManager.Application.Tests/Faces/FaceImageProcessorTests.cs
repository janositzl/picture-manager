using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PictureManager.Application.Common;
using PictureManager.Application.Faces;
using PictureManager.Application.Repositories;
using PictureManager.Model;
using Xunit;

namespace PictureManager.Application.Tests.Faces;

public sealed class FaceImageProcessorTests : IDisposable
{
    private const int ModelId = 3;
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("pm-faces-");
    private readonly IImageRepository _images = Substitute.For<IImageRepository>();
    private readonly IFaceRepository _faces = Substitute.For<IFaceRepository>();
    private readonly IFaceAnalyzer _analyzer = Substitute.For<IFaceAnalyzer>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Image _image;

    public FaceImageProcessorTests()
    {
        _clock.UtcNow.Returns(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        _image = new Image
        {
            Id = 7, FileName = "IMG1", Extension = ".jpg", ContentHash = "hash", Orientation = 1,
            Folder = new Folder { RelativePath = string.Empty, Root = new ImageRoot { MountPath = _root.FullName, IsActive = true } }
        };
        _images.GetByIdWithFolderAsync(7, Arg.Any<CancellationToken>()).Returns(_image);
        _faces.SaveResultAsync(default, default, default!, default!, default, default).ReturnsForAnyArgs(true);
    }

    public void Dispose() => _root.Delete(recursive: true);

    private string Physical => Path.Combine(_root.FullName, "IMG1.jpg");

    private FaceImageProcessor Create() =>
        new(_images, _faces, _analyzer, new FaceRecognitionOptions { MaxAttempts = 3 }, _clock, NullLogger<FaceImageProcessor>.Instance);

    private static DetectedFace AFace() => new(0, 0, 0.5f, 0.5f, 0.9f, 0.8f, new float[512]);

    [Fact]
    public async Task ProcessAsync_FacesFound_SavesThemAndReportsCount()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).Returns(new FaceAnalysisResult(new[] { AFace(), AFace() }));

        var result = await Create().ProcessAsync(7, ModelId);

        result.Should().Be(new FaceImageResult(FaceImageOutcome.Processed, 2));
        await _faces.Received(1).SaveResultAsync(7, ModelId, "hash", Arg.Is<IReadOnlyList<DetectedFace>>(f => f.Count == 2), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_NoFaces_SavesEmptyResult()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).Returns(new FaceAnalysisResult(Array.Empty<DetectedFace>()));

        var result = await Create().ProcessAsync(7, ModelId);

        result.Should().Be(new FaceImageResult(FaceImageOutcome.Processed, 0));
        await _faces.Received(1).SaveResultAsync(7, ModelId, "hash", Arg.Is<IReadOnlyList<DetectedFace>>(f => f.Count == 0), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_FileMissing_SkipsWithoutWritingState()
    {
        var result = await Create().ProcessAsync(7, ModelId);

        result.Outcome.Should().Be(FaceImageOutcome.Skipped);
        await _analyzer.DidNotReceiveWithAnyArgs().AnalyzeAsync(default!, default, default);
        await _faces.DidNotReceiveWithAnyArgs().SaveFailureAsync(default, default, default!, default, default, default, default, default);
        await _faces.DidNotReceiveWithAnyArgs().SaveResultAsync(default, default, default!, default!, default, default);
    }

    [Fact]
    public async Task ProcessAsync_IoError_RecordsRetryableFailure()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("network name no longer available"));

        var result = await Create().ProcessAsync(7, ModelId);

        result.Outcome.Should().Be(FaceImageOutcome.Failed);
        await _faces.Received(1).SaveFailureAsync(7, ModelId, "hash", FaceProcessingStatus.Failed, 1, "network name no longer available", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ThirdFailureForSameContent_BecomesPermanent()
    {
        File.WriteAllText(Physical, "x");
        _faces.GetStateAsync(7, Arg.Any<CancellationToken>()).Returns(new FaceProcessingState
        {
            ImageId = 7, FaceModelId = ModelId, ImageFingerprint = "hash", Status = FaceProcessingStatus.Failed, Attempts = 2
        });
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("again"));

        await Create().ProcessAsync(7, ModelId);

        await _faces.Received(1).SaveFailureAsync(7, ModelId, "hash", FaceProcessingStatus.PermanentlyFailed, 3, "again", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_PreviousFailureForOtherContent_RestartsTheCount()
    {
        File.WriteAllText(Physical, "x");
        _faces.GetStateAsync(7, Arg.Any<CancellationToken>()).Returns(new FaceProcessingState
        {
            ImageId = 7, FaceModelId = ModelId, ImageFingerprint = "older", Status = FaceProcessingStatus.Failed, Attempts = 2
        });
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("x"));

        await Create().ProcessAsync(7, ModelId);

        await _faces.Received(1).SaveFailureAsync(7, ModelId, "hash", FaceProcessingStatus.Failed, 1, "x", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_Undecodable_IsPermanentImmediately()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).Returns((FaceAnalysisResult?)null);

        await Create().ProcessAsync(7, ModelId);

        await _faces.Received(1).SaveFailureAsync(7, ModelId, "hash", FaceProcessingStatus.PermanentlyFailed, 1, "The image could not be decoded.", Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ImageDeletedBeforeSave_IsSkipped()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).Returns(new FaceAnalysisResult(new[] { AFace() }));
        _faces.SaveResultAsync(default, default, default!, default!, default, default).ReturnsForAnyArgs(false);

        (await Create().ProcessAsync(7, ModelId)).Should().Be(new FaceImageResult(FaceImageOutcome.Skipped, 0));
    }

    [Fact]
    public async Task ProcessAsync_ModelUnavailable_Propagates()
    {
        File.WriteAllText(Physical, "x");
        _analyzer.AnalyzeAsync(Physical, 1, Arg.Any<CancellationToken>()).ThrowsAsync(new FaceModelUnavailableException("missing"));

        var act = () => Create().ProcessAsync(7, ModelId);

        await act.Should().ThrowAsync<FaceModelUnavailableException>();
    }
}
