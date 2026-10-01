using System;

namespace PictureManager.Model;

/// <summary>A background job: an image scan, a folder discovery or a face recognition. Only one runs at a time.</summary>
public class Job
{
    public int Id { get; set; }
    public JobKind Kind { get; set; }

    /// <summary>
    /// Scope: null = all active roots. Otherwise the folder the job starts from: a root's top folder for a
    /// whole-root job, or any subfolder.
    /// </summary>
    public int? FolderId { get; set; }
    public bool IsRecursive { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Folders visited so far, for both kinds.</summary>
    public int FoldersProcessed { get; set; }

    /// <summary>Scan only; always 0 for a discovery job.</summary>
    public int FilesFound { get; set; }

    /// <summary>Scan only; always 0 for a discovery job.</summary>
    public int FilesEnriched { get; set; }
    public string? ErrorMessage { get; set; }
    public Folder? Folder { get; set; }
}
