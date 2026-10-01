namespace PictureManager.Model;

/// <summary>Auto = set by clustering/matching. Confirmed/Rejected = set by the user; automation never changes them.</summary>
public enum FaceAssignmentState
{
    Unassigned,
    Auto,
    Confirmed,
    Rejected
}