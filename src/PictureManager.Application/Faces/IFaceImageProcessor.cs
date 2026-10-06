using System.Threading;
using System.Threading.Tasks;

namespace PictureManager.Application.Faces;

public enum FaceImageOutcome
{
    Processed,
    Skipped,
    Failed
}

public sealed record FaceImageResult(FaceImageOutcome Outcome, int FacesFound);

/// <summary>Analyzes one image and records the outcome. Scoped: one instance (and DbContext) per image.</summary>
public interface IFaceImageProcessor
{
    Task<FaceImageResult> ProcessAsync(int imageId, int faceModelId, FaceDetectionPreset preset = FaceDetectionPreset.Fast, CancellationToken cancellationToken = default);
}
