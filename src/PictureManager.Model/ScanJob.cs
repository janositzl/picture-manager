using System;

namespace PictureManager.Model;

public class ScanJob
{
    public int Id { get; set; }
    public int? RootFolderId { get; set; }
    public bool IsRecursive { get; set; }
    public ScanJobStatus Status { get; set; } = ScanJobStatus.Pending;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public int FoldersScanned { get; set; }
    public int FilesFound { get; set; }
    public int FilesEnriched { get; set; }
    public string? ErrorMessage { get; set; }

    public Folder? RootFolder { get; set; }
}
