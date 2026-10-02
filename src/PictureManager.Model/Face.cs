using System;
using Pgvector;

namespace PictureManager.Model;

/// <summary>A detected face. The box is normalized (0-1) against the orientation-corrected image.</summary>
public class Face
{
    public int Id { get; set; }
    public int ImageId { get; set; }
    public int FaceModelId { get; set; }
    public int? PersonId { get; set; }
    public FaceAssignmentState AssignmentState { get; set; } = FaceAssignmentState.Unassigned;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float DetectionConfidence { get; set; }
    public float QualityScore { get; set; }

    /// <summary>L2-normalized; compared with cosine distance.</summary>
    public Vector Embedding { get; set; } = null!;
    public DateTime CreatedUtc { get; set; }

    /// <summary>When clustering last used this face as a seed; null = not yet (new or re-created by re-processing).</summary>
    public DateTime? ClusteredUtc { get; set; }

    public Image? Image { get; set; }
    public FaceModel? FaceModel { get; set; }
    public Person? Person { get; set; }
}