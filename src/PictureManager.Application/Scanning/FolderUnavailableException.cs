using System;

namespace PictureManager.Application.Scanning;

/// <summary>A folder requested as a job's target can't be used: unknown, removed, on an inactive root, or missing.</summary>
public sealed class FolderUnavailableException : Exception
{
    private FolderUnavailableException(int folderId, string message)
        : base(message)
    {
        FolderId = folderId;
    }

    public int FolderId { get; }

    public static FolderUnavailableException NotFound(int folderId) =>
        new(folderId, $"Folder {folderId} does not exist, was removed from the collection, or its root is inactive.");

    public static FolderUnavailableException Missing(int folderId) =>
        new(folderId, $"Folder {folderId} is missing on disk. Scan or discover its parent folder instead.");

    public static FolderUnavailableException Excluded(int folderId) =>
        new(folderId, $"Folder {folderId} is excluded from scans, directly or through a parent folder.");
}
