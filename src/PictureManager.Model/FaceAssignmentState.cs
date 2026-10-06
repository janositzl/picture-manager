namespace PictureManager.Model;

/// <summary>
/// Stored as an int, so the numbers are fixed. Suggested = set by clustering/matching and awaiting the user.
/// Confirmed/Ignored = set by the user; automation never changes them. Unknown = no person (not yet identified).
/// 3 was the old Rejected, now folded into Unknown (+ Face.RejectedPersonId).
/// </summary>
public enum FaceAssignmentState
{
    Unknown = 0,
    Suggested = 1,
    Confirmed = 2,
    Ignored = 4
}
