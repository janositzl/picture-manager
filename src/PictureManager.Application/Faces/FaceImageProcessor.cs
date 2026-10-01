using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PictureManager.Application.Common;
using PictureManager.Application.Repositories;
using PictureManager.Application.Scanning;
using PictureManager.Model;

namespace PictureManager.Application.Faces;

public sealed class FaceImageProcessor : IFaceImageProcessor
{
    private const string UndecodableMessage = "The image could not be decoded.";

    private readonly IImageRepository _images;
    private readonly IFaceRepository _faces;
    private readonly IFaceAnalyzer _analyzer;
    private readonly FaceRecognitionOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<FaceImageProcessor> _logger;

    public FaceImageProcessor(
        IImageRepository images, IFaceRepository faces, IFaceAnalyzer analyzer, FaceRecognitionOptions options, IClock clock,
        ILogger<FaceImageProcessor> logger)
    {
        _images = images;
        _faces = faces;
        _analyzer = analyzer;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task<FaceImageResult> ProcessAsync(int imageId, int faceModelId, CancellationToken cancellationToken = default)
    {
        var image = await _images.GetByIdWithFolderAsync(imageId, cancellationToken);
        if (image?.Folder?.Root is null)
            return new FaceImageResult(FaceImageOutcome.Skipped, 0);

        var path = ImagePathResolver.ResolvePhysicalPath(
            image.Folder.Root.MountPath, image.Folder.RelativePath, image.FileName, image.Extension);

        // Gone, or the share is offline: not this job's business (a scan marks it missing). No state is written,
        // so an unmounted NAS never burns attempts.
        if (!File.Exists(path))
            return new FaceImageResult(FaceImageOutcome.Skipped, 0);

        FaceAnalysisResult? analysis;
        try
        {
            analysis = await _analyzer.AnalyzeAsync(path, image.Orientation, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not FaceModelUnavailableException)
        {
            _logger.LogWarning(ex, "Face analysis failed for image {ImageId}", imageId);
            await RecordFailureAsync(image, faceModelId, ScanTargets.TruncateErrorMessage(ex.Message), permanent: false, cancellationToken);
            return new FaceImageResult(FaceImageOutcome.Failed, 0);
        }

        if (analysis is null)
        {
            await RecordFailureAsync(image, faceModelId, UndecodableMessage, permanent: true, cancellationToken);
            return new FaceImageResult(FaceImageOutcome.Failed, 0);
        }

        var saved = await _faces.SaveResultAsync(imageId, faceModelId, image.ContentHash, analysis.Faces, _clock.UtcNow, cancellationToken);
        return saved
            ? new FaceImageResult(FaceImageOutcome.Processed, analysis.Faces.Count)
            : new FaceImageResult(FaceImageOutcome.Skipped, 0);
    }

    private async Task RecordFailureAsync(Image image, int faceModelId, string? message, bool permanent, CancellationToken cancellationToken)
    {
        // Attempts only accumulate for the same model and the same content; anything else restarts the count.
        var previous = await _faces.GetStateAsync(image.Id, cancellationToken);
        var priorAttempts = previous is not null && previous.FaceModelId == faceModelId && previous.ImageFingerprint == image.ContentHash
            ? previous.Attempts
            : 0;
        var attempts = priorAttempts + 1;
        var status = permanent || attempts >= _options.MaxAttempts
            ? FaceProcessingStatus.PermanentlyFailed
            : FaceProcessingStatus.Failed;

        await _faces.SaveFailureAsync(image.Id, faceModelId, image.ContentHash, status, attempts, message, _clock.UtcNow, cancellationToken);
    }
}
