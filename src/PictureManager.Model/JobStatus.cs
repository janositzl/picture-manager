namespace PictureManager.Model;

/// <summary>
/// Enumerating = walking (folder names for Discovery; folders and files for Scan). Enriching is Scan only; a
/// discovery goes from Enumerating straight to a terminal status.
/// </summary>
public enum JobStatus
{
    Pending,
    Enumerating,
    Enriching,
    Completed,
    Failed,
    Cancelled
}
