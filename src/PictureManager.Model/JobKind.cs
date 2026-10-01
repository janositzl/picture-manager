namespace PictureManager.Model;

/// <summary>Scan first: a job created without a kind, and every row from before this column existed, is a scan.</summary>
public enum JobKind
{
    Scan,
    Discovery,
    FaceRecognition
}
