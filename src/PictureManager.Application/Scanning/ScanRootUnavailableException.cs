using System;

namespace PictureManager.Application.Scanning;

/// <summary>An explicitly requested scan root doesn't exist or is inactive (inactive roots are never scanned).</summary>
public sealed class ScanRootUnavailableException : Exception
{
    public ScanRootUnavailableException(int rootId)
        : base($"Image root {rootId} does not exist or is inactive.")
    {
        RootId = rootId;
    }

    public int RootId { get; }
}
