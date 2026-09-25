using System;

namespace PictureManager.Application.Scanning;

/// <summary>A job's target folder was gone from disk when the job ran. Nothing was changed.</summary>
public sealed class FolderNotOnDiskException : Exception
{
    public FolderNotOnDiskException(string rootName, string relativePath)
        : base($"Folder is no longer on disk: {rootName}/{relativePath}")
    {
    }
}
