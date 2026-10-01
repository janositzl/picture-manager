using System;

namespace PictureManager.Model;

/// <summary>
/// Face analysis outcome for one image, valid only while FaceModelId is the current model and ImageFingerprint
/// still equals the image's ContentHash; otherwise the image is a candidate again.
/// </summary>
public class FaceProcessingState
{
    public int ImageId { get; set; }
    public int FaceModelId { get; set; }
    public string ImageFingerprint { get; set; } = string.Empty;
    public FaceProcessingStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ProcessedUtc { get; set; }

    public Image? Image { get; set; }
    public FaceModel? FaceModel { get; set; }
}