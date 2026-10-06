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
    public FaceAssignmentState AssignmentState { get; set; } = FaceAssignmentState.Unknown;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float DetectionConfidence { get; set; }
    public float QualityScore { get; set; }

    /// <summary>L2-normalized; compared with cosine distance.</summary>
    public Vector Embedding { get; set; } = null!;

    /// <summary>Cosine distance behind a Suggested assignment (kept for sorting / low-confidence review); null otherwise.</summary>
    public float? MatchDistance { get; set; }

    /// <summary>The person the user last rejected for this face; clustering never suggests them again. No FK on purpose (may be stale).</summary>
    public int? RejectedPersonId { get; set; }
    public DateTime CreatedUtc { get; set; }

    /// <summary>When clustering last used this face as a seed; null = not yet (new or re-created by re-processing).</summary>
    public DateTime? ClusteredUtc { get; set; }

    public Image? Image { get; set; }
    public FaceModel? FaceModel { get; set; }
    public Person? Person { get; set; }
}