using System;

namespace PictureManager.Application.Faces;

/// <summary>How hard the detector looks for faces: Fast is quicker, Detailed also finds small or distant faces.</summary>
public enum FaceDetectionPreset
{
    Fast,
    Detailed
}

/// <summary>Detector settings a preset stands for.</summary>
public sealed record FaceDetectionSettings(int DetectorInputSize, int MinFaceSizePx);

public static class FaceDetectionPresets
{
    /// <summary>Null/blank = fallback; "fast" or "detailed" (any case); anything else is not a preset.</summary>
    public static bool TryParse(string? value, FaceDetectionPreset fallback, out FaceDetectionPreset preset)
    {
        preset = fallback;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out preset) && Enum.IsDefined(preset);
    }
}
