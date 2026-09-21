namespace PictureManager.Model;

public enum ScanJobStatus
{
    Pending,
    Enumerating,
    Enriching,
    Completed,
    Failed,
    Cancelled
}
