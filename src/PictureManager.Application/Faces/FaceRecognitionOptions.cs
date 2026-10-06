using System;

namespace PictureManager.Application.Faces;

/// <summary>Bound from the "FaceRecognition" configuration section.</summary>
public sealed class FaceRecognitionOptions
{
    /// <summary>Folder holding det_10g.onnx and w600k_r50.onnx. Relative paths resolve against the content root.</summary>
    public string ModelDirectory { get; set; } = "models/buffalo_l";
    public float DetectionThreshold { get; set; } = 0.6f;

    /// <summary>
    /// Side of the square the detector sees (the photo is letterboxed into it) for the Fast and the Detailed preset.
    /// Larger finds smaller faces but costs roughly size². Multiples of 32; photos are decoded to at most 1600px.
    /// </summary>
    public int FastDetectorInputSize { get; set; } = 640;
    public int DetailedDetectorInputSize { get; set; } = 1280;

    /// <summary>Shorter box side, in pixels of the decoded (≤1600px) image, below which a face is ignored (per preset).</summary>
    public int FastMinFaceSizePx { get; set; } = 40;
    public int DetailedMinFaceSizePx { get; set; } = 24;

    /// <summary>Images decoded (NAS reads) concurrently on top of InferenceConcurrency.</summary>
    public int ReadConcurrency { get; set; } = 2;

    /// <summary>Concurrent model executions (CPU-bound).</summary>
    public int InferenceConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Cosine distance for grouping unassigned faces (DBSCAN eps).</summary>
    public float ClusterDistance { get; set; } = 0.5f;

    /// <summary>Stricter cosine distance for attaching a new face to an existing person.</summary>
    public float AutoMatchDistance { get; set; } = 0.4f;
    public int MinFacesPerGroup { get; set; } = 3;
    public float MinQualityForClustering { get; set; } = 0.5f;

    public FaceDetectionSettings For(FaceDetectionPreset preset) => preset == FaceDetectionPreset.Detailed
        ? new FaceDetectionSettings(DetailedDetectorInputSize, DetailedMinFaceSizePx)
        : new FaceDetectionSettings(FastDetectorInputSize, FastMinFaceSizePx);
}
