namespace PictureManager.Model;

/// <summary>Failed = retried by the next run. PermanentlyFailed = given up (undecodable, or too many attempts).</summary>
public enum FaceProcessingStatus
{
    Completed,
    Failed,
    PermanentlyFailed
}