using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Faces;

public sealed record FaceModelDescriptor(string Name, string Version, int EmbeddingDimensions, string ModelHash);

/// <summary>Box normalized 0-1 against the displayed image: EXIF-corrected, then turned by the user's thumbnail rotation. Embedding is L2-normalized.</summary>
public sealed record DetectedFace(float X, float Y, float Width, float Height, float DetectionConfidence, float QualityScore, float[] Embedding);

public sealed record FaceAnalysisResult(IReadOnlyList<DetectedFace> Faces);

/// <summary>Face detection + embedding. Nothing model-specific leaks past this interface.</summary>
public interface IFaceAnalyzer
{
    /// <summary>Loads the models on first use. Throws FaceModelUnavailableException when they're missing.</summary>
    FaceModelDescriptor Model { get; }

    /// <summary>The faces in the image, seen after the extra clockwise `rotation` (0, 90, 180 or 270); an empty list if none; null if the file can't be decoded as an image.</summary>
    Task<FaceAnalysisResult?> AnalyzeAsync(string imagePath, int? orientation, FaceDetectionPreset preset = FaceDetectionPreset.Fast, CancellationToken cancellationToken = default, int rotation = 0);
}
